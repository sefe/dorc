﻿using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core;
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
        private readonly IConfigValuesPersistentSource _configValuesPersistentSource;
        private readonly IPropertyEvaluator _propertyEvaluator;

        public PendingRequestProcessor(
            ILog logger,
            IComponentProcessor componentProcessor,
            IVariableScopeOptionsResolver variableScopeOptionsResolver,
            IRequestsPersistentSource requestsPersistentSource,
            IPropertyValuesPersistentSource propertyValuesPersistentSource,
            IEnvironmentsPersistentSource environmentsPersistentSource,
            IManageProjectsPersistentSource manageProjectsPersistentSource,
            IConfigValuesPersistentSource configValuesPersistentSource, IPropertyEvaluator propertyEvaluator)
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
        }

        public async Task ExecuteAsync(RequestToProcessDto requestToExecute, CancellationToken cancellationToken)
        {
            logger.Info($"Attempting to deploy the request with id '{requestToExecute.Request.Id}'.");

            TriggerEvent(DeploymentEvent.Running, requestToExecute.Request);

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

                    SetUpConfigValuesAsProperties();

                    SetUpEnvironmentAsProperty(environment);

                    SetUpRequestDetailsPropertiesAsProperties(requestDetail.Properties);

                    InitializeDeploymentRequest(
                        requestToExecute.Request);

                    var deploymentRequestStatus = DeploymentRequestStatus.Completed;

                    var orderedNonSkippedComponents = GetOrderedNonSkippedComponents(
                        requestDetail);

                    logger.Info($"Found {orderedNonSkippedComponents.Count} non-skipped components for request {requestToExecute.Request.Id}:");
                    foreach (var comp in orderedNonSkippedComponents)
                    {
                        logger.Info($"  - Component: '{comp.ComponentName}', Type: {comp.ComponentType} (Enum Value: {(int)comp.ComponentType}), ID: {comp.ComponentId}");
                    }

                    if (!orderedNonSkippedComponents.Any())
                    {
                        logger.Warn($"No non-skipped components are found for the request with id '{requestToExecute.Request.Id}'.");

                        requestsPersistentSource.SetRequestCompletionStatus(
                            requestToExecute.Request.Id,
                            deploymentRequestStatus,
                            DateTimeOffset.Now);

                        return;
                    }

                    var deploymentResults = new Dictionary<int, DeploymentResultApiModel>();
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
                            else
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

                            bool isSuccessful = await componentProcessor.DeployComponentAsync(
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

                return;
            }

            TriggerEvent(DeploymentEvent.Completed, requestToExecute.Request);
        }

        private void InitializeDeploymentRequest(DeploymentRequestApiModel request)
        {
            logger.Info("Setting Request to Running state, Id: " + request.Id);

            requestsPersistentSource.SetRequestStartStatus(
                request,
                DeploymentRequestStatus.Running,
                DateTimeOffset.Now);
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

        private void SetUpConfigValuesAsProperties()
        {
            // attempt to add any other properties that don't need defaults
            foreach (var configValue in _configValuesPersistentSource.GetAllConfigValues(true))
            {
                if (_variableResolver.GetPropertyValue(configValue.Key) == null)
                {
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

        private void Continuation(Task task, RequestToProcessDto requestExecute)
        {
            if (task.Exception == null) return;
            task.Exception.Handle(
                ex =>
                {
                    logger.Error("Deployment runner", ex);
                    if (requestExecute.Request.Status == DeploymentRequestStatus.Cancelling.ToString()
                        || requestExecute.Request.Status == DeploymentRequestStatus.Cancelled.ToString())
                    {
                        requestsPersistentSource.UpdateRequestStatus(
                            requestExecute.Request.Id,
                            DeploymentRequestStatus.Cancelled, DateTimeOffset.Now);
                        foreach (var dr in requestsPersistentSource.GetDeploymentResultsForRequest(requestExecute.Request.Id))
                        {
                            if (dr.Status == DeploymentResultStatus.Complete.ToString())
                                continue;

                            requestsPersistentSource.UpdateRequestStatus(
                                requestExecute.Request.Id,
                                DeploymentRequestStatus.Cancelled, DateTimeOffset.Now);
                        }
                    }
                    else
                    {
                        requestsPersistentSource.UpdateRequestStatus(
                            requestExecute.Request.Id,
                            DeploymentRequestStatus.Errored, DateTimeOffset.Now);
                    }

                    return true;
                });
            TriggerEvent(DeploymentEvent.Completed, requestExecute.Request);
        }

        private void TriggerEvent(DeploymentEvent deploymentEvent, DeploymentRequestApiModel request)
        {
        }
    }
}
