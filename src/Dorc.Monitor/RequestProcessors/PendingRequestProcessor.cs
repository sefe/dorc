using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core;
using Dorc.Core.Events;
using Dorc.Core.Interfaces;
using Dorc.Core.VariableResolution;
using Dorc.PersistentData.Sources.Interfaces;
using log4net;
using Microsoft.Extensions.Configuration;
using System.Text;

namespace Dorc.Monitor.RequestProcessors
{
    internal class PendingRequestProcessor : IPendingRequestProcessor
    {
        private readonly ILog logger;
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
        private readonly IJobNotificationService _notificationService;

        public PendingRequestProcessor(
            ILog logger,
            IComponentProcessor componentProcessor,
            IVariableScopeOptionsResolver variableScopeOptionsResolver,
            IRequestsPersistentSource requestsPersistentSource,
            IPropertyValuesPersistentSource propertyValuesPersistentSource,
            IEnvironmentsPersistentSource environmentsPersistentSource,
            IManageProjectsPersistentSource manageProjectsPersistentSource,
            IConfigValuesPersistentSource configValuesPersistentSource, 
            IPropertyEvaluator propertyEvaluator,
            IDeploymentEventsPublisher eventPublisher,
            IJobNotificationService notificationService)
        {
            _propertyEvaluator = propertyEvaluator;
            _configValuesPersistentSource = configValuesPersistentSource;
            this.logger = logger;

            this.componentProcessor = componentProcessor;
            this._variableScopeOptionsResolver = variableScopeOptionsResolver;
            this.requestsPersistentSource = requestsPersistentSource;
            this.propertyValuesPersistentSource = propertyValuesPersistentSource;
            this.environmentsPersistentSource = environmentsPersistentSource;
            this.manageProjectsPersistentSource = manageProjectsPersistentSource;
            this.eventsPublisher = eventPublisher;
            this._notificationService = notificationService;
        }

        public void Execute(RequestToProcessDto requestToExecute, CancellationToken cancellationToken)
        {
            logger.Info($"Attempting to deploy the request with id '{requestToExecute.Request.Id}'.");

            _variableResolver = new VariableResolver(propertyValuesPersistentSource, logger, _propertyEvaluator);

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
                    logger.Debug($"Request details:\r\n{requestToExecute.Request.RequestDetails}");

                    var requestDetail = requestToExecute.Details;

                    var environmentName = requestDetail.EnvironmentName;

                    SetUpEnvironmentNameAsProperty(environmentName);

                    var environment = environmentsPersistentSource.GetEnvironment(environmentName);

                    if (environment.EnvironmentSecure)
                    {
                        logger.Info($"Environment '{environmentName}' is secure; not using default property values.");
                    }

                    SetUpDropFolderAsProperty(requestDetail.BuildDetail.DropLocation);

                    SetUpDeploymentLogDirAsProperty();

                    SetUpBuildNumberAsProperty(requestToExecute.Request.BuildNumber);

                    SetUpRefDataApiUrlAsProperty();

                    SetUpConfigValuesAsProperties(environment);

                    SetUpEnvironmentAsProperty(environment);

                    SetUpRequestDetailsPropertiesAsProperties(requestDetail.Properties);

                    InitializeDeploymentRequest(
                        requestToExecute.Request);

                    var deploymentRequestStatus = DeploymentRequestStatus.Completed;

                    var orderedNonSkippedComponents = GetOrderedNonSkippedComponents(
                        requestDetail);

                    logger.Info($"Found {orderedNonSkippedComponents.Count} non-skipped components for request {requestToExecute.Request.Id}:");

                    if (!orderedNonSkippedComponents.Any())
                    {
                        logger.Warn($"No non-skipped components are found for the request with id '{requestToExecute.Request.Id}'.");

                        requestsPersistentSource.SetRequestCompletionStatus(
                            requestToExecute.Request.Id,
                            deploymentRequestStatus,
                            DateTimeOffset.Now);

                        eventsPublisher.PublishRequestStatusChangedAsync(new DeploymentRequestEventData(requestToExecute.Request)
                        {
                            Status = deploymentRequestStatus.ToString(),
                            CompletedTime = DateTimeOffset.Now,
                        });

                        SendJobCompletionNotificationAsync(
                            requestToExecute.Request.UserName,
                            requestToExecute.Request.Id,
                            deploymentRequestStatus.ToString(),
                            environmentName,
                            requestToExecute.Request.Project,
                            requestToExecute.Request.BuildNumber);

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
                                logger.Warn($"Cannot create deployment result since duplicate component with id '{componentId}' is detected.");
                            }
                        }
                        catch (Exception exception)
                        {
                            logger.Error($"Deployment result cannot be created. Exception: {exception}");
                            throw;
                        }
                    }

                    var orderedEnabledNonSkippedComponents = orderedNonSkippedComponents
                        .Where(component => !component.IsEnabled.HasValue
                            || component.IsEnabled == true);

                    if (!orderedEnabledNonSkippedComponents.Any())
                    {
                        logger.Warn($"No enabled non-skipped components are found for the request with id '{requestToExecute.Request.Id}'.");

                        requestsPersistentSource.SetRequestCompletionStatus(
                            requestToExecute.Request.Id,
                            deploymentRequestStatus,
                            DateTimeOffset.Now);

                        eventsPublisher.PublishRequestStatusChangedAsync(new DeploymentRequestEventData(requestToExecute.Request)
                        {
                            Status = deploymentRequestStatus.ToString(),
                            CompletedTime = DateTimeOffset.Now,
                        });

                        SendJobCompletionNotificationAsync(
                            requestToExecute.Request.UserName,
                            requestToExecute.Request.Id,
                            deploymentRequestStatus.ToString(),
                            environmentName,
                            requestToExecute.Request.Project,
                            requestToExecute.Request.BuildNumber);

                        return;
                    }

                    var commonProperties = GetCommonProperties(
                        environment.EnvironmentIsProd);

                    foreach (var enabledNonSkippedComponent in orderedEnabledNonSkippedComponents)
                    {
                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();

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
                                scriptRoot,
                                commonProperties,
                                cancellationToken);

                            if (!isSuccessful)
                            {
                                deploymentRequestStatus = DeploymentRequestStatus.Failed;
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

                            logger.Info("Deployment of remaining components is cancelled.");
                            break;
                        }
                        catch (Exception exception)
                        {
                            deploymentRequestStatus = DeploymentRequestStatus.Failed;

                            logger.Error($"Component deployment failed. Exception: {exception}");

                            while (exception.InnerException != null)
                            {
                                logger.Error(exception.InnerException.ToString());

                                exception = exception.InnerException;
                            }

                            if (enabledNonSkippedComponent.StopOnFailure)
                            {
                                logger.Error("Deployment of remaining components is aborted.");
                                break;
                            }
                        }
                    }

                    requestsPersistentSource.SetRequestCompletionStatus(
                        requestToExecute.Request.Id,
                        deploymentRequestStatus,
                        DateTimeOffset.Now);

                    eventsPublisher.PublishRequestStatusChangedAsync(new DeploymentRequestEventData(requestToExecute.Request)
                    {
                        Status = deploymentRequestStatus.ToString(),
                        CompletedTime = DateTimeOffset.Now,
                    });

                    SendJobCompletionNotificationAsync(
                        requestToExecute.Request.UserName,
                        requestToExecute.Request.Id,
                        deploymentRequestStatus.ToString(),
                        requestDetail.EnvironmentName,
                        requestToExecute.Request.Project,
                        requestToExecute.Request.BuildNumber);
                }
                catch (Exception ex)
                {
                    var criticalLogBuilder = new StringBuilder();

                    logger.Error($"Deployment execution failure. Exception: {ex}");
                    criticalLogBuilder.AppendLine($"Deployment execution failure. Exception: {ex}");

                    var log = new StringBuilder();
                    while (ex != null)
                    {
                        log.AppendLine(ex.GetType().ToString());
                        log.AppendLine(ex.Message);
                        log.AppendLine(ex.StackTrace);
                        ex = ex.InnerException;
                    }
                    logger.Error(log.ToString());
                    criticalLogBuilder.AppendLine(log.ToString());
                    requestsPersistentSource.SetRequestCompletionStatus(
                        requestToExecute.Request.Id,
                        DeploymentRequestStatus.Errored,
                        DateTimeOffset.Now,
                        criticalLogBuilder.ToString());

                    eventsPublisher.PublishRequestStatusChangedAsync(new DeploymentRequestEventData(requestToExecute.Request)
                    {
                        Status = DeploymentRequestStatus.Errored.ToString(),
                        CompletedTime = DateTimeOffset.Now,
                    });

                    SendJobCompletionNotificationAsync(
                        requestToExecute.Request.UserName,
                        requestToExecute.Request.Id,
                        DeploymentRequestStatus.Errored.ToString(),
                        requestToExecute.Request.EnvironmentName,
                        requestToExecute.Request.Project,
                        requestToExecute.Request.BuildNumber);
                }
            }
            catch (Exception e)
            {
                var criticalLogBuilder = new StringBuilder();

                logger.Error($"Failed while starting runner: {e}");
                criticalLogBuilder.AppendLine($"Failed while starting runner: {e}");

                requestsPersistentSource.UpdateRequestStatus(
                    requestToExecute.Request.Id,
                    DeploymentRequestStatus.Errored,
                    DateTimeOffset.Now,
                    criticalLogBuilder.ToString());

                eventsPublisher.PublishRequestStatusChangedAsync(new DeploymentRequestEventData(requestToExecute.Request)
                {
                    Status = DeploymentRequestStatus.Errored.ToString(),
                    CompletedTime = DateTimeOffset.Now,
                });

                SendJobCompletionNotificationAsync(
                    requestToExecute.Request.UserName,
                    requestToExecute.Request.Id,
                    DeploymentRequestStatus.Errored.ToString(),
                    requestToExecute.Request.EnvironmentName,
                    requestToExecute.Request.Project,
                    requestToExecute.Request.BuildNumber);

                return;
            }
        }

        private void InitializeDeploymentRequest(DeploymentRequestApiModel request)
        {
            logger.Info("Setting Request to Running state, Id: " + request.Id);

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
            logger.Debug("Getting ordered non-skipped components.");

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
                    logger.Info($"Skipping component '{component.ComponentName}'.");
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

        private void SendJobCompletionNotificationAsync(
            string userName,
            int requestId,
            string status,
            string environment,
            string project,
            string buildNumber)
        {
            Task.Run(async () =>
            {
                try
                {
                    await _notificationService.NotifyJobCompletionAsync(
                        userName,
                        requestId,
                        status,
                        environment,
                        project,
                        buildNumber);
                }
                catch (Exception ex)
                {
                    logger.Error($"Failed to send job completion notification for request '{requestId}': {ex}");
                }
            });
        }
    }
}
