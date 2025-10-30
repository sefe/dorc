using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core.Events;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Sources.Interfaces;
using log4net;
using System.Text;

namespace Dorc.Monitor
{
    internal class ComponentProcessor : IComponentProcessor
    {
        private readonly IScriptDispatcher scriptDispatcher;

        private readonly IRequestsPersistentSource requestsPersistentSource;
        private readonly IComponentsPersistentSource componentsPersistentSource;
        private readonly IDeploymentEventsPublisher eventsPublisher;
        private ILog _logger;

        public ComponentProcessor(
            IScriptDispatcher scriptDispatcher,
            IRequestsPersistentSource requestsPersistentSource,
            IComponentsPersistentSource componentsPersistentSource,
            ILog Logger,
            IDeploymentEventsPublisher eventsPublisher)
        {
            _logger = Logger;
            this.scriptDispatcher = scriptDispatcher;
            this.requestsPersistentSource = requestsPersistentSource;
            this.componentsPersistentSource = componentsPersistentSource;
            this.eventsPublisher = eventsPublisher;
        }

        public bool DeployComponent(ComponentApiModel component,
            DeploymentResultApiModel deploymentResult,
            int requestId,
            bool isProductionRequest,
            int environmentId,
            bool isProductionEnvironment,
            string environmentName,
            string scriptRoot,
            IDictionary<string, VariableValue> commonProperties,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.Info($"Deploying component with the name '{component.ComponentName}' and id '{component.ComponentId}'.");

            var script = GetScripts(
                component.ComponentId);


            var deploymentResultStatus = DeploymentResultStatus.StatusNotSet;

            StringBuilder componentResultLogBuilder = new StringBuilder();

            try
            {
                requestsPersistentSource.UpdateResultStatus(
                    deploymentResult!,
                    DeploymentResultStatus.Running);

                eventsPublisher.PublishResultStatusChangedAsync(new DeploymentResultEventData(deploymentResult)
                {
                    Status = DeploymentResultStatus.Running.ToString()
                });

                if (isProductionEnvironment)
                {
                    if (script.NonProdOnly)
                    {
                        deploymentResultStatus = DeploymentResultStatus.Warning;

                        var warningMessage = $"SCRIPT '{script.Path}' IS SET TO RUN FOR NON PROD ENVIRONMENTS ONLY! SKIPPED THIS SCRIPT EXECUTION!";
                        _logger.Warn(warningMessage);
                        componentResultLogBuilder.AppendLine(warningMessage);

                        requestsPersistentSource.UpdateResultLog(
                            deploymentResult,
                            componentResultLogBuilder.ToString());
                        requestsPersistentSource.UpdateResultStatus(
                            deploymentResult,
                            deploymentResultStatus);

                        eventsPublisher.PublishResultStatusChangedAsync(new DeploymentResultEventData(deploymentResult)
                        {
                            Status = deploymentResultStatus.ToString()
                        });

                        componentsPersistentSource.SaveEnvComponentStatus(
                            environmentId,
                            component,
                            deploymentResultStatus.ToString(),
                            requestId);

                        return true;
                    }
                }

                bool isSuccessful = scriptDispatcher.Dispatch(
                             scriptRoot,
                             script,
                             commonProperties,
                             requestId,
                             deploymentResult.Id,
                             isProductionRequest,
                             environmentName,
                             componentResultLogBuilder,
                             cancellationToken);

                if (isSuccessful
                    && deploymentResultStatus != DeploymentResultStatus.Warning)
                {
                    _logger.Info($"Processing of the component '{component.ComponentName}' completed.");

                    deploymentResultStatus = DeploymentResultStatus.Complete;
                }
                else
                {
                    _logger.Info($"Processing of the component '{component.ComponentName}' failed.");

                    deploymentResultStatus = DeploymentResultStatus.Failed;
                }
            }
            catch (OperationCanceledException)
            {
                deploymentResultStatus = DeploymentResultStatus.Cancelled;

                _logger.Info($"Processing of the component '{component.ComponentName}' is cancelled.");
                throw;
            }
            catch (Exception)
            {
                deploymentResultStatus = DeploymentResultStatus.Failed;

                _logger.Error($"Processing of the component '{component.ComponentName}' failed.");
                throw;
            }
            finally
            {
                deploymentResult.Log = componentResultLogBuilder.ToString();
                requestsPersistentSource.UpdateResultStatus(
                    deploymentResult,
                    deploymentResultStatus);

                componentsPersistentSource.SaveEnvComponentStatus(
                    environmentId,
                    component,
                    deploymentResultStatus.ToString(),
                    requestId);

                eventsPublisher.PublishResultStatusChangedAsync(new DeploymentResultEventData(deploymentResult)
                {
                    Status = deploymentResultStatus.ToString()
                });
            }

            return deploymentResultStatus != DeploymentResultStatus.Failed;
        }

        private ScriptApiModel GetScripts(
            int? enabledComponentId)
        {
            if (!enabledComponentId.HasValue)
            {
                throw new InvalidOperationException("Component ID is not specified.");
            }

            return componentsPersistentSource.GetScripts(enabledComponentId.Value);
        }
    }
}
