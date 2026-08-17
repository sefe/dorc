using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core;
using Dorc.Core.Events;
using Dorc.Core.Interfaces;
using Dorc.Core.VariableResolution;
using Dorc.Core.Security;
using Dorc.PersistentData.Security;
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
        private readonly IGitHubArtifactDownloader _gitHubArtifactDownloader;
        private readonly IScriptScopeConfigValues _scriptScopeConfigValues;
        private readonly IDeploymentCredentialSource _credentialSource;

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
            IGitHubArtifactDownloader gitHubArtifactDownloader,
            IScriptScopeConfigValues scriptScopeConfigValues,
            IDeploymentCredentialSource credentialSource)
        {
            _loggerFactory = loggerFactory;
            _propertyEvaluator = propertyEvaluator;
            _configValuesPersistentSource = configValuesPersistentSource;
            _gitHubArtifactDownloader = gitHubArtifactDownloader;
            _scriptScopeConfigValues = scriptScopeConfigValues;
            _credentialSource = credentialSource;
            this.logger = _loggerFactory.CreateLogger<PendingRequestProcessor>();

            this.componentProcessor = componentProcessor;
            this._variableScopeOptionsResolver = variableScopeOptionsResolver;
            this.requestsPersistentSource = requestsPersistentSource;
            this.propertyValuesPersistentSource = propertyValuesPersistentSource;
            this.environmentsPersistentSource = environmentsPersistentSource;
            this.manageProjectsPersistentSource = manageProjectsPersistentSource;
            this.eventsPublisher = eventPublisher;
        }

        public void Execute(RequestToProcessDto requestToExecute, CancellationToken cancellationToken)
        {
            using (logger.BeginScope(new Dictionary<string, object> { ["RequestId"] = requestToExecute.Request.Id }))
            {

                logger.LogInformation($"Attempting to deploy the request with id '{requestToExecute.Request.Id}'.");

                _variableResolver = new VariableResolver(propertyValuesPersistentSource, _loggerFactory, _propertyEvaluator);

                string? resolvedDropFolder = null;
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
                        logger.LogDebug($"Request details:\r\n{requestToExecute.Request.RequestDetails}");

                        var requestDetail = requestToExecute.Details;

                        var environmentName = requestDetail.EnvironmentName;

                        SetUpEnvironmentNameAsProperty(environmentName);

                        var environment = environmentsPersistentSource.GetEnvironment(environmentName);

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

                        InitializeDeploymentRequest(
                            requestToExecute.Request);

                        var deploymentRequestStatus = DeploymentRequestStatus.Completed;

                        var orderedNonSkippedComponents = GetOrderedNonSkippedComponents(
                            requestDetail);

                        logger.LogInformation($"Found {orderedNonSkippedComponents.Count} non-skipped components for request {requestToExecute.Request.Id}:");

                        if (!orderedNonSkippedComponents.Any())
                        {
                            logger.LogWarning($"No non-skipped components are found for the request with id '{requestToExecute.Request.Id}'.");

                            requestsPersistentSource.SetRequestCompletionStatus(
                                requestToExecute.Request.Id,
                                deploymentRequestStatus,
                                DateTimeOffset.Now);

                            eventsPublisher.PublishRequestStatusChangedAsync(new DeploymentRequestEventData(requestToExecute.Request)
                            {
                                Status = deploymentRequestStatus.ToString(),
                                CompletedTime = DateTimeOffset.Now,
                            }).ContinueWith(t => logger.LogWarning(t.Exception!.InnerException,
                                "fire-and-forget publish failed for requestId={RequestId}", requestToExecute.Request.Id),
                                TaskContinuationOptions.OnlyOnFaulted);

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
                                else if (!deploymentResults[componentId].Status.Equals(DeploymentResultStatus.Confirmed.ToString()))
                                {
                                    logger.LogWarning($"Cannot create deployment result since duplicate component with id '{componentId}' is detected.");
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

                            requestsPersistentSource.SetRequestCompletionStatus(
                                requestToExecute.Request.Id,
                                deploymentRequestStatus,
                                DateTimeOffset.Now);

                            eventsPublisher.PublishRequestStatusChangedAsync(new DeploymentRequestEventData(requestToExecute.Request)
                            {
                                Status = deploymentRequestStatus.ToString(),
                                CompletedTime = DateTimeOffset.Now,
                            }).ContinueWith(t => logger.LogWarning(t.Exception!.InnerException,
                                "fire-and-forget publish failed for requestId={RequestId}", requestToExecute.Request.Id),
                                TaskContinuationOptions.OnlyOnFaulted);

                            return;
                        }

                        var commonProperties = GetCommonProperties(
                            environment.EnvironmentIsProd);

                        foreach (var enabledNonSkippedComponent in orderedEnabledNonSkippedComponents)
                        {
                            try
                            {
                                cancellationToken.ThrowIfCancellationRequested();

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
                                    scriptRoot,
                                    commonProperties,
                                    cancellationToken);

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
                                if (deploymentResult.Status == DeploymentResultStatus.WaitingConfirmation.ToString())
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

                        requestsPersistentSource.SetRequestCompletionStatus(
                            requestToExecute.Request.Id,
                            deploymentRequestStatus,
                            DateTimeOffset.Now);

                        eventsPublisher.PublishRequestStatusChangedAsync(new DeploymentRequestEventData(requestToExecute.Request)
                        {
                            Status = deploymentRequestStatus.ToString(),
                            CompletedTime = DateTimeOffset.Now,
                        }).ContinueWith(t => logger.LogWarning(t.Exception!.InnerException,
                            "fire-and-forget publish failed for requestId={RequestId}", requestToExecute.Request.Id),
                            TaskContinuationOptions.OnlyOnFaulted);
                    }
                    catch (Exception ex)
                    {
                        var criticalLogBuilder = new StringBuilder();

                        logger.LogError($"Deployment execution failure. Exception: {ex}");
                        criticalLogBuilder.AppendLine($"Deployment execution failure. Exception: {ex}");

                        var log = new StringBuilder();
                        while (ex != null)
                        {
                            log.AppendLine(ex.GetType().ToString());
                            log.AppendLine(ex.Message);
                            log.AppendLine(ex.StackTrace);
                            ex = ex.InnerException;
                        }
                        logger.LogError(log.ToString());
                        criticalLogBuilder.AppendLine(log.ToString());

                        CancelPendingDeploymentResults(requestToExecute.Request.Id, DeploymentRequestStatus.Errored);

                        ReportExecutionIdentityAdoption();

                        requestsPersistentSource.SetRequestCompletionStatus(
                            requestToExecute.Request.Id,
                            DeploymentRequestStatus.Errored,
                            DateTimeOffset.Now,
                            criticalLogBuilder.ToString());

                        eventsPublisher.PublishRequestStatusChangedAsync(new DeploymentRequestEventData(requestToExecute.Request)
                        {
                            Status = DeploymentRequestStatus.Errored.ToString(),
                            CompletedTime = DateTimeOffset.Now,
                        }).ContinueWith(t => logger.LogWarning(t.Exception!.InnerException,
                            "fire-and-forget publish failed for requestId={RequestId}", requestToExecute.Request.Id),
                            TaskContinuationOptions.OnlyOnFaulted);
                    }
                }
                catch (Exception e)
                {
                    var criticalLogBuilder = new StringBuilder();

                    logger.LogError($"Failed while starting runner: {e}");
                    criticalLogBuilder.AppendLine($"Failed while starting runner: {e}");

                    CancelPendingDeploymentResults(requestToExecute.Request.Id, DeploymentRequestStatus.Errored);

                    requestsPersistentSource.UpdateRequestStatus(
                        requestToExecute.Request.Id,
                        DeploymentRequestStatus.Errored,
                        DateTimeOffset.Now,
                        criticalLogBuilder.ToString());

                    eventsPublisher.PublishRequestStatusChangedAsync(new DeploymentRequestEventData(requestToExecute.Request)
                    {
                        Status = DeploymentRequestStatus.Errored.ToString(),
                        CompletedTime = DateTimeOffset.Now,
                    }).ContinueWith(t => logger.LogWarning(t.Exception!.InnerException,
                        "fire-and-forget publish failed for requestId={RequestId}", requestToExecute.Request.Id),
                        TaskContinuationOptions.OnlyOnFaulted);

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

        private void CancelPendingDeploymentResults(int requestId, DeploymentRequestStatus requestStatus)
        {
            try
            {
                var pendingResults = requestsPersistentSource
                    .GetDeploymentResultsForRequest(requestId)
                    .Where(r => r.Status == DeploymentResultStatus.Pending.ToString())
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

        private void InitializeDeploymentRequest(DeploymentRequestApiModel request)
        {
            logger.LogInformation("Setting Request to Running state, Id: " + request.Id);

            requestsPersistentSource.SetRequestStartStatus(
                request,
                DeploymentRequestStatus.Running,
                DateTimeOffset.Now);

            eventsPublisher.PublishRequestStatusChangedAsync(new DeploymentRequestEventData(request)
            {
                Status = DeploymentRequestStatus.Running.ToString(),
                StartedTime = DateTimeOffset.Now,
            }).ContinueWith(t => logger.LogWarning(t.Exception!.InnerException,
                "fire-and-forget publish failed for requestId={RequestId}", request.Id),
                TaskContinuationOptions.OnlyOnFaulted);
        }

        private IList<ComponentApiModel> GetOrderedNonSkippedComponents(
            DeploymentRequestDetail requestDetails)
        {
            logger.LogDebug("Getting ordered non-skipped components.");

            var orderedComponents =
                manageProjectsPersistentSource.GetOrderedComponents(requestDetails.Components);

            var orderedNonSkippedComponents = new List<ComponentApiModel>();

            foreach (var component in orderedComponents)
            {
                if (requestDetails.ComponentsToSkip == null
                    || !requestDetails.ComponentsToSkip.Contains(component.ComponentName))
                {
                    orderedNonSkippedComponents.Add(component);

                }
                else
                {
                    logger.LogInformation($"Skipping component '{component.ComponentName}'.");
                }
            }

            return orderedNonSkippedComponents;
        }

        private IDictionary<string, VariableValue> GetCommonProperties(bool environmentIsProd)
        {
            var commonProperties = _variableResolver.LoadProperties();

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
        private void ReportExecutionIdentityAdoption()
        {
            var (bound, fallback) = _credentialSource.ResolutionCounts;

            if (fallback > 0)
            {
                logger.LogInformation(
                    "Execution identity: {Bound} resolution(s) used an environment's own identity,"
                    + " {Fallback} fell back to the shared tier default. Every fallback shares one"
                    + " account across its whole tier.",
                    bound,
                    fallback);
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
                logger.LogInformation("Set EnvOwnerEmails property to '{EnvOwnerEmails}' for request {RequestId}",
                    envOwnerEmail, request.Id);
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
