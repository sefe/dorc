using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core.Events;
using Dorc.Core.Interfaces;
using Dorc.Monitor.RunnerProcess;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text;

namespace Dorc.Monitor
{
    internal class ComponentProcessor : IComponentProcessor
    {
        public const string TerraformOperationPropertyName = "TerraformOperation";
        public const string DestroyOperationValue = "Destroy";
        public const string IsDestroyPropertyName = "IsDestroy";
        public const string EphemeralPropertyName = "DOrcIsEphemeral";
        public const string TerminationProtectionPropertyName = "termination_protection";
        public const string ServiceTerminationProtectionPropertyName = "service_termination_protection";

        private readonly IScriptDispatcher scriptDispatcher;
        private readonly ITerraformDispatcher terraformDispatcher;

        private readonly IRequestsPersistentSource requestsPersistentSource;
        private readonly IComponentsPersistentSource componentsPersistentSource;
        private readonly IDeploymentEventsPublisher eventsPublisher;
        private readonly IConfigValuesPersistentSource configValuesPersistentSource;
        private readonly IEnvironmentsPersistentSource? environmentsPersistentSource;
        private ILogger _logger;

        public ComponentProcessor(
            IScriptDispatcher scriptDispatcher,
            ITerraformDispatcher terraformDispatcher,
            IRequestsPersistentSource requestsPersistentSource,
            IComponentsPersistentSource componentsPersistentSource,
            IDeploymentEventsPublisher eventsPublisher,
            IConfigValuesPersistentSource configValuesPersistentSource,
            ILogger<ComponentProcessor> Logger,
            IEnvironmentsPersistentSource? environmentsPersistentSource = null)
        {
            _logger = Logger;
            this.scriptDispatcher = scriptDispatcher;
            this.terraformDispatcher = terraformDispatcher;
            this.requestsPersistentSource = requestsPersistentSource;
            this.componentsPersistentSource = componentsPersistentSource;
            this.eventsPublisher = eventsPublisher;
            this.configValuesPersistentSource = configValuesPersistentSource;
            this.environmentsPersistentSource = environmentsPersistentSource;
        }

        internal static bool IsDestroyOperation(
            IDictionary<string, VariableValue>? commonProperties,
            EnvironmentApiModel? targetEnvironment = null)
        {
            if (commonProperties != null)
            {
                if (commonProperties.TryGetValue(TerraformOperationPropertyName, out var opValue) &&
                    string.Equals(Convert.ToString(opValue.Value), DestroyOperationValue, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (commonProperties.TryGetValue(IsDestroyPropertyName, out var isDestroyValue) &&
                    string.Equals(Convert.ToString(isDestroyValue.Value), "true", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        internal static void ApplyEphemeralPropertyInvariants(IDictionary<string, VariableValue>? properties)
        {
            if (properties is null)
            {
                return;
            }

            properties[EphemeralPropertyName] = new VariableValue
            {
                Value = "true",
                Type = typeof(string)
            };

            if (properties.ContainsKey(TerminationProtectionPropertyName))
            {
                properties[TerminationProtectionPropertyName] = new VariableValue
                {
                    Value = "false",
                    Type = typeof(string)
                };
            }

            if (properties.ContainsKey(ServiceTerminationProtectionPropertyName))
            {
                properties[ServiceTerminationProtectionPropertyName] = new VariableValue
                {
                    Value = "false",
                    Type = typeof(string)
                };
            }
        }

        internal static bool ShouldAutoApproveEphemeralPlan(
            EnvironmentApiModel? targetEnvironment,
            bool isProductionRequest,
            bool isProductionEnvironment)
        {
            return targetEnvironment is { IsEphemeral: true, EnvironmentIsProd: false } &&
                   !isProductionRequest &&
                   !isProductionEnvironment;
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

            if (deploymentResult.Status != DeploymentResultStatus.Pending.ToString() &&
                !(component.ComponentType == ComponentType.Terraform &&
                  deploymentResult.Status == DeploymentResultStatus.Confirmed.ToString()))
                throw new InvalidOperationException(
                    $"Component '{component.ComponentName}' cannot be dispatched in status '{deploymentResult.Status}'.");

            EnvironmentApiModel? targetEnvironment = null;
            if (component.ComponentType == ComponentType.Terraform && environmentsPersistentSource != null)
            {
                targetEnvironment = environmentId > 0
                    ? environmentsPersistentSource.GetEnvironment(environmentId)
                    : null;
                targetEnvironment ??= environmentsPersistentSource.GetEnvironment(environmentName);
            }

            var isDestroy = IsDestroyOperation(commonProperties, targetEnvironment);
            if (component.ComponentType == ComponentType.Terraform && isDestroy &&
                (targetEnvironment is null || !targetEnvironment.IsEphemeral || targetEnvironment.EnvironmentIsProd))
            {
                _logger.LogError("Terraform Destroy is only permitted on non-production ephemeral environments.");
                requestsPersistentSource.UpdateResultStatus(deploymentResult, DeploymentResultStatus.Failed);
                return false;
            }

            if (targetEnvironment is { IsEphemeral: true })
            {
                if (isProductionRequest || isProductionEnvironment || targetEnvironment.EnvironmentIsProd)
                {
                    requestsPersistentSource.UpdateResultStatus(deploymentResult, DeploymentResultStatus.Failed);
                    throw new InvalidOperationException(
                        $"Ephemeral environment '{environmentName}' cannot be marked as production or auto-approved in production.");
                }

                ApplyEphemeralPropertyInvariants(commonProperties);
            }

            var isConfirmedStatus = deploymentResult.Status == DeploymentResultStatus.Confirmed.ToString();
            var terraformOperation = isConfirmedStatus
                ? (isDestroy ? TerraformRunnerOperations.ApplyDestroyPlan : TerraformRunnerOperations.ApplyPlan)
                : (isDestroy ? TerraformRunnerOperations.CreateDestroyPlan : TerraformRunnerOperations.CreatePlan);
            if (component.ComponentType == ComponentType.Terraform &&
                !requestsPersistentSource.UpdateResultStatus(
                    deploymentResult,
                    DeploymentResultStatus.Running,
                    isConfirmedStatus
                        ? DeploymentResultStatus.Confirmed : DeploymentResultStatus.Pending))
                throw new InvalidOperationException(
                    $"Terraform component '{component.ComponentName}' changed status before dispatch.");

            _logger.LogInformation($"Deploying component with the name '{component.ComponentName}' and id '{component.ComponentId}' of type '{component.ComponentType}' (Enum Value: {(int)component.ComponentType}).");

            var deploymentResultStatus = DeploymentResultStatus.StatusNotSet;
            var autoApproveCasFailed = false;
            StringBuilder componentResultLogBuilder = new StringBuilder();

            try
            {

                bool isSuccessful;

                // Route to appropriate dispatcher based on component type
                switch (component.ComponentType)
                {
                    case ComponentType.Terraform:
                        _logger.LogInformation($"Processing Terraform component '{component.ComponentName}' - routing to TerraformDispatcher.");

                        var terreformOperation = terraformOperation;

                        eventsPublisher.PublishResultStatusChangedAsync(new DeploymentResultEventData(deploymentResult)
                        {
                            Status = DeploymentResultStatus.Running.ToString()
                        }).ContinueWith(t => _logger.LogWarning(t.Exception!.InnerException,
                            "fire-and-forget publish failed for result"), TaskContinuationOptions.OnlyOnFaulted);

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

                        if (isSuccessful)
                        {
                            var isPlanPhase = terreformOperation == TerraformRunnerOperations.CreatePlan ||
                                              terreformOperation == TerraformRunnerOperations.CreateDestroyPlan;

                            if (isPlanPhase && ShouldAutoApproveEphemeralPlan(targetEnvironment, isProductionRequest, isProductionEnvironment))
                            {
                                var receipt = requestsPersistentSource.GetTerraformDeploymentReceipt(deploymentResult.Id);
                                var approval = new TerraformPlanApproval
                                {
                                    DeploymentResultId = deploymentResult.Id,
                                    DeploymentRequestId = requestId,
                                    ApprovedBy = TerraformPlanApproval.EphemeralAutoApproveActor,
                                    ConfirmedBy = TerraformPlanApproval.EphemeralAutoApproveActor,
                                    ApprovedAt = DateTimeOffset.UtcNow,
                                    PlanSha256 = receipt?.PlanSha256 ?? string.Empty,
                                    SourceSha256 = receipt?.SourceSha256 ?? string.Empty,
                                    PlanContentSha256 = receipt?.PlanContentSha256 ?? string.Empty
                                };

                                var canAutoApprove = !cancellationToken.IsCancellationRequested &&
                                                     requestsPersistentSource.TryConfirmTerraformPlan(deploymentResult, approval) &&
                                                     requestsPersistentSource.UpdateResultStatus(
                                                         deploymentResult,
                                                         DeploymentResultStatus.Running,
                                                         DeploymentResultStatus.Confirmed);

                                if (!canAutoApprove)
                                {
                                    autoApproveCasFailed = true;
                                    _logger.LogWarning(
                                        $"Auto-approval CAS or cancellation check failed for ephemeral Terraform plan on component '{component.ComponentName}'; skipping apply.");
                                    var currentResult = requestsPersistentSource.GetDeploymentResults(deploymentResult.Id);
                                    var isCancelled = cancellationToken.IsCancellationRequested ||
                                                      string.Equals(
                                                          currentResult?.Status,
                                                          DeploymentResultStatus.Cancelled.ToString(),
                                                          StringComparison.OrdinalIgnoreCase);
                                    deploymentResultStatus = isCancelled
                                        ? DeploymentResultStatus.Cancelled
                                        : DeploymentResultStatus.Failed;
                                    if (isCancelled)
                                    {
                                        deploymentResult.Status = DeploymentResultStatus.Cancelled.ToString();
                                        if (!string.Equals(
                                                currentResult?.Status,
                                                DeploymentResultStatus.Cancelled.ToString(),
                                                StringComparison.OrdinalIgnoreCase))
                                        {
                                            requestsPersistentSource.UpdateResultStatus(
                                                deploymentResult,
                                                DeploymentResultStatus.Cancelled);
                                        }
                                    }
                                    else
                                    {
                                        deploymentResult.Status = !string.IsNullOrWhiteSpace(currentResult?.Status)
                                            ? currentResult.Status
                                            : deploymentResultStatus.ToString();
                                    }
                                }
                                else
                                {
                                    deploymentResult.Status = DeploymentResultStatus.Running.ToString();
                                    var applyOperation = terreformOperation == TerraformRunnerOperations.CreateDestroyPlan
                                        ? TerraformRunnerOperations.ApplyDestroyPlan
                                        : TerraformRunnerOperations.ApplyPlan;

                                    _logger.LogInformation(
                                        $"Auto-approved ephemeral non-prod Terraform plan for component '{component.ComponentName}'; transitioning immediately to '{applyOperation}'.");

                                    isSuccessful = terraformDispatcher.Dispatch(
                                        component,
                                        deploymentResult,
                                        commonProperties,
                                        requestId,
                                        isProductionRequest,
                                        environmentName,
                                        componentResultLogBuilder,
                                        applyOperation,
                                        cancellationToken);

                                    deploymentResultStatus = isSuccessful
                                        ? DeploymentResultStatus.Complete
                                        : DeploymentResultStatus.Failed;
                                }
                            }
                            else
                            {
                                deploymentResultStatus = isPlanPhase
                                    ? DeploymentResultStatus.WaitingConfirmation
                                    : DeploymentResultStatus.Complete;
                                _logger.LogInformation($"Terraform component '{component.ComponentName}' operation '{terreformOperation}' completed with status '{deploymentResultStatus}'.");
                            }
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
                        }).ContinueWith(t => _logger.LogWarning(t.Exception!.InnerException,
                            "fire-and-forget publish failed for result"), TaskContinuationOptions.OnlyOnFaulted);

                        // Handle PowerShell components
                        var script = requestsPersistentSource.GetSubmittedScript(requestId,
                            component.ComponentId ?? throw new InvalidOperationException("Component ID is missing."));

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
                            }).ContinueWith(t => _logger.LogWarning(t.Exception!.InnerException,
                                "fire-and-forget publish failed for result"), TaskContinuationOptions.OnlyOnFaulted);

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
                if (!autoApproveCasFailed)
                    requestsPersistentSource.UpdateResultStatus(
                        deploymentResult,
                        deploymentResultStatus);

                componentsPersistentSource.SaveEnvComponentStatus(
                    environmentId,
                    component,
                    deploymentResult.Status ?? deploymentResultStatus.ToString(),
                    requestId);

                eventsPublisher.PublishResultStatusChangedAsync(new DeploymentResultEventData(deploymentResult)
                {
                    Status = deploymentResultStatus.ToString()
                }).ContinueWith(t => _logger.LogWarning(t.Exception!.InnerException,
                    "fire-and-forget publish failed for result"), TaskContinuationOptions.OnlyOnFaulted);
            }

            return deploymentResultStatus != DeploymentResultStatus.Failed && deploymentResultStatus != DeploymentResultStatus.Cancelled;
        }
    }
}
