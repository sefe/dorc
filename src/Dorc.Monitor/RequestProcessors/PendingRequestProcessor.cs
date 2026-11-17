using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core;
using Dorc.Core.Events;
using Dorc.Core.Interfaces;
using Dorc.Core.VariableResolution;
using Dorc.Monitor.Notifications;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;

namespace Dorc.Monitor.RequestProcessors
{
    internal class PendingRequestProcessor : IPendingRequestProcessor
    {
        private readonly ILogger logger;
        private IVariableResolver _variableResolver;
        private readonly IComponentProcessor componentProcessor;
        private readonly IVariableScopeOptionsResolver _variableScopeOptionsResolver;
        private readonly IRequestsPersistentSource requestsPersistentSource;
        private readonly IPropertyValuesPersistentSource propertyValuesPersistentSource;
        private readonly IEnvironmentsPersistentSource environmentsPersistentSource;
        private readonly IManageProjectsPersistentSource manageProjectsPersistentSource;
        private readonly IDeploymentEventsPublisher eventsPublisher;
        private readonly IConfigValuesPersistentSource _configValuesPersistentSource;
        private readonly IPropertyEvaluator _propertyEvaluator;
        private readonly ILoggerFactory _loggerFactory;
        private readonly IDeploymentNotificationSink _notificationSink;
        private readonly IGitHubArtifactDownloader _gitHubArtifactDownloader;

        public PendingRequestProcessor(
            ILoggerFactory loggerFactory,
            IComponentProcessor componentProcessor,
            IVariableScopeOptionsResolver variableScopeOptionsResolver,
            IRequestsPersistentSource requestsPersistentSource,
            IPropertyValuesPersistentSource propertyValuesPersistentSource,
            IEnvironmentsPersistentSource environmentsPersistentSource,
            IManageProjectsPersistentSource manageProjectsPersistentSource,
            IConfigValuesPersistentSource configValuesPersistentSource,
            IPropertyEvaluator propertyEvaluator,
            IDeploymentEventsPublisher eventPublisher,
            IDeploymentNotificationSink notificationSink,
            IGitHubArtifactDownloader gitHubArtifactDownloader)
        {
            _loggerFactory = loggerFactory;
            _propertyEvaluator = propertyEvaluator;
            _configValuesPersistentSource = configValuesPersistentSource;
            _gitHubArtifactDownloader = gitHubArtifactDownloader;
            this.logger = _loggerFactory.CreateLogger<PendingRequestProcessor>();

            this.componentProcessor = componentProcessor;
            this._variableScopeOptionsResolver = variableScopeOptionsResolver;
            this.requestsPersistentSource = requestsPersistentSource;
            this.propertyValuesPersistentSource = propertyValuesPersistentSource;
            this.environmentsPersistentSource = environmentsPersistentSource;
            this.manageProjectsPersistentSource = manageProjectsPersistentSource;
            this.eventsPublisher = eventPublisher;
            _notificationSink = notificationSink;
        }

        public void Execute(RequestToProcessDto requestToExecute, CancellationToken cancellationToken, ILoggerFactory loggerFactory)
        {
            using (logger.BeginScope(new Dictionary<string, object> { ["RequestId"] = requestToExecute.Request.Id }))
            {

                logger.LogInformation($"Attempting to deploy the request with id '{requestToExecute.Request.Id}'.");

                using var statusPoller = new RequestStatusPoller(
                    requestsPersistentSource,
                    loggerFactory.CreateLogger<RequestStatusPoller>(),
                    pollInterval: TimeSpan.FromSeconds(10));

                using var compositeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                statusPoller.StartMonitoring(
                    requestToExecute.Request.Id,
                    compositeCts,
                    cancellationToken);

                _variableResolver = new VariableResolver(propertyValuesPersistentSource, _loggerFactory, _propertyEvaluator);

                string? resolvedDropFolder = null;
                EnvironmentApiModel? environment = null;
                DeploymentRequestDetail? requestDetail = requestToExecute.Details;
                var isDestroyRequest = IsDestroyRequest(requestDetail);
                try
                {
                    var scriptRoot = _configValuesPersistentSource.GetConfigValue("ScriptRoot");
                    SetUpScriptRootAsProperty(scriptRoot);

                    if (string.IsNullOrEmpty(requestToExecute.Request.RequestDetails))
                    {
                        throw new InvalidOperationException("Deployment request details are empty.");
                    }

                    try
                    {
                        // Debug-level convenience dump; RequestDetails carries
                        // sensitive wizard parameter values in cleartext, so
                        // redact before the payload touches any log sink.
                        logger.LogDebug($"Request details:\r\n{RequestPropertyRedaction.RedactRequestDetailsXml(requestToExecute.Request.RequestDetails)}");

                        requestDetail = requestToExecute.Details;

                        var environmentName = requestDetail.EnvironmentName;

                        SetUpEnvironmentNameAsProperty(environmentName);

                        environment = environmentsPersistentSource.GetEnvironment(environmentName);

                        if (environment == null)
                        {
                            if (isDestroyRequest)
                            {
                                logger.LogWarning($"Environment '{environmentName}' no longer exists for destroy request '{requestToExecute.Request.Id}'; completing cleanly.");
                                CancelPendingDeploymentResults(requestToExecute.Request.Id, DeploymentRequestStatus.Completed);
                                var completedAt = DateTimeOffset.Now;
                                requestsPersistentSource.SetRequestCompletionStatus(
                                    requestToExecute.Request.Id,
                                    DeploymentRequestStatus.Completed,
                                    completedAt);
                                requestToExecute.Request.Status = DeploymentRequestStatus.Completed.ToString();
                                requestToExecute.Request.CompletedTime = completedAt;
                                return;
                            }

                            throw new InvalidOperationException($"Environment '{environmentName}' was not found.");
                        }

                        var expectedEnvironmentId = GetExpectedEphemeralEnvironmentId(requestDetail);
                        if (expectedEnvironmentId.HasValue &&
                            environment.EnvironmentId > 0 &&
                            expectedEnvironmentId.Value != environment.EnvironmentId)
                        {
                            logger.LogWarning(
                                "Cancelling request {RequestId} because target environment '{EnvironmentName}' now has Id {ActualId} instead of expected Id {ExpectedId}.",
                                requestToExecute.Request.Id,
                                environment.EnvironmentName,
                                environment.EnvironmentId,
                                expectedEnvironmentId.Value);
                            CancelPendingDeploymentResults(requestToExecute.Request.Id, DeploymentRequestStatus.Cancelled);
                            var cancelledTime = DateTimeOffset.Now;
                            requestsPersistentSource.SetRequestCompletionStatus(
                                requestToExecute.Request.Id,
                                DeploymentRequestStatus.Cancelled,
                                cancelledTime,
                                $"Cancelled because environment '{environment.EnvironmentName}' was replaced (expected Id {expectedEnvironmentId.Value}, found {environment.EnvironmentId}).");
                            requestToExecute.Request.Status = DeploymentRequestStatus.Cancelled.ToString();
                            requestToExecute.Request.CompletedTime = cancelledTime;
                            return;
                        }

                        if (environment.EnvironmentSecure)
                        {
                            logger.LogInformation($"Environment '{environmentName}' is secure; not using default property values.");
                        }

                        resolvedDropFolder = SetUpDropFolderAsProperty(requestDetail.BuildDetail.DropLocation);

                        SetUpDeploymentLogDirAsProperty();

                        SetUpBuildNumberAsProperty(requestToExecute.Request.BuildNumber);

                        SetUpRefDataApiUrlAsProperty();

                        SetUpConfigValuesAsProperties(environment);

                        SetUpEnvironmentAsProperty(environment);

                        SetUpEnvOwnerEmailAsProperty(requestToExecute.Request);

                        SetUpRequestDetailsPropertiesAsProperties(requestDetail.Properties);

                        var deploymentStartedTime = DateTimeOffset.Now;

                        if (!InitializeDeploymentRequest(requestToExecute.Request, deploymentStartedTime))
                            return;

                        var deploymentRequestStatus = DeploymentRequestStatus.Completed;
                        isDestroyRequest = IsDestroyRequest(requestDetail, environment);
                        if (isDestroyRequest && (!environment.IsEphemeral || environment.EnvironmentIsProd || requestToExecute.Request.IsProd))
                        {
                            logger.LogError("Terraform Destroy is only permitted on non-production ephemeral environments.");
                            CancelPendingDeploymentResults(requestToExecute.Request.Id, DeploymentRequestStatus.Failed);
                            var failedTime = DateTimeOffset.Now;
                            requestsPersistentSource.SetRequestCompletionStatus(
                                requestToExecute.Request.Id,
                                DeploymentRequestStatus.Failed,
                                failedTime,
                                "Terraform Destroy is only permitted on non-production ephemeral environments.");
                            requestToExecute.Request.Status = DeploymentRequestStatus.Failed.ToString();
                            requestToExecute.Request.CompletedTime = failedTime;
                            return;
                        }

                        if (isDestroyRequest &&
                            string.Equals(environment.EphemeralState, EphemeralEnvironmentStates.TeardownFailed, StringComparison.OrdinalIgnoreCase))
                        {
                            logger.LogInformation(
                                "Skipping destroy request {RequestId} because ephemeral environment '{EnvironmentName}' is already in state 'TeardownFailed'.",
                                requestToExecute.Request.Id,
                                environment.EnvironmentName);
                            CancelPendingDeploymentResults(requestToExecute.Request.Id, DeploymentRequestStatus.Cancelled);
                            var cancelledTime = DateTimeOffset.Now;
                            requestsPersistentSource.SetRequestCompletionStatus(
                                requestToExecute.Request.Id,
                                DeploymentRequestStatus.Cancelled,
                                cancelledTime,
                                $"Cancelled because ephemeral environment '{environment.EnvironmentName}' is already in TeardownFailed state.");
                            requestToExecute.Request.Status = DeploymentRequestStatus.Cancelled.ToString();
                            requestToExecute.Request.CompletedTime = cancelledTime;
                            return;
                        }

                        if (isDestroyRequest &&
                            !string.Equals(environment.EphemeralState, EphemeralEnvironmentStates.TearingDown, StringComparison.OrdinalIgnoreCase))
                        {
                            environmentsPersistentSource.UpdateEphemeralState(environment.EnvironmentId, EphemeralEnvironmentStates.TearingDown);
                            environment.EphemeralState = EphemeralEnvironmentStates.TearingDown;
                        }
                        else if (!isDestroyRequest &&
                                 environment.IsEphemeral &&
                                 (string.Equals(environment.EphemeralState, EphemeralEnvironmentStates.TearingDown, StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(environment.EphemeralState, EphemeralEnvironmentStates.TornDown, StringComparison.OrdinalIgnoreCase)))
                        {
                            logger.LogInformation(
                                "Skipping non-destroy request {RequestId} because ephemeral environment '{EnvironmentName}' is in state '{EphemeralState}'.",
                                requestToExecute.Request.Id,
                                environment.EnvironmentName,
                                environment.EphemeralState);
                            CancelPendingDeploymentResults(requestToExecute.Request.Id, DeploymentRequestStatus.Cancelled);
                            var cancelledTime = DateTimeOffset.Now;
                            requestsPersistentSource.SetRequestCompletionStatus(
                                requestToExecute.Request.Id,
                                DeploymentRequestStatus.Cancelled,
                                cancelledTime,
                                $"Cancelled because ephemeral environment '{environment.EnvironmentName}' is tearing down or torn down.");
                            requestToExecute.Request.Status = DeploymentRequestStatus.Cancelled.ToString();
                            requestToExecute.Request.CompletedTime = cancelledTime;
                            return;
                        }

                        var orderedNonSkippedComponents = OrderComponentsForExecution(
                            requestsPersistentSource.GetSubmittedComponents(requestToExecute.Request.Id),
                            isDestroyRequest,
                            requestDetail);

                        logger.LogInformation($"Found {orderedNonSkippedComponents.Count} non-skipped components for request {requestToExecute.Request.Id}:");

                        if (!orderedNonSkippedComponents.Any())
                        {
                            logger.LogWarning($"No non-skipped components are found for the request with id '{requestToExecute.Request.Id}'.");

                            var completedTime = DateTimeOffset.Now;

                            deploymentRequestStatus = UpdateEphemeralEnvironmentLifecycleState(environment, requestDetail, isDestroyRequest, deploymentRequestStatus, requestToExecute.Request);

                            requestsPersistentSource.SetRequestCompletionStatus(
                                requestToExecute.Request.Id,
                                deploymentRequestStatus,
                                completedTime);
                            requestToExecute.Request.Status = deploymentRequestStatus.ToString();
                            requestToExecute.Request.CompletedTime = completedTime;

                            eventsPublisher.PublishRequestStatusChangedAsync(new DeploymentRequestEventData(requestToExecute.Request)
                            {
                                Status = deploymentRequestStatus.ToString(),
                                CompletedTime = completedTime,
                            }).ContinueWith(t => logger.LogWarning(t.Exception!.InnerException,
                                "fire-and-forget publish failed for requestId={RequestId}", requestToExecute.Request.Id),
                                TaskContinuationOptions.OnlyOnFaulted);

                            FireNotification(requestToExecute.Request, deploymentRequestStatus.ToString(), deploymentStartedTime, completedTime);

                            return;
                        }

                        var deploymentResults = requestsPersistentSource.GetDeploymentResultsForRequest(requestToExecute.Request.Id).ToDictionary(r => r.ComponentId);
                        foreach (var nonSkippedComponent in orderedNonSkippedComponents)
                        {
                            try
                            {
                                var componentId = nonSkippedComponent.ComponentId!.Value;

                                if (!deploymentResults.ContainsKey(componentId))
                                {
                                    var deploymentResult = requestsPersistentSource.CreateDeploymentResult(
                                        componentId,
                                        requestToExecute.Request.Id);

                                    deploymentResults.Add(componentId, deploymentResult);
                                }
                            }
                            catch (Exception exception)
                            {
                                logger.LogError($"Deployment result cannot be created. Exception: {exception}");
                                throw;
                            }
                        }

                        var orderedEnabledNonSkippedComponents = orderedNonSkippedComponents
                            .Where(component => isDestroyRequest
                                ? component.ComponentType == ComponentType.Terraform
                                : component.IsEnabled);

                        if (!orderedEnabledNonSkippedComponents.Any())
                        {
                            logger.LogWarning($"No enabled non-skipped components are found for the request with id '{requestToExecute.Request.Id}'.");

                            var completedTime = DateTimeOffset.Now;

                            deploymentRequestStatus = UpdateEphemeralEnvironmentLifecycleState(environment, requestDetail, isDestroyRequest, deploymentRequestStatus, requestToExecute.Request);

                            requestsPersistentSource.SetRequestCompletionStatus(
                                requestToExecute.Request.Id,
                                deploymentRequestStatus,
                                completedTime);
                            requestToExecute.Request.Status = deploymentRequestStatus.ToString();
                            requestToExecute.Request.CompletedTime = completedTime;

                            eventsPublisher.PublishRequestStatusChangedAsync(new DeploymentRequestEventData(requestToExecute.Request)
                            {
                                Status = deploymentRequestStatus.ToString(),
                                CompletedTime = completedTime,
                            }).ContinueWith(t => logger.LogWarning(t.Exception!.InnerException,
                                "fire-and-forget publish failed for requestId={RequestId}", requestToExecute.Request.Id),
                                TaskContinuationOptions.OnlyOnFaulted);

                            FireNotification(requestToExecute.Request, deploymentRequestStatus.ToString(), deploymentStartedTime, completedTime);

                            return;
                        }

                        var commonProperties = GetCommonProperties(
                            environment.EnvironmentIsProd,
                            requestDetail.Properties);
                        if (environment.IsEphemeral)
                        {
                            ComponentProcessor.ApplyEphemeralPropertyInvariants(commonProperties);
                        }
                        if (isDestroyRequest)
                        {
                            commonProperties[ComponentProcessor.TerraformOperationPropertyName] = new VariableValue
                            {
                                Value = ComponentProcessor.DestroyOperationValue,
                                Type = typeof(string)
                            };
                        }

                        foreach (var enabledNonSkippedComponent in orderedEnabledNonSkippedComponents)
                        {
                            try
                            {
                                compositeCts.Token.ThrowIfCancellationRequested();

                                if (IsRequestCancelledByAnotherNode(requestToExecute.Request.Id))
                                {
                                    deploymentRequestStatus = DeploymentRequestStatus.Cancelled;

                                    logger.LogInformation(
                                        "Request {RequestId} was cancelled by another node; aborting deployment of remaining components.",
                                        requestToExecute.Request.Id);
                                    break;
                                }

                                var componentId = enabledNonSkippedComponent.ComponentId!.Value;
                                var deploymentResult = deploymentResults[componentId];

                                if (isDestroyRequest &&
                                    enabledNonSkippedComponent.ComponentType == ComponentType.Terraform &&
                                    deploymentResult.Status == DeploymentResultStatus.Disabled.ToString())
                                {
                                    requestsPersistentSource.UpdateResultStatus(deploymentResult, DeploymentResultStatus.Pending);
                                    deploymentResult.Status = DeploymentResultStatus.Pending.ToString();
                                }

                                if (deploymentResult.Status != DeploymentResultStatus.Pending.ToString() &&
                                    !(enabledNonSkippedComponent.ComponentType == ComponentType.Terraform &&
                                      deploymentResult.Status == DeploymentResultStatus.Confirmed.ToString()))
                                {
                                    logger.LogInformation(
                                        "Not dispatching component {ComponentId} in status {Status}.",
                                        componentId, deploymentResult.Status);
                                    if (deploymentResult.Status == DeploymentResultStatus.WaitingConfirmation.ToString() &&
                                        deploymentRequestStatus == DeploymentRequestStatus.Completed)
                                        deploymentRequestStatus = DeploymentRequestStatus.WaitingConfirmation;
                                    else if (deploymentResult.Status == DeploymentResultStatus.Failed.ToString() ||
                                             deploymentResult.Status == DeploymentResultStatus.Running.ToString())
                                    {
                                        deploymentRequestStatus = DeploymentRequestStatus.Failed;
                                        if (deploymentResult.Status == DeploymentResultStatus.Running.ToString())
                                        {
                                            const string interrupted = "The previous component execution was interrupted. Reconcile infrastructure/state and submit a fresh deployment; it will not be replayed automatically.";
                                            logger.LogError("Component {ComponentId}: {Reason}", componentId, interrupted);
                                            requestsPersistentSource.UpdateResultLog(deploymentResult,
                                                deploymentResult.Log + System.Environment.NewLine + interrupted);
                                            requestsPersistentSource.UpdateResultStatus(deploymentResult,
                                                DeploymentResultStatus.Failed, DeploymentResultStatus.Running);
                                        }
                                        if (isDestroyRequest || enabledNonSkippedComponent.StopOnFailure)
                                            break;
                                    }
                                    continue;
                                }

                                bool isSuccessful = componentProcessor.DeployComponent(
                                    enabledNonSkippedComponent,
                                    deploymentResult,
                                    requestToExecute.Request.Id,
                                    requestToExecute.Request.IsProd,
                                    environment.EnvironmentId,
                                    environment.EnvironmentIsProd,
                                    environmentName,
                                    scriptRoot,
                                    commonProperties,
                                    compositeCts.Token);

                                if (!isSuccessful)
                                {
                                    deploymentRequestStatus = deploymentResult.Status == DeploymentResultStatus.Cancelled.ToString()
                                        ? DeploymentRequestStatus.Cancelled
                                        : DeploymentRequestStatus.Failed;

                                    if (isDestroyRequest || enabledNonSkippedComponent.StopOnFailure)
                                    {
                                        logger.LogWarning(
                                            "Deployment of remaining components is aborted due to StopOnFailure flag or destroy failure on component '{ComponentName}'.",
                                            enabledNonSkippedComponent.ComponentName);
                                        break;
                                    }
                                }
                                if (deploymentResult.Status == DeploymentResultStatus.WaitingConfirmation.ToString() &&
                                    deploymentRequestStatus == DeploymentRequestStatus.Completed)
                                {
                                    deploymentRequestStatus = DeploymentRequestStatus.WaitingConfirmation;
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                var currentDbRequestStatus = requestsPersistentSource.GetRequest(requestToExecute.Request.Id).Status;
                                deploymentRequestStatus = currentDbRequestStatus != DeploymentRequestStatus.Pending.ToString()
                                    ? DeploymentRequestStatus.Cancelled
                                    : DeploymentRequestStatus.Pending;

                                logger.LogInformation("Deployment of remaining components is cancelled.");
                                break;
                            }
                            catch (Exception exception)
                            {
                                deploymentRequestStatus = DeploymentRequestStatus.Failed;

                                logger.LogError($"Component deployment failed. Exception: {exception}");

                                while (exception.InnerException != null)
                                {
                                    logger.LogError(exception.InnerException.ToString());

                                    exception = exception.InnerException;
                                }

                                if (isDestroyRequest || enabledNonSkippedComponent.StopOnFailure)
                                {
                                    logger.LogError("Deployment of remaining components is aborted.");
                                    break;
                                }
                            }
                        }

                        if (deploymentRequestStatus != DeploymentRequestStatus.Completed &&
                            deploymentRequestStatus != DeploymentRequestStatus.WaitingConfirmation)
                        {
                            CancelPendingDeploymentResults(requestToExecute.Request.Id, deploymentRequestStatus);
                        }

                        var finalCompletedTime = DateTimeOffset.Now;

                        // A user cancellation races with DeploymentRequestStateProcessor.CancelRequests
                        // over the Cancelling -> Cancelled transition. Claim it optimistically before the
                        // unconditional completion write below, so exactly one of the two components
                        // notifies the requester — whichever wins this switch.
                        var wonCancelledTransition = deploymentRequestStatus == DeploymentRequestStatus.Cancelled
                            && requestsPersistentSource.SwitchDeploymentRequestStatuses(
                                new List<DeploymentRequestApiModel> { requestToExecute.Request },
                                DeploymentRequestStatus.Cancelling,
                                DeploymentRequestStatus.Cancelled,
                                finalCompletedTime) > 0;

                        deploymentRequestStatus = UpdateEphemeralEnvironmentLifecycleState(environment, requestDetail, isDestroyRequest, deploymentRequestStatus, requestToExecute.Request);

                        requestsPersistentSource.SetRequestCompletionStatus(
                            requestToExecute.Request.Id,
                            deploymentRequestStatus,
                            finalCompletedTime);
                        requestToExecute.Request.Status = deploymentRequestStatus.ToString();
                        requestToExecute.Request.CompletedTime = finalCompletedTime;

                        eventsPublisher.PublishRequestStatusChangedAsync(new DeploymentRequestEventData(requestToExecute.Request)
                        {
                            Status = deploymentRequestStatus.ToString(),
                            CompletedTime = finalCompletedTime,
                        }).ContinueWith(t => logger.LogWarning(t.Exception!.InnerException,
                            "fire-and-forget publish failed for requestId={RequestId}", requestToExecute.Request.Id),
                            TaskContinuationOptions.OnlyOnFaulted);

                        if (deploymentRequestStatus != DeploymentRequestStatus.Cancelled || wonCancelledTransition)
                        {
                            FireNotification(requestToExecute.Request, deploymentRequestStatus.ToString(), deploymentStartedTime, finalCompletedTime);
                        }
                    }
                    catch (Exception ex)
                    {
                        var criticalLogBuilder = new StringBuilder();

                        logger.LogError(ex, "Deployment execution failure: {ErrorMessage}", ex.Message);
                        criticalLogBuilder.AppendLine($"Deployment execution failure: {ex.Message}");
                        criticalLogBuilder.AppendLine();
                        criticalLogBuilder.AppendLine(ex.ToString());

                        CancelPendingDeploymentResults(requestToExecute.Request.Id, DeploymentRequestStatus.Errored);
                        TryMarkEphemeralTeardownFailedOnException(environment, isDestroyRequest, requestToExecute.Request.EnvironmentName, requestDetail);

                        var erroredTime = DateTimeOffset.Now;

                        requestsPersistentSource.SetRequestCompletionStatus(
                            requestToExecute.Request.Id,
                            DeploymentRequestStatus.Errored,
                            erroredTime,
                            criticalLogBuilder.ToString());
                        requestToExecute.Request.Status = DeploymentRequestStatus.Errored.ToString();
                        requestToExecute.Request.CompletedTime = erroredTime;

                        eventsPublisher.PublishRequestStatusChangedAsync(new DeploymentRequestEventData(requestToExecute.Request)
                        {
                            Status = DeploymentRequestStatus.Errored.ToString(),
                            CompletedTime = erroredTime,
                        }).ContinueWith(t => logger.LogWarning(t.Exception!.InnerException,
                            "fire-and-forget publish failed for requestId={RequestId}", requestToExecute.Request.Id),
                            TaskContinuationOptions.OnlyOnFaulted);

                        FireNotification(requestToExecute.Request, DeploymentRequestStatus.Errored.ToString(), requestToExecute.Request.StartedTime ?? erroredTime, erroredTime);
                    }
                }
                catch (Exception e)
                {
                    var criticalLogBuilder = new StringBuilder();

                    logger.LogError(e, "Failed while starting runner: {ErrorMessage}", e.Message);
                    criticalLogBuilder.AppendLine($"Failed while starting runner: {e.Message}");
                    criticalLogBuilder.AppendLine();
                    criticalLogBuilder.AppendLine(e.ToString());

                    CancelPendingDeploymentResults(requestToExecute.Request.Id, DeploymentRequestStatus.Errored);
                    TryMarkEphemeralTeardownFailedOnException(environment, isDestroyRequest, requestToExecute.Request.EnvironmentName, requestDetail);

                    var erroredTime = DateTimeOffset.Now;

                    requestsPersistentSource.UpdateRequestStatus(
                        requestToExecute.Request.Id,
                        DeploymentRequestStatus.Errored,
                        erroredTime,
                        criticalLogBuilder.ToString());
                    requestToExecute.Request.Status = DeploymentRequestStatus.Errored.ToString();
                    requestToExecute.Request.CompletedTime = erroredTime;

                    eventsPublisher.PublishRequestStatusChangedAsync(new DeploymentRequestEventData(requestToExecute.Request)
                    {
                        Status = DeploymentRequestStatus.Errored.ToString(),
                        CompletedTime = erroredTime,
                    }).ContinueWith(t => logger.LogWarning(t.Exception!.InnerException,
                        "fire-and-forget publish failed for requestId={RequestId}", requestToExecute.Request.Id),
                        TaskContinuationOptions.OnlyOnFaulted);

                    FireNotification(requestToExecute.Request, DeploymentRequestStatus.Errored.ToString(), requestToExecute.Request.RequestedTime ?? erroredTime, erroredTime);

                    return;
                }
                finally
                {
                    // Clean up downloaded GitHub artifacts after deployment completes
                    if (resolvedDropFolder != null &&
                        _gitHubArtifactDownloader.IsGitHubArtifactUrl(requestToExecute.Details.BuildDetail.DropLocation))
                    {
                        _gitHubArtifactDownloader.Cleanup(resolvedDropFolder);
                    }
                }
            }
        }

        private void FireNotification(
            DeploymentRequestApiModel request,
            string finalStatus,
            DateTimeOffset startedTime,
            DateTimeOffset completedTime)
        {
            DeploymentNotificationDispatch.FireAndForget(_notificationSink, logger, request, finalStatus, startedTime, completedTime);
        }

        private void CancelPendingDeploymentResults(int requestId, DeploymentRequestStatus requestStatus)
        {
            try
            {
                var pendingResults = requestsPersistentSource
                    .GetDeploymentResultsForRequest(requestId)
                    .Where(r => r.Status == DeploymentResultStatus.Pending.ToString() ||
                        r.Status == DeploymentResultStatus.WaitingConfirmation.ToString() ||
                        r.Status == DeploymentResultStatus.Confirmed.ToString())
                    .ToList();

                if (pendingResults.Count == 0)
                    return;

                logger.LogInformation(
                    "Cancelling {Count} pending deployment results for request {RequestId} due to request status '{Status}'.",
                    pendingResults.Count, requestId, requestStatus);

                foreach (var pendingResult in pendingResults)
                {
                    requestsPersistentSource.UpdateResultStatus(pendingResult, DeploymentResultStatus.Cancelled);

                    eventsPublisher.PublishResultStatusChangedAsync(new DeploymentResultEventData(pendingResult)
                    {
                        Status = DeploymentResultStatus.Cancelled.ToString()
                    }).ContinueWith(t => logger.LogWarning(t.Exception!.InnerException,
                        "fire-and-forget publish failed for result"), TaskContinuationOptions.OnlyOnFaulted);
                }

                logger.LogInformation(
                    "Cancelled {Count} pending deployment results for request {RequestId}.",
                    pendingResults.Count, requestId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Failed to cancel pending deployment results for request {RequestId}.", requestId);
            }
        }

        private bool InitializeDeploymentRequest(DeploymentRequestApiModel request, DateTimeOffset startedTime)
        {
            logger.LogInformation("Setting Request to Running state, Id: " + request.Id);

            if (!requestsPersistentSource.SetRequestStartStatus(
                request,
                DeploymentRequestStatus.Running,
                startedTime))
            {
                logger.LogWarning("Request {RequestId} changed status before execution; not starting it.", request.Id);
                return false;
            }

            eventsPublisher.PublishRequestStatusChangedAsync(new DeploymentRequestEventData(request)
            {
                Status = DeploymentRequestStatus.Running.ToString(),
                StartedTime = startedTime,
            }).ContinueWith(t => logger.LogWarning(t.Exception!.InnerException,
                "fire-and-forget publish failed for requestId={RequestId}", request.Id),
                TaskContinuationOptions.OnlyOnFaulted);
            return true;
        }


        internal IDictionary<string, VariableValue> GetCommonProperties(
            bool environmentIsProd,
            IReadOnlyCollection<PropertyPair>? requestProperties = null)
        {
            var commonProperties = _variableResolver.LoadProperties();

            var reservedKeys = commonProperties.Keys
                .Where(TerraformDestroyOperationConstants.IsReservedLifecycleProperty)
                .ToList();
            foreach (var reservedKey in reservedKeys)
            {
                commonProperties.Remove(reservedKey);
            }

            if (requestProperties != null)
            {
                foreach (var propertyPair in requestProperties)
                {
                    if (TerraformDestroyOperationConstants.IsReservedLifecycleProperty(propertyPair.Name))
                    {
                        commonProperties[propertyPair.Name] = new VariableValue
                        {
                            Value = propertyPair.Value,
                            Type = typeof(string)
                        };
                    }
                }
            }

            commonProperties.Add("IsProd", new VariableValue
            {
                Value = environmentIsProd.ToString(),
                Type = environmentIsProd.ToString().GetType()
            });

            return commonProperties;
        }

        private void SetUpRequestDetailsPropertiesAsProperties(List<PropertyPair> properties)
        {
            if (properties == null)
            {
                return;
            }

            foreach (var propertyPair in properties)
            {
                if (TerraformDestroyOperationConstants.IsReservedLifecycleProperty(propertyPair.Name) ||
                    propertyPair.Name.Contains(':'))
                {
                    continue;
                }

                // Supplied on the deployment request, so recorded as such: these values, and
                // any curated value that interpolates them, must never reach the expression
                // evaluator. It compiles and runs C# inside this process, which holds both
                // deployment credential pairs.
                _variableResolver.SetRequestSuppliedPropertyValue(propertyPair.Name, propertyPair.Value);
            }
        }

        private void SetUpEnvironmentAsProperty(EnvironmentApiModel environment)
        {
            _variableScopeOptionsResolver.SetPropertyValues(_variableResolver, environment);
        }

        private void SetUpConfigValuesAsProperties(EnvironmentApiModel environment)
        {
            // attempt to add any other properties that don't need defaults
            foreach (var configValue in _configValuesPersistentSource.GetAllConfigValues(true))
            {
                if (_variableResolver.GetPropertyValue(configValue.Key) == null)
                {
                    var needToSetConfigForEnv = !configValue.IsForProd.HasValue
                        || (environment.EnvironmentIsProd && configValue.IsForProd.HasValue && configValue.IsForProd.Value)
                        || (!environment.EnvironmentIsProd && configValue.IsForProd.HasValue && !configValue.IsForProd.Value);

                    if (needToSetConfigForEnv)
                        _variableResolver.SetPropertyValue(configValue.Key, configValue.Value);
                }
            }
        }

        private void SetUpRefDataApiUrlAsProperty()
        {
            string? refDataApiUrl = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build()
                .GetSection("AppSettings")["RefDataApiUrl"];
            if (string.IsNullOrEmpty(refDataApiUrl))
            {
                throw new Exception("'RefDataApiUrl' is not specified in 'appsettings.json'.");
            }

            _variableResolver.SetPropertyValue(PropertyValueScopeOptionsFixed.RefDataApiUrl, refDataApiUrl);
        }

        private void SetUpBuildNumberAsProperty(string buildNumber)
        {
            _variableResolver.SetPropertyValue(PropertyValueScopeOptionsFixed.BuildNumber, buildNumber);
        }

        private void SetUpDeploymentLogDirAsProperty()
        {
            var deploymentLogDir = _configValuesPersistentSource.GetConfigValue(
                "DeploymentLogDir",
                Environment.CurrentDirectory);

            _variableResolver.SetPropertyValue(PropertyValueScopeOptionsFixed.DeploymentLogDir, deploymentLogDir);
        }

        private void SetUpScriptRootAsProperty(string scriptRoot)
        {
            if (string.IsNullOrEmpty(scriptRoot))
            {
                throw new InvalidOperationException("ScriptRoot config value is not defined.");
            }

            _variableResolver.SetPropertyValue(PropertyValueScopeOptionsFixed.ScriptRoot, scriptRoot);
        }

        private string SetUpDropFolderAsProperty(string dropFolder)
        {
            if (dropFolder.StartsWith("file"))
            {
                dropFolder = new Uri(dropFolder).LocalPath;
            }
            else if (_gitHubArtifactDownloader.IsGitHubArtifactUrl(dropFolder))
            {
                // GitHub Actions artifact URLs are HTTPS endpoints that must be
                // downloaded and extracted to a local path before PowerShell scripts
                // can use Join-Path on them.
                var localPath = _gitHubArtifactDownloader.DownloadAndExtract(dropFolder);
                logger.LogInformation("Resolved GitHub artifact to local path: {Path}", localPath);
                dropFolder = localPath;
            }
            _variableResolver.SetPropertyValue(PropertyValueScopeOptionsFixed.DropFolder, dropFolder);
            return dropFolder;
        }

        private void SetUpEnvironmentNameAsProperty(string environmentName)
        {
            if (string.IsNullOrEmpty(environmentName))
            {
                throw new InvalidOperationException("Environment name is not specified in the request details.");
            }

            propertyValuesPersistentSource.AddEnvironmentFilter(environmentName);

            _variableResolver.SetPropertyValue(PropertyValueScopeOptionsFixed.EnvironmentName, environmentName);
        }

        private void SetUpEnvOwnerEmailAsProperty(DeploymentRequestApiModel request)
        {
            var envOwnerEmail = request.EnvironmentOwnerEmail;

            if (string.IsNullOrEmpty(envOwnerEmail))
            {
                var freshRequest = requestsPersistentSource.GetRequest(request.Id);
                envOwnerEmail = freshRequest?.EnvironmentOwnerEmail;
            }

            if (!string.IsNullOrEmpty(envOwnerEmail))
            {
                _variableResolver.SetPropertyValue(PropertyValueScopeOptionsFixed.EnvOwnerEmails, envOwnerEmail);
                logger.LogInformation("Set EnvOwnerEmails property for request {RequestId}", request.Id);
            }
            else
            {
                logger.LogWarning("EnvironmentOwnerEmails is not set on request {RequestId}, EnvOwnerEmails property will not be available.",
                    request.Id);
            }
        }

        private bool IsRequestCancelledByAnotherNode(int requestId)
        {
            try
            {
                var currentDbStatus = requestsPersistentSource.GetRequestStatus(requestId).Status;

                return currentDbStatus == DeploymentRequestStatus.Cancelled.ToString()
                    || currentDbStatus == DeploymentRequestStatus.Cancelling.ToString();
            }
            catch (Exception ex)
            {
                // If we can't verify the status, err on the side of continuing so that
                // a transient DB error doesn't unnecessarily abort an in-progress deployment.
                logger.LogWarning(ex,
                    "Failed to verify cancellation status for request {RequestId} from another node.", requestId);
                return false;
            }
        }

        internal static bool IsDestroyRequest(
            DeploymentRequestDetail? requestDetail,
            EnvironmentApiModel? environment = null)
        {
            if (requestDetail?.Properties != null)
            {
                if (requestDetail.Properties.Any(p =>
                        string.Equals(p.Name, ComponentProcessor.TerraformOperationPropertyName, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(p.Value, ComponentProcessor.DestroyOperationValue, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }

                if (requestDetail.Properties.Any(p =>
                        string.Equals(p.Name, ComponentProcessor.IsDestroyPropertyName, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(p.Value, "true", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return false;
        }

        internal static IList<ComponentApiModel> OrderComponentsForExecution(
            IList<ComponentApiModel> submittedComponents,
            bool isDestroyRequest,
            DeploymentRequestDetail? requestDetail = null)
        {
            if (submittedComponents.Count <= 1)
            {
                return submittedComponents;
            }

            var requestProperties = requestDetail?.Properties;
            var hasExplicitEphemeralOrder = requestProperties != null &&
                requestProperties.Any(p =>
                    string.Equals(p.Name, TerraformDestroyOperationConstants.EphemeralTargetStatePropertyName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.Name, TerraformDestroyOperationConstants.DeferredValidationComponentsPropertyName, StringComparison.OrdinalIgnoreCase));

            if (!isDestroyRequest && !hasExplicitEphemeralOrder)
            {
                return submittedComponents;
            }

            IList<ComponentApiModel> ordered = submittedComponents;
            var rankById = new Dictionary<int, int>();
            var rawDestroyIds = requestProperties?
                .FirstOrDefault(p => string.Equals(p.Name, TerraformDestroyOperationConstants.DestroyComponentIdsPropertyName, StringComparison.OrdinalIgnoreCase))
                ?.Value;
            if (!string.IsNullOrWhiteSpace(rawDestroyIds))
            {
                var idTokens = rawDestroyIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                for (var i = 0; i < idTokens.Length; i++)
                {
                    rankById[int.Parse(idTokens[i], System.Globalization.CultureInfo.InvariantCulture)] = i;
                }
            }

            var requestComponents = requestDetail?.Components;
            if (requestComponents is { Count: > 1 } || rankById.Count > 0)
            {
                var rankByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var compCount = requestComponents?.Count ?? 0;
                for (var i = 0; i < compCount; i++)
                {
                    var name = requestComponents![i];
                    if (!string.IsNullOrWhiteSpace(name) && !rankByName.ContainsKey(name))
                    {
                        rankByName[name] = i;
                    }
                }

                ordered = submittedComponents
                    .Select((comp, idx) => (
                        Component: comp,
                        Rank: comp.ComponentId.HasValue && rankById.TryGetValue(comp.ComponentId.Value, out var idRank)
                            ? idRank
                            : (!string.IsNullOrWhiteSpace(comp.ComponentName) && rankByName.TryGetValue(comp.ComponentName, out var r)
                                ? r
                                : int.MaxValue),
                        OriginalIndex: idx))
                    .OrderBy(x => x.Rank)
                    .ThenBy(x => x.OriginalIndex)
                    .Select(x => x.Component)
                    .ToList();
            }

            var isPreReversed = requestDetail?.Properties != null &&
                requestDetail.Properties.Any(p =>
                    string.Equals(p.Name, TerraformDestroyOperationConstants.DestroyOrderPreReversedPropertyName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(p.Value, "true", StringComparison.OrdinalIgnoreCase));

            if (!isDestroyRequest || isPreReversed)
            {
                return ordered;
            }

            return ordered.Reverse().ToList();
        }

        internal void TryMarkEphemeralTeardownFailedOnException(
            EnvironmentApiModel? environment,
            bool isDestroyRequest,
            string? fallbackEnvironmentName,
            DeploymentRequestDetail? requestDetail = null)
        {
            try
            {
                var targetEnv = environment;
                if (targetEnv == null && !string.IsNullOrWhiteSpace(fallbackEnvironmentName))
                {
                    targetEnv = environmentsPersistentSource.GetEnvironment(fallbackEnvironmentName);
                }

                if (targetEnv is { IsEphemeral: true })
                {
                    var expectedEnvironmentId = GetExpectedEphemeralEnvironmentId(requestDetail);
                    if (expectedEnvironmentId.HasValue &&
                        targetEnv.EnvironmentId > 0 &&
                        expectedEnvironmentId.Value != targetEnv.EnvironmentId)
                    {
                        return;
                    }

                    if (isDestroyRequest ||
                        string.Equals(targetEnv.EphemeralState, EphemeralEnvironmentStates.TearingDown, StringComparison.OrdinalIgnoreCase))
                    {
                        if (targetEnv.EnvironmentId > 0)
                        {
                            environmentsPersistentSource.UpdateEphemeralState(
                                targetEnv.EnvironmentId,
                                EphemeralEnvironmentStates.TeardownFailed);
                        }
                        else
                        {
                            environmentsPersistentSource.UpdateEphemeralState(
                                targetEnv.EnvironmentName,
                                EphemeralEnvironmentStates.TeardownFailed);
                        }
                        targetEnv.EphemeralState = EphemeralEnvironmentStates.TeardownFailed;
                    }
                    else if (IsEphemeralLifecycleRequest(requestDetail) &&
                             string.Equals(targetEnv.EphemeralState, EphemeralEnvironmentStates.Provisioning, StringComparison.OrdinalIgnoreCase))
                    {
                        environmentsPersistentSource.TryTransitionEphemeralState(
                            targetEnv.EnvironmentName,
                            new[] { EphemeralEnvironmentStates.Provisioning },
                            EphemeralEnvironmentStates.ProvisionFailed,
                            expectedEnvironmentId);
                        targetEnv.EphemeralState = EphemeralEnvironmentStates.ProvisionFailed;
                    }
                    else if (IsEphemeralLifecycleRequest(requestDetail) &&
                             string.Equals(targetEnv.EphemeralState, EphemeralEnvironmentStates.Testing, StringComparison.OrdinalIgnoreCase))
                    {
                        environmentsPersistentSource.TryTransitionEphemeralState(
                            targetEnv.EnvironmentName,
                            new[] { EphemeralEnvironmentStates.Testing },
                            EphemeralEnvironmentStates.ValidationFailed,
                            expectedEnvironmentId);
                        targetEnv.EphemeralState = EphemeralEnvironmentStates.ValidationFailed;
                    }
                }
            }
            catch (Exception stateEx)
            {
                logger.LogWarning(stateEx, "Failed to transition ephemeral environment to TeardownFailed after unhandled request exception.");
            }
        }

        internal DeploymentRequestStatus UpdateEphemeralEnvironmentLifecycleState(
            EnvironmentApiModel? environment,
            DeploymentRequestDetail? requestDetail,
            bool isDestroyRequest,
            DeploymentRequestStatus finalStatus,
            DeploymentRequestApiModel? sourceRequest = null)
        {
            if (environment is not { IsEphemeral: true })
            {
                return finalStatus;
            }

            var expectedEnvironmentId = GetExpectedEphemeralEnvironmentId(requestDetail);
            if (expectedEnvironmentId.HasValue &&
                environment.EnvironmentId > 0 &&
                expectedEnvironmentId.Value != environment.EnvironmentId)
            {
                return finalStatus;
            }

            if (isDestroyRequest)
            {
                if (finalStatus == DeploymentRequestStatus.Completed)
                {
                    if (HasRemainingActiveDestroyRequests(environment.EnvironmentName, sourceRequest?.Id ?? 0, requestDetail))
                    {
                        return finalStatus;
                    }

                    if ((sourceRequest?.Id ?? 0) > 0)
                    {
                        var freshEnvironment = !string.IsNullOrWhiteSpace(environment.EnvironmentName)
                            ? environmentsPersistentSource.GetEnvironment(environment.EnvironmentName)
                            : null;
                        if (freshEnvironment != null && !string.IsNullOrWhiteSpace(freshEnvironment.EphemeralState))
                        {
                            environment.EphemeralState = freshEnvironment.EphemeralState;
                        }

                        if (string.Equals(environment.EphemeralState, EphemeralEnvironmentStates.TeardownFailed, StringComparison.OrdinalIgnoreCase))
                        {
                            return finalStatus;
                        }
                    }

                    if (ShouldDeleteEnvironmentOnTeardown(requestDetail))
                    {
                        try
                        {
                            var deleted = environmentsPersistentSource.DeleteEnvironmentInternal(environment.EnvironmentId);
                            if (!deleted)
                            {
                                environmentsPersistentSource.UpdateEphemeralState(
                                    environment.EnvironmentId,
                                    EphemeralEnvironmentStates.TeardownFailed);
                                environment.EphemeralState = EphemeralEnvironmentStates.TeardownFailed;
                                return DeploymentRequestStatus.Failed;
                            }
                            environment.EphemeralState = EphemeralEnvironmentStates.TornDown;
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(
                                ex,
                                "Failed to delete ephemeral environment '{EnvironmentName}' ({EnvironmentId}) after destroy; transitioning to TeardownFailed.",
                                environment.EnvironmentName,
                                environment.EnvironmentId);
                            environmentsPersistentSource.UpdateEphemeralState(
                                environment.EnvironmentId,
                                EphemeralEnvironmentStates.TeardownFailed);
                            environment.EphemeralState = EphemeralEnvironmentStates.TeardownFailed;
                            return DeploymentRequestStatus.Failed;
                        }
                    }
                    else
                    {
                        environmentsPersistentSource.UpdateEphemeralState(
                            environment.EnvironmentId,
                            EphemeralEnvironmentStates.TornDown);
                        environment.EphemeralState = EphemeralEnvironmentStates.TornDown;
                    }
                }
                else if (finalStatus == DeploymentRequestStatus.Failed ||
                         finalStatus == DeploymentRequestStatus.Errored ||
                         finalStatus == DeploymentRequestStatus.Cancelled ||
                         finalStatus == DeploymentRequestStatus.Abandoned)
                {
                    environmentsPersistentSource.UpdateEphemeralState(
                        environment.EnvironmentId,
                        EphemeralEnvironmentStates.TeardownFailed);
                    environment.EphemeralState = EphemeralEnvironmentStates.TeardownFailed;
                    CancelRemainingActiveDestroyRequests(environment.EnvironmentName, sourceRequest?.Id ?? 0);
                }
                return finalStatus;
            }

            if (!IsEphemeralLifecycleRequest(requestDetail))
            {
                return finalStatus;
            }

            if (finalStatus == DeploymentRequestStatus.Completed)
            {
                var deferredValidationTokens = GetDeferredValidationComponentTokens(requestDetail);
                if (deferredValidationTokens.Count > 0)
                {
                    if (environmentsPersistentSource.TryTransitionEphemeralState(
                            environment.EnvironmentName,
                            new[] { EphemeralEnvironmentStates.Provisioning, EphemeralEnvironmentStates.ProvisionFailed },
                            EphemeralEnvironmentStates.Testing,
                            expectedEnvironmentId))
                    {
                        environment.EphemeralState = EphemeralEnvironmentStates.Testing;
                        SubmitDeferredValidationRequest(environment, requestDetail, sourceRequest, deferredValidationTokens);
                    }
                    return finalStatus;
                }

                if (ShouldTransitionToTestingState(requestDetail))
                {
                    if (environmentsPersistentSource.TryTransitionEphemeralState(
                            environment.EnvironmentName,
                            new[]
                            {
                                EphemeralEnvironmentStates.Provisioning,
                                EphemeralEnvironmentStates.ProvisionFailed,
                                EphemeralEnvironmentStates.ValidationFailed,
                                EphemeralEnvironmentStates.Ready
                            },
                            EphemeralEnvironmentStates.Testing,
                            expectedEnvironmentId))
                    {
                        environment.EphemeralState = EphemeralEnvironmentStates.Testing;
                    }
                    return finalStatus;
                }

                if (environmentsPersistentSource.TryTransitionEphemeralState(
                        environment.EnvironmentName,
                        new[]
                        {
                            EphemeralEnvironmentStates.Provisioning,
                            EphemeralEnvironmentStates.ProvisionFailed,
                            EphemeralEnvironmentStates.Testing,
                            EphemeralEnvironmentStates.ValidationFailed
                        },
                        EphemeralEnvironmentStates.Ready,
                        expectedEnvironmentId))
                {
                    environment.EphemeralState = EphemeralEnvironmentStates.Ready;
                }
                return finalStatus;
            }

            if (finalStatus == DeploymentRequestStatus.Failed ||
                finalStatus == DeploymentRequestStatus.Errored ||
                finalStatus == DeploymentRequestStatus.Cancelled ||
                finalStatus == DeploymentRequestStatus.Abandoned)
            {
                if (string.Equals(environment.EphemeralState, EphemeralEnvironmentStates.Provisioning, StringComparison.OrdinalIgnoreCase))
                {
                    if (environmentsPersistentSource.TryTransitionEphemeralState(
                            environment.EnvironmentName,
                            new[] { EphemeralEnvironmentStates.Provisioning },
                            EphemeralEnvironmentStates.ProvisionFailed,
                            expectedEnvironmentId))
                    {
                        environment.EphemeralState = EphemeralEnvironmentStates.ProvisionFailed;
                    }
                }
                else if (string.Equals(environment.EphemeralState, EphemeralEnvironmentStates.Testing, StringComparison.OrdinalIgnoreCase))
                {
                    if (environmentsPersistentSource.TryTransitionEphemeralState(
                            environment.EnvironmentName,
                            new[] { EphemeralEnvironmentStates.Testing },
                            EphemeralEnvironmentStates.ValidationFailed,
                            expectedEnvironmentId))
                    {
                        environment.EphemeralState = EphemeralEnvironmentStates.ValidationFailed;
                    }
                }
            }

            return finalStatus;
        }

        private static List<string> GetDeferredValidationComponentTokens(DeploymentRequestDetail requestDetail)
        {
            var raw = requestDetail.Properties
                .FirstOrDefault(p => string.Equals(
                    p.Name,
                    TerraformDestroyOperationConstants.DeferredValidationComponentsPropertyName,
                    StringComparison.OrdinalIgnoreCase))
                ?.Value;

            if (string.IsNullOrWhiteSpace(raw))
            {
                return new List<string>();
            }

            return raw
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }

        private void SubmitDeferredValidationRequest(
            EnvironmentApiModel environment,
            DeploymentRequestDetail requestDetail,
            DeploymentRequestApiModel sourceRequest,
            IReadOnlyList<string> deferredValidationTokens)
        {
            var byId = new Dictionary<int, string>();
            var mappedProjects = environmentsPersistentSource.GetMappedProjects(environment.EnvironmentName);
            if (mappedProjects != null)
            {
                foreach (var project in mappedProjects)
                {
                    var components = manageProjectsPersistentSource.GetOrderedComponents(project.ProjectId);
                    if (components == null)
                    {
                        continue;
                    }

                    IndexComponentsById(components, byId);
                }
            }

            var resolvedComponentNames = new List<string>();
            foreach (var token in deferredValidationTokens)
            {
                if (int.TryParse(token, out var componentId) && byId.TryGetValue(componentId, out var componentName))
                {
                    resolvedComponentNames.Add(componentName);
                }
                else
                {
                    resolvedComponentNames.Add(token);
                }
            }

            var deferredBuildNum = requestDetail.Properties
                .FirstOrDefault(p => string.Equals(
                    p.Name,
                    TerraformDestroyOperationConstants.DeferredValidationBuildNumPropertyName,
                    StringComparison.OrdinalIgnoreCase))
                ?.Value;
            var deferredBuildUrl = requestDetail.Properties
                .FirstOrDefault(p => string.Equals(
                    p.Name,
                    TerraformDestroyOperationConstants.DeferredValidationBuildUrlPropertyName,
                    StringComparison.OrdinalIgnoreCase))
                ?.Value;

            var projectName = sourceRequest.Project ?? string.Empty;
            var buildNumber = !string.IsNullOrWhiteSpace(deferredBuildNum)
                ? deferredBuildNum
                : (sourceRequest.BuildNumber ?? "ephemeral-validation");
            var dropLocation = IsUsableDropLocation(requestDetail.BuildDetail?.DropLocation)
                ? requestDetail.BuildDetail!.DropLocation!
                : (IsUsableDropLocation(sourceRequest.DropLocation)
                    ? sourceRequest.DropLocation!
                    : (IsUsableDropLocation(deferredBuildUrl)
                        ? deferredBuildUrl!.Trim()
                        : "catalog://bundled"));
            var buildUri = !IsCatalogSentinelValue(sourceRequest.BuildUri) &&
                           sourceRequest.BuildUri!.StartsWith("vstfs://", StringComparison.OrdinalIgnoreCase)
                ? sourceRequest.BuildUri
                : (!string.IsNullOrWhiteSpace(deferredBuildUrl)
                    ? NormalizeBuildUriValue(deferredBuildUrl)
                    : NormalizeBuildUriValue(sourceRequest.BuildUri));
            var userName = !string.IsNullOrWhiteSpace(sourceRequest.UserName) ? sourceRequest.UserName : "system:ephemeral-validation";

            var validationProperties = requestDetail.Properties
                .Where(p => !string.Equals(p.Name, TerraformDestroyOperationConstants.DeferredValidationComponentsPropertyName, StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(p.Name, TerraformDestroyOperationConstants.DeferredValidationBuildNumPropertyName, StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(p.Name, TerraformDestroyOperationConstants.DeferredValidationBuildUrlPropertyName, StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(p.Name, TerraformDestroyOperationConstants.EphemeralTargetStatePropertyName, StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(p.Name, TerraformDestroyOperationConstants.EphemeralEnvironmentIdPropertyName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            validationProperties.Add(new PropertyPair(TerraformDestroyOperationConstants.EphemeralTargetStatePropertyName, EphemeralEnvironmentStates.Ready));
            validationProperties.Add(new PropertyPair(
                TerraformDestroyOperationConstants.EphemeralEnvironmentIdPropertyName,
                environment.EnvironmentId.ToString(System.Globalization.CultureInfo.InvariantCulture)));

            var validationDetail = new DeploymentRequestDetail
            {
                EnvironmentName = environment.EnvironmentName,
                BuildDetail = new BuildDetail
                {
                    Project = projectName,
                    BuildNumber = buildNumber,
                    DropLocation = dropLocation,
                    Uri = buildUri
                },
                Components = resolvedComponentNames,
                Properties = validationProperties
            };

            var serializer = new DeploymentRequestDetailSerializer();
            requestsPersistentSource.SubmitRequest(new Dorc.PersistentData.Model.DeploymentRequest
            {
                RequestDetails = serializer.Serialize(validationDetail),
                UserName = userName,
                RequestedTime = DateTimeOffset.UtcNow,
                Project = projectName,
                Environment = environment.EnvironmentName,
                BuildNumber = buildNumber,
                Components = string.Join("|", resolvedComponentNames),
                IsProd = false
            });
        }

        private static void IndexComponentsById(IEnumerable<ComponentApiModel> components, IDictionary<int, string> byId)
        {
            foreach (var component in components)
            {
                if (component.ComponentId.HasValue && !string.IsNullOrWhiteSpace(component.ComponentName))
                {
                    byId[component.ComponentId.Value] = component.ComponentName;
                }

                if (component.Children is { Count: > 0 })
                {
                    IndexComponentsById(component.Children, byId);
                }
            }
        }

        private static bool IsCatalogSentinelValue(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ||
                   string.Equals(value, "catalog://bundled", StringComparison.OrdinalIgnoreCase) ||
                   value.StartsWith("dorc-catalog://", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeBuildUriValue(string? rawBuildUrl)
        {
            if (string.IsNullOrWhiteSpace(rawBuildUrl))
            {
                return string.Empty;
            }

            var trimmed = rawBuildUrl.Trim();
            var match = System.Text.RegularExpressions.Regex.Match(
                trimmed,
                @"(?:[?&]buildId=|/_apis/build/Builds/)(\d+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success
                ? $"vstfs:///Build/Build/{match.Groups[1].Value}"
                : trimmed;
        }

        private static bool IsUsableDropLocation(string? candidate)
        {
            if (IsCatalogSentinelValue(candidate))
            {
                return false;
            }

            var trimmed = candidate!.Trim();
            if (trimmed.StartsWith("vstfs://", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return !((trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                      trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) &&
                     System.Text.RegularExpressions.Regex.IsMatch(
                         trimmed,
                         @"(?:[?&]buildId=|/_apis/build/Builds/)\d+",
                         System.Text.RegularExpressions.RegexOptions.IgnoreCase) &&
                     !trimmed.Contains("/artifacts", StringComparison.OrdinalIgnoreCase));
        }

        private static int? GetExpectedEphemeralEnvironmentId(DeploymentRequestDetail? requestDetail)
        {
            var raw = requestDetail?.Properties?
                .FirstOrDefault(p => string.Equals(
                    p.Name,
                    TerraformDestroyOperationConstants.EphemeralEnvironmentIdPropertyName,
                    StringComparison.OrdinalIgnoreCase))
                ?.Value;
            return int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed) && parsed > 0
                ? parsed
                : null;
        }

        private static bool IsEphemeralLifecycleRequest(DeploymentRequestDetail? requestDetail) =>
            requestDetail?.Properties is { Count: > 0 } &&
            requestDetail.Properties.Any(p =>
                string.Equals(p.Name, "RunValidationRequest", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.Name, TerraformDestroyOperationConstants.EphemeralTargetStatePropertyName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.Name, TerraformDestroyOperationConstants.DeferredValidationComponentsPropertyName, StringComparison.OrdinalIgnoreCase));

        private static bool ShouldTransitionToTestingState(DeploymentRequestDetail requestDetail) =>
            requestDetail.Properties.Any(p =>
                (string.Equals(p.Name, "RunValidationRequest", StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(p.Value, "true", StringComparison.OrdinalIgnoreCase)) ||
                (string.Equals(p.Name, "EphemeralTargetState", StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(p.Value, EphemeralEnvironmentStates.Testing, StringComparison.OrdinalIgnoreCase)));

        private List<DeploymentRequestApiModel> GetActiveSiblingDestroyRequests(string? environmentName, int excludedRequestId)
        {
            if (string.IsNullOrWhiteSpace(environmentName) || excludedRequestId <= 0)
            {
                return new List<DeploymentRequestApiModel>();
            }

            var primary = requestsPersistentSource.GetRequestsWithStatus(
                DeploymentRequestStatus.Pending,
                DeploymentRequestStatus.Running,
                DeploymentRequestStatus.Requesting,
                DeploymentRequestStatus.WaitingConfirmation,
                isProd: false) ?? Enumerable.Empty<DeploymentRequestApiModel>();
            var secondary = requestsPersistentSource.GetRequestsWithStatus(
                DeploymentRequestStatus.Confirmed,
                DeploymentRequestStatus.Paused,
                isProd: false) ?? Enumerable.Empty<DeploymentRequestApiModel>();

            return primary.Concat(secondary)
                .Where(r =>
                    r.Id != excludedRequestId &&
                    string.Equals(r.EnvironmentName, environmentName, StringComparison.OrdinalIgnoreCase) &&
                    Dorc.PersistentData.Sources.RequestsPersistentSource.IsDestroyRequestXml(r.RequestDetails))
                .ToList();
        }

        private bool HasRemainingActiveDestroyRequests(
            string? environmentName,
            int completedRequestId,
            DeploymentRequestDetail? requestDetail = null)
        {
            if (GetActiveSiblingDestroyRequests(environmentName, completedRequestId).Count > 0)
            {
                return true;
            }

            if (completedRequestId <= 0 || requestDetail?.Properties == null)
            {
                return false;
            }

            var batchCountRaw = requestDetail.Properties
                .FirstOrDefault(p => string.Equals(
                    p.Name,
                    TerraformDestroyOperationConstants.DestroyBatchCountPropertyName,
                    StringComparison.OrdinalIgnoreCase))
                ?.Value;

            if (!int.TryParse(batchCountRaw, out var expectedBatchCount) || expectedBatchCount <= 1)
            {
                return false;
            }

            var siblingIds = new HashSet<int>();
            var siblingIdsRaw = requestDetail.Properties
                .FirstOrDefault(p => string.Equals(
                    p.Name,
                    TerraformDestroyOperationConstants.SiblingDestroyRequestIdsPropertyName,
                    StringComparison.OrdinalIgnoreCase))
                ?.Value;

            if (!string.IsNullOrWhiteSpace(siblingIdsRaw))
            {
                foreach (var token in siblingIdsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (int.TryParse(token, out var parsedId) && parsedId > 0 && parsedId != completedRequestId)
                    {
                        siblingIds.Add(parsedId);
                    }
                }
            }

            var expectedSiblingCount = expectedBatchCount - 1;
            for (var candidateId = completedRequestId + 1;
                 siblingIds.Count < expectedSiblingCount && candidateId <= completedRequestId + expectedSiblingCount;
                 candidateId++)
            {
                var candidate = requestsPersistentSource.GetRequest(candidateId);
                if (candidate != null &&
                    string.Equals(candidate.EnvironmentName, environmentName, StringComparison.OrdinalIgnoreCase) &&
                    Dorc.PersistentData.Sources.RequestsPersistentSource.IsDestroyRequestXml(candidate.RequestDetails))
                {
                    siblingIds.Add(candidateId);
                }
            }

            if (siblingIds.Count < expectedSiblingCount)
            {
                return true;
            }

            foreach (var siblingId in siblingIds)
            {
                var siblingStatus = requestsPersistentSource.GetRequestStatus(siblingId)?.Status;
                if (!string.Equals(siblingStatus, DeploymentRequestStatus.Completed.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void CancelRemainingActiveDestroyRequests(string? environmentName, int failedRequestId)
        {
            foreach (var sibling in GetActiveSiblingDestroyRequests(environmentName, failedRequestId))
            {
                var isRunning = string.Equals(sibling.Status, DeploymentRequestStatus.Running.ToString(), StringComparison.OrdinalIgnoreCase)
                    || string.Equals(sibling.Status, DeploymentRequestStatus.Requesting.ToString(), StringComparison.OrdinalIgnoreCase);
                var targetStatus = isRunning
                    ? DeploymentRequestStatus.Cancelling
                    : DeploymentRequestStatus.Cancelled;
                requestsPersistentSource.UpdateRequestStatus(
                    sibling.Id,
                    targetStatus,
                    "system:ephemeral-teardown",
                    DateTimeOffset.UtcNow);
            }
        }

        internal static bool ShouldDeleteEnvironmentOnTeardown(DeploymentRequestDetail? requestDetail)
        {
            if (requestDetail?.Properties == null)
            {
                return true;
            }

            if (requestDetail.Properties.Any(p =>
                (string.Equals(p.Name, TerraformDestroyOperationConstants.DeleteEnvironmentOnSuccessPropertyName, StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(p.Value, "false", StringComparison.OrdinalIgnoreCase)) ||
                (string.Equals(p.Name, "TeardownPolicy", StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(p.Value, "KeepRecord", StringComparison.OrdinalIgnoreCase))))
            {
                return false;
            }

            return true;
        }
    }
}
