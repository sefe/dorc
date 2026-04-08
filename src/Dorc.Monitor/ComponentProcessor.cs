using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core.Events;
using Dorc.Core.Interfaces;
using Dorc.Monitor.RunnerProcess;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text;

namespace Dorc.Monitor
{
    internal class ComponentProcessor : IComponentProcessor
    {
        private readonly IScriptDispatcher scriptDispatcher;
        private readonly ITerraformDispatcher terraformDispatcher;

        private readonly IRequestsPersistentSource requestsPersistentSource;
        private readonly IComponentsPersistentSource componentsPersistentSource;
        private readonly IDeploymentEventsPublisher eventsPublisher;
        private readonly IConfigValuesPersistentSource configValuesPersistentSource;
        private ILogger _logger;

        public ComponentProcessor(
            IScriptDispatcher scriptDispatcher,
            ITerraformDispatcher terraformDispatcher,
            IRequestsPersistentSource requestsPersistentSource,
            IComponentsPersistentSource componentsPersistentSource,
            IDeploymentEventsPublisher eventsPublisher,
            IConfigValuesPersistentSource configValuesPersistentSource,
            ILogger<ComponentProcessor> Logger)
        {
            _logger = Logger;
            this.scriptDispatcher = scriptDispatcher;
            this.terraformDispatcher = terraformDispatcher;
            this.requestsPersistentSource = requestsPersistentSource;
            this.componentsPersistentSource = componentsPersistentSource;
            this.eventsPublisher = eventsPublisher;
            this.configValuesPersistentSource = configValuesPersistentSource;
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

            _logger.LogInformation($"Deploying component with the name '{component.ComponentName}' and id '{component.ComponentId}' of type '{component.ComponentType}' (Enum Value: {(int)component.ComponentType}).");

            var deploymentResultStatus = DeploymentResultStatus.StatusNotSet;
            StringBuilder componentResultLogBuilder = new StringBuilder();

            try
            {

                bool isSuccessful;

                // Route to appropriate dispatcher based on component type
                switch (component.ComponentType)
                {
                    case ComponentType.Terraform:
                        _logger.LogInformation($"Processing Terraform component '{component.ComponentName}' - routing to TerraformDispatcher.");

                        TerraformRunnerOperations terreformOperation = TerraformRunnerOperations.None;
                        if (deploymentResult.Status.Equals(DeploymentResultStatus.Pending.Value))
                            terreformOperation = TerraformRunnerOperations.CreatePlan;
                        else if (deploymentResult.Status.Equals(DeploymentResultStatus.Confirmed.Value))
                            terreformOperation = TerraformRunnerOperations.ApplyPlan;

                        requestsPersistentSource.UpdateResultStatus(
                            deploymentResult!,
                            DeploymentResultStatus.Running);

                        eventsPublisher.PublishResultStatusChangedAsync(new DeploymentResultEventData(deploymentResult)
                        {
                            Status = DeploymentResultStatus.Running.ToString()
                        });

                        isSuccessful = terraformDispatcher.Dispatch(
                            component,
                            deploymentResult,
                            commonProperties,
                            requestId,
                            isProductionRequest,
                            environmentName,
                            componentResultLogBuilder,
                            terreformOperation,
                            cancellationToken);

                        // For Terraform components, if successful, the status should be WaitingConfirmation
                        if (isSuccessful)
                        {
                            deploymentResultStatus = terreformOperation switch
                            {
                                TerraformRunnerOperations.CreatePlan => DeploymentResultStatus.WaitingConfirmation,
                                TerraformRunnerOperations.ApplyPlan => DeploymentResultStatus.Complete,
                                TerraformRunnerOperations.None => throw new NotImplementedException()
                            };
                            _logger.LogInformation($"Terraform component '{component.ComponentName}' plan created, waiting for confirmation.");
                        }
                        else
                        {
                            deploymentResultStatus = DeploymentResultStatus.Failed;
                            _logger.LogError($"Terraform component '{component.ComponentName}' plan creation failed.");
                        }
                        break;
                    case ComponentType.PowerShell:
                        requestsPersistentSource.UpdateResultStatus(
                            deploymentResult!,
                            DeploymentResultStatus.Running);

                        eventsPublisher.PublishResultStatusChangedAsync(new DeploymentResultEventData(deploymentResult)
                        {
                            Status = DeploymentResultStatus.Running.ToString()
                        });

                        // Handle PowerShell components
                        var script = GetScripts(component.ComponentId);

                        if (isProductionEnvironment && script.NonProdOnly)
                        {
                            deploymentResultStatus = DeploymentResultStatus.Warning;

                            var warningMessage = $"SCRIPT '{script.Path}' IS SET TO RUN FOR NON PROD ENVIRONMENTS ONLY! SKIPPED THIS SCRIPT EXECUTION!";
                            _logger.LogWarning(warningMessage);
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

                        isSuccessful = scriptDispatcher.Dispatch(
                                     scriptRoot,
                                     script,
                                     commonProperties,
                                     requestId,
                                     deploymentResult.Id,
                                     isProductionRequest,
                                     environmentName,
                                     componentResultLogBuilder,
                                     cancellationToken);

                        if (isSuccessful && deploymentResultStatus != DeploymentResultStatus.Warning)
                        {
                            _logger.LogInformation($"Processing of the PowerShell component '{component.ComponentName}' completed.");
                            deploymentResultStatus = DeploymentResultStatus.Complete;
                        }
                        else
                        {
                            _logger.LogInformation($"Processing of the PowerShell component '{component.ComponentName}' failed.");
                            deploymentResultStatus = DeploymentResultStatus.Failed;
                        }
                        break;
                    default:
                        break;
                }
            }

            catch (OperationCanceledException)
            {
                deploymentResultStatus = DeploymentResultStatus.Cancelled;
                _logger.LogInformation($"Processing of the component '{component.ComponentName}' is cancelled.");
                throw;
            }
            catch (Exception)
            {
                deploymentResultStatus = DeploymentResultStatus.Failed;
                _logger.LogError($"Processing of the component '{component.ComponentName}' failed.");
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
