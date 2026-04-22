using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core;
using Dorc.Core.Events;
using Dorc.Core.Interfaces;
using Dorc.Core.VariableResolution;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
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
            IDeploymentEventsPublisher eventPublisher)
        {
            _loggerFactory = loggerFactory;
            _propertyEvaluator = propertyEvaluator;
            _configValuesPersistentSource = configValuesPersistentSource;
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

                try
                {
                    var scriptRoot = _configValuesPersistentSource.GetConfigValue("ScriptRoot");
                    SetUpScriptRootAsProperty(scriptRoot);

                    if (string.IsNullOrEmpty(requestToExecute.Request.RequestDetails))
                    {
                        throw new InvalidOperationException("Deployment request details are empty.");
                    }

                    ExecuteDeployment(requestToExecute, scriptRoot, cancellationToken);
                }
                catch (Exception e)
                {
                    HandleStartupFailure(requestToExecute, e);
                }
            }
        }

        private void ExecuteDeployment(RequestToProcessDto requestToExecute, string scriptRoot, CancellationToken cancellationToken)
        {
            try
            {
                logger.LogDebug($"Request details:\r\n{requestToExecute.Request.RequestDetails}");

                var requestDetail = requestToExecute.Details;
                var environmentName = requestDetail.EnvironmentName;
                var environment = SetUpAllProperties(requestToExecute, requestDetail, environmentName);

                InitializeDeploymentRequest(requestToExecute.Request);

                var orderedNonSkippedComponents = GetOrderedNonSkippedComponents(requestDetail);
                logger.LogInformation($"Found {orderedNonSkippedComponents.Count} non-skipped components for request {requestToExecute.Request.Id}:");

                if (!orderedNonSkippedComponents.Any())
                {
                    CompleteRequest(requestToExecute, DeploymentRequestStatus.Completed,
                        $"No non-skipped components are found for the request with id '{requestToExecute.Request.Id}'.");
                    return;
                }

                var deploymentResults = EnsureDeploymentResults(requestToExecute.Request.Id, orderedNonSkippedComponents);

                var orderedEnabledNonSkippedComponents = orderedNonSkippedComponents
                    .Where(component => component.IsEnabled);

                if (!orderedEnabledNonSkippedComponents.Any())
                {
                    CompleteRequest(requestToExecute, DeploymentRequestStatus.Completed,
                        $"No enabled non-skipped components are found for the request with id '{requestToExecute.Request.Id}'.");
                    return;
                }

                var commonProperties = GetCommonProperties(environment.EnvironmentIsProd);

                var deploymentRequestStatus = DeployComponents(
                    orderedEnabledNonSkippedComponents, deploymentResults, requestToExecute,
                    environment, environmentName, scriptRoot, commonProperties, cancellationToken);

                FinalizeRequest(requestToExecute, deploymentRequestStatus);
            }
            catch (Exception ex)
            {
                HandleDeploymentFailure(requestToExecute, ex);
            }
        }

        private EnvironmentApiModel SetUpAllProperties(RequestToProcessDto requestToExecute,
            DeploymentRequestDetail requestDetail, string environmentName)
        {
            SetUpEnvironmentNameAsProperty(environmentName);

            var environment = environmentsPersistentSource.GetEnvironment(environmentName);

            if (environment.EnvironmentSecure)
            {
                logger.LogInformation($"Environment '{environmentName}' is secure; not using default property values.");
            }

            SetUpDropFolderAsProperty(requestDetail.BuildDetail.DropLocation);
            SetUpDeploymentLogDirAsProperty();
            SetUpBuildNumberAsProperty(requestToExecute.Request.BuildNumber);
            SetUpRefDataApiUrlAsProperty();
            SetUpConfigValuesAsProperties(environment);
            SetUpEnvironmentAsProperty(environment);
            SetUpEnvOwnerEmailAsProperty(requestToExecute.Request);
            SetUpRequestDetailsPropertiesAsProperties(requestDetail.Properties);

            return environment;
        }

        private Dictionary<int, DeploymentResultApiModel> EnsureDeploymentResults(int requestId,
            IList<ComponentApiModel> orderedNonSkippedComponents)
        {
            var deploymentResults = requestsPersistentSource
                .GetDeploymentResultsForRequest(requestId)
                .ToDictionary(r => r.ComponentId);

            foreach (var nonSkippedComponent in orderedNonSkippedComponents)
            {
                try
                {
                    var componentId = nonSkippedComponent.ComponentId!.Value;

                    if (!deploymentResults.ContainsKey(componentId))
                    {
                        var deploymentResult = requestsPersistentSource.CreateDeploymentResult(componentId, requestId);
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

            return deploymentResults;
        }

        private DeploymentRequestStatus DeployComponents(
            IEnumerable<ComponentApiModel> components,
            Dictionary<int, DeploymentResultApiModel> deploymentResults,
            RequestToProcessDto requestToExecute,
            EnvironmentApiModel environment,
            string environmentName,
            string scriptRoot,
            IDictionary<string, VariableValue> commonProperties,
            CancellationToken cancellationToken)
        {
            var deploymentRequestStatus = DeploymentRequestStatus.Completed;

            foreach (var component in components)
            {
                var result = DeploySingleComponent(component, deploymentResults, requestToExecute,
                    environment, environmentName, scriptRoot, commonProperties, cancellationToken);

                if (result == null)
                    break;

                deploymentRequestStatus = result.Value;
                if (ShouldAbortDeployment(result.Value, component))
                    break;
            }

            return deploymentRequestStatus;
        }

        private DeploymentRequestStatus? DeploySingleComponent(
            ComponentApiModel component,
            Dictionary<int, DeploymentResultApiModel> deploymentResults,
            RequestToProcessDto requestToExecute,
            EnvironmentApiModel environment,
            string environmentName,
            string scriptRoot,
            IDictionary<string, VariableValue> commonProperties,
            CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var componentId = component.ComponentId!.Value;
                var deploymentResult = deploymentResults[componentId];

                bool isSuccessful = componentProcessor.DeployComponent(
                    component, deploymentResult, requestToExecute.Request.Id,
                    requestToExecute.Request.IsProd, environment.EnvironmentId,
                    environment.EnvironmentIsProd, environmentName, scriptRoot,
                    commonProperties, cancellationToken);

                if (!isSuccessful)
                    return DeploymentRequestStatus.Failed;

                if (deploymentResult.Status == DeploymentResultStatus.WaitingConfirmation.ToString())
                    return DeploymentRequestStatus.WaitingConfirmation;

                return DeploymentRequestStatus.Completed;
            }
            catch (OperationCanceledException)
            {
                var currentDbRequestStatus = requestsPersistentSource.GetRequest(requestToExecute.Request.Id).Status;
                var status = currentDbRequestStatus != DeploymentRequestStatus.Pending.ToString()
                    ? DeploymentRequestStatus.Cancelled
                    : DeploymentRequestStatus.Pending;

                logger.LogInformation("Deployment of remaining components is cancelled.");
                return null; // signals break
            }
            catch (Exception exception)
            {
                LogExceptionChain(exception);

                if (component.StopOnFailure)
                {
                    logger.LogError("Deployment of remaining components is aborted.");
                    return null; // signals break
                }

                return DeploymentRequestStatus.Failed;
            }
        }

        private bool ShouldAbortDeployment(DeploymentRequestStatus status, ComponentApiModel component)
        {
            if (status != DeploymentRequestStatus.Failed)
                return false;

            if (!component.StopOnFailure)
                return false;

            logger.LogWarning(
                "Deployment of remaining components is aborted due to StopOnFailure flag on component '{ComponentName}'.",
                component.ComponentName);
            return true;
        }

        private void FinalizeRequest(RequestToProcessDto requestToExecute, DeploymentRequestStatus deploymentRequestStatus)
        {
            if (deploymentRequestStatus != DeploymentRequestStatus.Completed &&
                deploymentRequestStatus != DeploymentRequestStatus.WaitingConfirmation)
            {
                CancelPendingDeploymentResults(requestToExecute.Request.Id, deploymentRequestStatus);
            }

            requestsPersistentSource.SetRequestCompletionStatus(
                requestToExecute.Request.Id, deploymentRequestStatus, DateTimeOffset.Now);

            eventsPublisher.PublishRequestStatusChangedAsync(new DeploymentRequestEventData(requestToExecute.Request)
            {
                Status = deploymentRequestStatus.ToString(),
                CompletedTime = DateTimeOffset.Now,
            });
        }

        private void CompleteRequest(RequestToProcessDto requestToExecute, DeploymentRequestStatus status, string warningMessage)
        {
            logger.LogWarning(warningMessage);

            requestsPersistentSource.SetRequestCompletionStatus(
                requestToExecute.Request.Id, status, DateTimeOffset.Now);

            eventsPublisher.PublishRequestStatusChangedAsync(new DeploymentRequestEventData(requestToExecute.Request)
            {
                Status = status.ToString(),
                CompletedTime = DateTimeOffset.Now,
            });
        }

        private void HandleDeploymentFailure(RequestToProcessDto requestToExecute, Exception ex)
        {
            var criticalLog = BuildCriticalLog($"Deployment execution failure. Exception: {ex}", ex);

            CancelPendingDeploymentResults(requestToExecute.Request.Id, DeploymentRequestStatus.Errored);

            requestsPersistentSource.SetRequestCompletionStatus(
                requestToExecute.Request.Id, DeploymentRequestStatus.Errored,
                DateTimeOffset.Now, criticalLog);

            eventsPublisher.PublishRequestStatusChangedAsync(new DeploymentRequestEventData(requestToExecute.Request)
            {
                Status = DeploymentRequestStatus.Errored.ToString(),
                CompletedTime = DateTimeOffset.Now,
            });
        }

        private void HandleStartupFailure(RequestToProcessDto requestToExecute, Exception e)
        {
            var criticalLog = $"Failed while starting runner: {e}";
            logger.LogError(criticalLog);

            CancelPendingDeploymentResults(requestToExecute.Request.Id, DeploymentRequestStatus.Errored);

            requestsPersistentSource.UpdateRequestStatus(
                requestToExecute.Request.Id, DeploymentRequestStatus.Errored,
                DateTimeOffset.Now, criticalLog);

            eventsPublisher.PublishRequestStatusChangedAsync(new DeploymentRequestEventData(requestToExecute.Request)
            {
                Status = DeploymentRequestStatus.Errored.ToString(),
                CompletedTime = DateTimeOffset.Now,
            });
        }

        private string BuildCriticalLog(string message, Exception ex)
        {
            var criticalLogBuilder = new StringBuilder();
            logger.LogError(message);
            criticalLogBuilder.AppendLine(message);

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

            return criticalLogBuilder.ToString();
        }

        private void LogExceptionChain(Exception exception)
        {
            logger.LogError($"Component deployment failed. Exception: {exception}");

            while (exception.InnerException != null)
            {
                logger.LogError(exception.InnerException.ToString());
                exception = exception.InnerException;
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
                    });
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
            });
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
                _variableResolver.SetPropertyValue(propertyPair.Name, propertyPair.Value);
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

        private void SetUpDropFolderAsProperty(string dropFolder)
        {
            if (dropFolder.StartsWith("file"))
            {
                dropFolder = new Uri(dropFolder).LocalPath;
            }
            _variableResolver.SetPropertyValue(PropertyValueScopeOptionsFixed.DropFolder, dropFolder);
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
    }
}
