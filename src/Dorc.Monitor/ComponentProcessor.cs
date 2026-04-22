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
                deploymentResultStatus = component.ComponentType switch
                {
                    ComponentType.Terraform => DeployTerraformComponent(component, deploymentResult, requestId,
                        isProductionRequest, environmentId, environmentName, commonProperties,
                        componentResultLogBuilder, cancellationToken),
                    ComponentType.PowerShell => DeployPowerShellComponent(component, deploymentResult, requestId,
                        isProductionRequest, environmentId, isProductionEnvironment, environmentName, scriptRoot,
                        commonProperties, componentResultLogBuilder, cancellationToken),
                    _ => DeploymentResultStatus.StatusNotSet
                };
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

        private DeploymentResultStatus DeployTerraformComponent(
            ComponentApiModel component,
            DeploymentResultApiModel deploymentResult,
            int requestId,
            bool isProductionRequest,
            int environmentId,
            string environmentName,
            IDictionary<string, VariableValue> commonProperties,
            StringBuilder componentResultLogBuilder,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation($"Processing Terraform component '{component.ComponentName}' - routing to TerraformDispatcher.");

            TerraformRunnerOperations terreformOperation = TerraformRunnerOperations.None;
            if (deploymentResult.Status.Equals(DeploymentResultStatus.Pending.Value))
                terreformOperation = TerraformRunnerOperations.CreatePlan;
            else if (deploymentResult.Status.Equals(DeploymentResultStatus.Confirmed.Value))
                terreformOperation = TerraformRunnerOperations.ApplyPlan;

            SetResultRunning(deploymentResult);

            var isSuccessful = terraformDispatcher.Dispatch(
                component,
                deploymentResult,
                commonProperties,
                requestId,
                isProductionRequest,
                environmentName,
                componentResultLogBuilder,
                terreformOperation,
                cancellationToken);

            if (isSuccessful)
            {
                _logger.LogInformation($"Terraform component '{component.ComponentName}' plan created, waiting for confirmation.");
                return terreformOperation switch
                {
                    TerraformRunnerOperations.CreatePlan => DeploymentResultStatus.WaitingConfirmation,
                    TerraformRunnerOperations.ApplyPlan => DeploymentResultStatus.Complete,
                    TerraformRunnerOperations.None => throw new NotImplementedException()
                };
            }

            _logger.LogError($"Terraform component '{component.ComponentName}' plan creation failed.");
            return DeploymentResultStatus.Failed;
        }

        private DeploymentResultStatus DeployPowerShellComponent(
            ComponentApiModel component,
            DeploymentResultApiModel deploymentResult,
            int requestId,
            bool isProductionRequest,
            int environmentId,
            bool isProductionEnvironment,
            string environmentName,
            string scriptRoot,
            IDictionary<string, VariableValue> commonProperties,
            StringBuilder componentResultLogBuilder,
            CancellationToken cancellationToken)
        {
            SetResultRunning(deploymentResult);

            var script = GetScripts(component.ComponentId);

            if (isProductionEnvironment && script.NonProdOnly)
            {
                return HandleNonProdOnlyScript(component, deploymentResult, environmentId, requestId, script, componentResultLogBuilder);
            }

            var isSuccessful = scriptDispatcher.Dispatch(
                scriptRoot,
                script,
                commonProperties,
                requestId,
                deploymentResult.Id,
                isProductionRequest,
                environmentName,
                componentResultLogBuilder,
                cancellationToken);

            if (isSuccessful)
            {
                _logger.LogInformation($"Processing of the PowerShell component '{component.ComponentName}' completed.");
                return DeploymentResultStatus.Complete;
            }

            _logger.LogInformation($"Processing of the PowerShell component '{component.ComponentName}' failed.");
            return DeploymentResultStatus.Failed;
        }

        private DeploymentResultStatus HandleNonProdOnlyScript(
            ComponentApiModel component,
            DeploymentResultApiModel deploymentResult,
            int environmentId,
            int requestId,
            ScriptApiModel script,
            StringBuilder componentResultLogBuilder)
        {
            var deploymentResultStatus = DeploymentResultStatus.Warning;

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

            return deploymentResultStatus;
        }

        private void SetResultRunning(DeploymentResultApiModel deploymentResult)
        {
            requestsPersistentSource.UpdateResultStatus(
                deploymentResult!,
                DeploymentResultStatus.Running);

            eventsPublisher.PublishResultStatusChangedAsync(new DeploymentResultEventData(deploymentResult)
            {
                Status = DeploymentResultStatus.Running.ToString()
            });
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
