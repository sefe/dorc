using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core;
using Dorc.Core.Configuration;
using Dorc.Core.Events;
using Dorc.Core.Interfaces;
using Dorc.Core.Security;
using Dorc.Core.VariableResolution;
using Dorc.Monitor.Notifications;
using Dorc.Monitor.Pipes;
using Dorc.Monitor.Security;
using Dorc.PersistentData.Security;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Runtime.Versioning;
using System.Security.Principal;
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
        private readonly ISourceHostAllowList _sourceHosts;
        private readonly IScriptScopeConfigValues _scriptScopeConfigValues;
        private readonly IDeploymentCredentialSource _credentialSource;
        private readonly IConfigurationSettings? _configurationSettings;

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
            IGitHubArtifactDownloader gitHubArtifactDownloader,
            ISourceHostAllowList sourceHosts,
            IScriptScopeConfigValues scriptScopeConfigValues,
            IDeploymentCredentialSource? credentialSource = null,
            IConfigurationSettings? configurationSettings = null)
        {
            _loggerFactory = loggerFactory;
            _propertyEvaluator = propertyEvaluator;
            _configValuesPersistentSource = configValuesPersistentSource;
            _gitHubArtifactDownloader = gitHubArtifactDownloader;
            _sourceHosts = sourceHosts;
            _scriptScopeConfigValues = scriptScopeConfigValues;
            _credentialSource = credentialSource
                ?? new ConfigValueDeploymentCredentialSource(
                    configValuesPersistentSource,
                    _loggerFactory.CreateLogger<ConfigValueDeploymentCredentialSource>());
            _configurationSettings = configurationSettings;
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

                _variableResolver = new VariableResolver(propertyValuesPersistentSource, _loggerFactory, _propertyEvaluator, _scriptScopeConfigValues);

                string? resolvedDropFolder = null;
                var identityAdoption = new RequestExecutionIdentityAdoption();
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

                        var requestDetail = requestToExecute.Details;

                        var environmentName = requestDetail.EnvironmentName;

                        SetUpEnvironmentNameAsProperty(environmentName);

                        var environment = environmentsPersistentSource.GetEnvironment(environmentName);

                        if (environment.EnvironmentSecure)
                        {
                            logger.LogInformation($"Environment '{environmentName}' is secure; not using default property values.");
                        }

                        resolvedDropFolder = SetUpDropFolderAsProperty(
                            requestDetail.BuildDetail.DropLocation,
                            requestToExecute.Request.IsProd,
                            environment.ExecutionIdentityReference);

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

                        var orderedNonSkippedComponents = requestsPersistentSource.GetSubmittedComponents(requestToExecute.Request.Id);

                        logger.LogInformation($"Found {orderedNonSkippedComponents.Count} non-skipped components for request {requestToExecute.Request.Id}:");

                        if (!orderedNonSkippedComponents.Any())
                        {
                            logger.LogWarning($"No non-skipped components are found for the request with id '{requestToExecute.Request.Id}'.");

                            var completedTime = DateTimeOffset.Now;

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
                            .Where(component => component.IsEnabled);

                        if (!orderedEnabledNonSkippedComponents.Any())
                        {
                            logger.LogWarning($"No enabled non-skipped components are found for the request with id '{requestToExecute.Request.Id}'.");

                            var completedTime = DateTimeOffset.Now;

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
                            environment.EnvironmentIsProd);

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
                                        if (enabledNonSkippedComponent.StopOnFailure)
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
                                    // The environment's own execution identity, where it names
                                    // one. Null means the tier default, which is how every
                                    // environment behaves until it is bound.
                                    environment.ExecutionIdentityReference,
                                    identityAdoption,
                                    scriptRoot,
                                    commonProperties,
                                    compositeCts.Token);

                                if (!isSuccessful)
                                {
                                    deploymentRequestStatus = DeploymentRequestStatus.Failed;

                                    if (enabledNonSkippedComponent.StopOnFailure)
                                    {
                                        logger.LogWarning(
                                            "Deployment of remaining components is aborted due to StopOnFailure flag on component '{ComponentName}'.",
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

                                if (enabledNonSkippedComponent.StopOnFailure)
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
                    ReportExecutionIdentityAdoption(identityAdoption);

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


        private IDictionary<string, VariableValue> GetCommonProperties(bool environmentIsProd)
        {
            var commonProperties = _variableResolver.LoadProperties();

            foreach (var withheldKey in commonProperties.Keys.Where(_scriptScopeConfigValues.IsWithheld).ToArray())
            {
                commonProperties.Remove(withheldKey);
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
                if (_scriptScopeConfigValues.IsWithheld(propertyPair.Name))
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
            var configValues = _configValuesPersistentSource.GetAllConfigValues(true).ToList();

            ReportSecureConfigValuesStillReachingScripts(configValues);

            // attempt to add any other properties that don't need defaults
            foreach (var configValue in configValues)
            {
                if (_scriptScopeConfigValues.IsWithheld(configValue))
                {
                    // Withheld either by the reserved-key list or by the value's own
                    // classification. Every secure value was previously decrypted and injected
                    // subject only to a coarse production flag, so DOrc's own operating
                    // credentials were delivered in cleartext, as ordinary PowerShell variables,
                    // to the deployment scripts of every environment the flag admitted.
                    continue;
                }

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

        /// <summary>
        /// Records which secure values a deployment still receives.
        ///
        /// The withheld set is the three keys a production share scan showed no script
        /// consumes. The rest stay published because scripts genuinely depend on them - one of
        /// them is how scripts reach target servers at all - and they cannot be withheld until
        /// their consumers are migrated. Reporting what is derived from the estate, rather than
        /// from a list in code, keeps that backlog visible and catches a secure value added
        /// after this was written.
        /// </summary>
        private void ReportSecureConfigValuesStillReachingScripts(IEnumerable<ConfigValueApiModel> configValues)
        {
            var stillPublished = _scriptScopeConfigValues.StillPublishedSecureKeys(configValues);

            if (stillPublished.Count > 0)
            {
                logger.LogWarning(
                    "{Count} secure configuration value(s) are still published into deployment script"
                    + " scope: {Keys}. Each is readable by any script the deployment runs.",
                    stillPublished.Count,
                    string.Join(", ", stillPublished));
            }
        }

        /// <summary>
        /// Records how many credential resolutions used an environment's own execution identity
        /// and how many fell back to the tier default.
        ///
        /// Migration progress is otherwise invisible, and "we have started binding identities"
        /// is not the same claim as "the estate is bound". Reported from the count rather than
        /// from a configuration flag, so it cannot be wrong.
        /// </summary>
        private void ReportExecutionIdentityAdoption(RequestExecutionIdentityAdoption identityAdoption)
        {
            var (bound, fallback) = identityAdoption.Counts;

            logger.LogInformation(
                "Execution identity adoption for request: {Bound} resolution(s) used an environment's"
                + " own identity, {Fallback} fell back to the shared tier default. Every fallback"
                + " shares one account across its whole tier.",
                bound,
                fallback);
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

        private string SetUpDropFolderAsProperty(
            string dropFolder,
            bool isProduction,
            string? executionIdentityReference)
        {
            if (_gitHubArtifactDownloader.IsGitHubArtifactUrl(dropFolder))
            {
                // GitHub Actions artifact URLs are HTTPS endpoints that must be
                // downloaded and extracted to a local path before PowerShell scripts
                // can use Join-Path on them.
                var localPath = _gitHubArtifactDownloader.DownloadAndExtract(dropFolder);
                logger.LogInformation("Resolved GitHub artifact to local path: {Path}", localPath);
                try
                {
                    if (OperatingSystem.IsWindows())
                    {
                        RestrictExtractedArtifactDrop(localPath, isProduction, executionIdentityReference);
                    }

                    _variableResolver.SetPropertyValue(PropertyValueScopeOptionsFixed.DropFolder, localPath);
                    return localPath;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    CleanupExtractedArtifactDropBestEffort(localPath);
                    throw;
                }
            }
            else if (!string.IsNullOrWhiteSpace(dropFolder)
                && !dropFolder.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                RequirePermittedRemoteDropFolder(dropFolder);
                if (dropFolder.StartsWith("file", StringComparison.OrdinalIgnoreCase))
                {
                    dropFolder = new Uri(SourceHost.NormalizeExtendedUnc(dropFolder)).LocalPath;
                }
                else
                {
                    dropFolder = SourceHost.NormalizeExtendedUnc(dropFolder);
                }
            }
            _variableResolver.SetPropertyValue(PropertyValueScopeOptionsFixed.DropFolder, dropFolder);
            return dropFolder;
        }

        private void CleanupExtractedArtifactDropBestEffort(string localPath)
        {
            if (string.IsNullOrWhiteSpace(localPath))
            {
                return;
            }

            try
            {
                _gitHubArtifactDownloader.Cleanup(localPath);
            }
            catch (Exception cleanupEx) when (cleanupEx is not OutOfMemoryException)
            {
                logger.LogWarning(
                    cleanupEx,
                    "Failed best-effort cleanup of extracted GitHub artifact drop '{Path}' after restriction failure.",
                    localPath);
            }
        }

        [SupportedOSPlatform("windows")]
        private void RestrictExtractedArtifactDrop(
            string localPath,
            bool isProduction,
            string? executionIdentityReference)
        {
            if (string.IsNullOrWhiteSpace(localPath)
                || SourceHost.IsRemoteOrUnc(localPath)
                || !Directory.Exists(localPath))
            {
                return;
            }

            var fullLocalPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(localPath));
            var programDataRootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(DorcProgramData.Root));
            if (fullLocalPath.StartsWith(programDataRootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                RestrictedDirectory.EnsureProgramDataRoot(DorcProgramData.Root);
                var parentDirectory = Path.GetDirectoryName(fullLocalPath);
                if (!string.IsNullOrEmpty(parentDirectory)
                    && !string.Equals(
                        Path.TrimEndingDirectorySeparator(parentDirectory),
                        programDataRootFull,
                        StringComparison.OrdinalIgnoreCase))
                {
                    RestrictedDirectory.Ensure(parentDirectory);
                }
            }

            var readers = ResolveArtifactDropReaderIdentities(
                fullLocalPath,
                isProduction,
                executionIdentityReference);
            RestrictedDirectory.RestrictReadTree(fullLocalPath, readers);
        }

        [SupportedOSPlatform("windows")]
        private IReadOnlyList<IdentityReference> ResolveArtifactDropReaderIdentities(
            string localPath,
            bool isProduction,
            string? executionIdentityReference)
        {
            DeploymentCredential? credential = null;
            try
            {
                try
                {
                    credential = _credentialSource.Resolve(
                        isProduction ? DeploymentTier.Production : DeploymentTier.NonProduction,
                        executionIdentityReference);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    logger.LogWarning(
                        ex,
                        "Deployment credential could not be resolved while restricting GitHub artifact drop '{ArtifactPath}'; restricting to Monitor/SYSTEM/Administrators only.",
                        localPath);
                    return [];
                }

                if (credential is null || string.IsNullOrWhiteSpace(credential.UserName))
                {
                    logger.LogWarning(
                        "No deployment credential was resolved while restricting GitHub artifact drop '{ArtifactPath}'; restricting to Monitor/SYSTEM/Administrators only.",
                        localPath);
                    return [];
                }

                var domainName = _configurationSettings?.GetConfigurationDomainNameIntra() ?? string.Empty;
                var readerIdentity = new ScriptGroupReaderIdentity(domainName, credential.UserName);
                var resolution = readerIdentity.Resolve();
                if (!resolution.IsResolved)
                {
                    logger.LogWarning(
                        resolution.Cause,
                        "{Refusal} GitHub artifact drop '{ArtifactPath}' has been restricted to Monitor/SYSTEM/Administrators without a runner grant.",
                        resolution.Refusal,
                        localPath);
                    return [];
                }

                return [resolution.SecurityIdentifier!];
            }
            finally
            {
                credential?.Dispose();
            }
        }

        private void RequirePermittedRemoteDropFolder(string dropFolder)
        {
            if (!SourceHost.IsRemoteOrUnc(dropFolder))
            {
                return;
            }

            if (!SourceHost.TryExtractRemoteHost(dropFolder, out _))
            {
                throw new InvalidOperationException(
                    $"Refusing to publish remote DropFolder '{dropFolder}' to runner: remote or UNC authority could not be verified.");
            }

            if (_sourceHosts.IsArtefactSourceUnconfigured)
            {
                throw new InvalidOperationException(
                    $"Refusing to publish remote DropFolder '{dropFolder}' to runner because no artefact host allow-list is configured ('{SourceHostAllowList.ArtefactHostsSetting}').");
            }

            var decision = _sourceHosts.CheckArtefactSource(dropFolder);
            if (decision == null || !decision.Allowed)
            {
                throw new InvalidOperationException(
                    $"Refusing to publish remote DropFolder '{dropFolder}' to runner, because {decision?.Reason ?? "host is not permitted"}");
            }
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
    }
}
