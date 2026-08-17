using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core;
using Dorc.Core.AzureStorageAccount;
using Dorc.Core.BuildServer;
using Dorc.Core.Configuration;
using Dorc.Core.Security;
using Dorc.Monitor.Pipes;
using Dorc.Monitor.RunnerProcess;
using Dorc.Monitor.RunnerProcess.Interop.Windows.Kernel32;
using Dorc.Monitor.TerraformSourceConfig;
using Dorc.PersistentData.Security;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Dorc.Kafka.Events.Publisher;

namespace Dorc.Monitor
{
    public class TerraformDispatcher : ITerraformDispatcher
    {
        private readonly ILogger logger;
        private readonly IRequestsPersistentSource _requestsPersistentSource;
        private readonly IConfigValuesPersistentSource _configValuesPersistentSource;
        private readonly IConfigurationSettings _configurationSettingsEngine;
        private readonly IDeploymentRequestProcessesPersistentSource _processesPersistentSource;
        private readonly IScriptGroupPipeServer _scriptGroupPipeServer;
        private readonly IAzureStorageAccountWorker _azureStorageAccountWorker;
        private readonly IProjectsPersistentSource _projectsPersistentSource;
        private readonly TerraformSourceConfigurator _sourceConfigurator;
        private readonly IScriptScopeConfigValues _scriptScopeConfigValues;
        private readonly IDeploymentCredentialSource _credentialSource;

        private bool isScriptExecutionSuccessful; // This field is needed to be instance-wide since Runner process errors are processed as instance-wide events.

        public TerraformDispatcher(
            ILogger<TerraformDispatcher> logger,
            IRequestsPersistentSource requestsPersistentSource,
            IConfigValuesPersistentSource configValuesPersistentSource,
            IConfigurationSettings configurationSettingsEngine,
            IDeploymentRequestProcessesPersistentSource processesPersistentSource,
            IScriptGroupPipeServer scriptGroupPipeServer,
            IAzureStorageAccountWorker azureStorageAccountWorker,
            IProjectsPersistentSource projectsPersistentSource,
            IGitHubHostValidator gitHubHostValidator,
            IScriptScopeConfigValues scriptScopeConfigValues,
            IDeploymentCredentialSource credentialSource)
        {
            this.logger = logger;
            this._requestsPersistentSource = requestsPersistentSource;
            this._configValuesPersistentSource = configValuesPersistentSource;
            this._configurationSettingsEngine = configurationSettingsEngine;
            this._processesPersistentSource = processesPersistentSource;
            this._scriptGroupPipeServer = scriptGroupPipeServer;
            this._azureStorageAccountWorker = azureStorageAccountWorker;
            this._projectsPersistentSource = projectsPersistentSource;
            this._scriptScopeConfigValues = scriptScopeConfigValues;
            this._credentialSource = credentialSource;
            this._sourceConfigurator = new TerraformSourceConfigurator(logger, _configurationSettingsEngine, gitHubHostValidator);
        }

        public bool Dispatch(
            ComponentApiModel component,
            DeploymentResultApiModel deploymentResult,
            IDictionary<string, VariableValue> properties,
            int requestId,
            bool isProduction,
            string environmentName,
            string? executionIdentityReference,
            RequestExecutionIdentityAdoption identityAdoption,
            StringBuilder resultLogBuilder,
            TerraformRunnerOperations terreformOperation,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            logger.LogInformation($"TerraformDispatcher.DispatchAsync called for component '{component.ComponentName}' with id '{component.ComponentId}', deployment result id '{deploymentResult.Id}', environment '{environmentName}'.");

            // Update status to Running
            _requestsPersistentSource.UpdateResultStatus(
                deploymentResult,
                DeploymentResultStatus.Running);

            logger.LogInformation($"Updated deployment result {deploymentResult.Id} status to Running.");

            isScriptExecutionSuccessful = true;

            // See ScriptDispatcher: one resolution point rather than a copy per dispatch path.
            // Environment-keyed. An environment naming its own execution identity deploys under
            // it; one that does not falls back to the tier default and behaves exactly as before,
            // so migration proceeds environment by environment rather than as a flag day.
            var credential = identityAdoption.Resolve(
                _credentialSource,
                isProduction ? DeploymentTier.Production : DeploymentTier.NonProduction,
                executionIdentityReference);

            if (credential == null)
            {
                logger.LogError(
                    "No deployment credential is available for environment '{EnvironmentName}'."
                    + " Credentials are read from {Source}.",
                    environmentName,
                    _credentialSource.Description);

                isScriptExecutionSuccessful = false;
                return isScriptExecutionSuccessful;
            }

            var processAccountName = credential.UserName;
            var processAccountPassword = credential.Password;

            // Get request and project information for Terraform source configuration
            var request = _requestsPersistentSource.GetRequest(requestId);
            ProjectApiModel? project = null;
            if (!string.IsNullOrEmpty(request?.Project))
            {
                try
                {
                    project = _projectsPersistentSource.GetProject(request.Project);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, $"Could not retrieve project '{request.Project}' for request {requestId}");
                }
            }

            var scriptGroup = GetScriptGroup(
                component.ScriptPath,
                properties,
                deploymentResult.Id,
                component,
                request,
                project);

            var domainName = _configurationSettingsEngine.GetConfigurationDomainNameIntra();
            var contextBuilder = new ProcessSecurityContextBuilder(logger)
            {
                UserName = processAccountName,
                Domain = domainName,
                Password = processAccountPassword
            };

            using (var pipeCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            using (var securityContext = contextBuilder.Build())
            {
                var startedScriptGroupPipeName = $"DOrcMonitor-{HostInstanceId.Value}-{requestId}";

                // The script group is made available to the account the Runner will be started
                // as, taken from the same credential resolution that built the process security
                // context rather than assumed to be the Monitor's own identity.
                var readerIdentity = new ScriptGroupReaderIdentity(domainName, processAccountName);

                Terraform.TerraformPlanStore? planStore = null;

                try
                {
                    Task scriptGroupPipeTask = _scriptGroupPipeServer.Start(
                            startedScriptGroupPipeName,
                            scriptGroup,
                            readerIdentity,
                            pipeCancellationTokenSource.Token);
                    logger.LogInformation($"Server named pipe with the name '{startedScriptGroupPipeName}' has started.");

                    var runnerLogPathSetting = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build()
                    .GetSection("AppSettings")["RunnerLogPath"]!;
                    var runnerLogPath = runnerLogPathSetting + $"\\{startedScriptGroupPipeName}.txt";
                    var uncLogPath = runnerLogPath.Replace("c:", @"\\" + System.Environment.GetEnvironmentVariable("COMPUTERNAME"));

                    _requestsPersistentSource.UpdateUncLogPath(requestId, uncLogPath);

                    // The plan and its rendered content carry the variable values they were planned
                    // with, so they get a directory of their own, restricted to this deployment's
                    // identity, rather than the shared inherited-permission folder they used to
                    // share with every plan the host had ever produced.
                    planStore = Terraform.TerraformPlanStore.Reserve(
                        logger,
                        Path.Join(DorcProgramData.Root, "terraform-plans"),
                        deploymentResult.Id,
                        readerIdentity);

                    var terraformPlanFileName = deploymentResult.Id.CreateTerraformPlanBlobName();
                    var terraformPlanFilePath = planStore.PathOf(terraformPlanFileName);
                    var terraformPlanContentFileName = deploymentResult.Id.CreateTerraformPlanContentBlobName();
                    var terraformPlanContentFilePath = planStore.PathOf(terraformPlanContentFileName);
                    if (terreformOperation == TerraformRunnerOperations.ApplyPlan)
                    {
                        _azureStorageAccountWorker.DownloadFileFromBlobs(terraformPlanFileName, terraformPlanFilePath);
                    }

                    var processStarter = new TerraformRunnerProcessStarter(logger)
                    {
                        RunnerExecutableFullName = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build().GetSection("AppSettings")["TerraformDeploymentRunnerPath"],
                        ScriptGroupPipeName = startedScriptGroupPipeName,
                        RunnerLogPath = runnerLogPath,
                        PlanFilePath = terraformPlanFilePath,
                        PlanContentFilePath = terraformPlanContentFilePath,
                        TerrafromRunnerOperation = terreformOperation
                    };
                    try
                    {
                        Interop.Kernel32.STARTUPINFO startupInfo = new ProcessStartupInfoBuilder(logger).Build();
                        logger.LogInformation("Starting Runner process.");

                        cancellationToken.ThrowIfCancellationRequested();

                        var process = processStarter.Start(startupInfo, securityContext);
                        try
                        {
                            if (Marshal.GetLastWin32Error() != 0)
                            {
                                logger.LogError("The process creation was not successful.");
                                throw new Win32Exception(Marshal.GetLastWin32Error());
                            }

                            _processesPersistentSource.AssociateProcessWithRequest((int)process.Id, requestId);

                            if (cancellationToken.IsCancellationRequested)
                            {
                                logger.LogDebug("Trying to terminate the Runner process.");
                                process.Kill();
                                logger.LogInformation("The Runner process is terminated.");
                                throw new OperationCanceledException("The Runner process is terminated.");
                            }

                            logger.LogDebug("Waiting for process to exit.");
                            var resultCode = process.WaitForExit();
                            logger.LogInformation($"Runner finished for request ID '{requestId}' with result code [{resultCode}]");

                            cancellationToken.ThrowIfCancellationRequested();

                            if (resultCode == RunnerProcess.RunnerProcess.ProcessTerminatedExitCode)
                            {
                                logger.LogInformation("The Runner process is terminated.");
                                throw new OperationCanceledException("The Runner process is terminated.");
                            }
                            else if (resultCode != 0)
                            {
                                isScriptExecutionSuccessful = false;
                                Exception? ex = new Win32Exception(Marshal.GetLastWin32Error());

                                if (ex != null)
                                {
                                    logger.LogError("The Win32 exception with HRESULT error code is detected immediately after WaitForExit invocation."
                                                           + " Message:" + ex.Message
                                                           + "; Source: " + ex.Source
                                                           + "; Data: " + ex.Data
                                                           + "; HelpLink: " + ex.HelpLink
                                                           + "; InnerException: " + ex.InnerException
                                                           + "; TargetSite: " + ex.TargetSite + ".");
                                }
                            }

                            if (Marshal.GetLastWin32Error() != 0)
                            {
                                logger.LogError("Waiting the process to exit was not successful.");
                                throw new Win32Exception(Marshal.GetLastWin32Error());
                            }
                        }
                        finally
                        {
                            pipeCancellationTokenSource.Cancel();
                            _processesPersistentSource.RemoveProcess((int)process.Id);
                            process.Dispose();
                        }
                    }
                    catch (Exception e)
                    {
                        pipeCancellationTokenSource.Cancel();
                        logger.LogError($"Exception is thrown while operating with the Runner process. Exception: {e}");
                        throw;
                    }
                
                    if (isScriptExecutionSuccessful)
                    switch (terreformOperation)
                    {
                        case TerraformRunnerOperations.CreatePlan:
                            // save Terraform binary plan file to Azure Storage Account
                            _azureStorageAccountWorker.SaveFileToBlobs(terraformPlanFilePath);
                            // save Terraform human-readable plan file to Azure Storage Account
                            _azureStorageAccountWorker.SaveFileToBlobs(terraformPlanContentFilePath);

                            // Both artefacts now have a durable copy in blob storage, which is
                            // where the apply reads the plan back from - so the local ones, which
                            // list the variable values the plan was made with, are withdrawn.
                            // Reached only if both uploads returned: if either threw, the local
                            // copy is the only copy and is deliberately left in place.
                            planStore.Expire();

                            // Update status to WaitingConfirmation
                            _requestsPersistentSource.UpdateResultStatus(
                                deploymentResult,
                                DeploymentResultStatus.WaitingConfirmation);

                            logger.LogInformation($"Terraform plan created for component '{component.ComponentName}'. Waiting for confirmation.");
                            break;

                        case TerraformRunnerOperations.ApplyPlan:
                            // Update status to WaitingConfirmation
                            _requestsPersistentSource.UpdateResultStatus(
                                deploymentResult,
                                DeploymentResultStatus.Complete);

                            logger.LogInformation($"Terraform plan applied for component '{component.ComponentName}'. Completed.");
                            break;
                    }
                }
                finally
                {
                    // The bundle carries the resolved deployment properties, secrets
                    // included. It is needed for the span of one Runner process, so it is
                    // withdrawn as soon as that process is gone - on the failure and
                    // cancellation paths as much as the successful one.
                    _scriptGroupPipeServer.Expire(startedScriptGroupPipeName);

                    if (terreformOperation == TerraformRunnerOperations.ApplyPlan)
                    {
                        // On the apply path the local plan is a cache of the blob that was
                        // downloaded into it, so nothing is lost by withdrawing it whatever the
                        // outcome. The plan path is the asymmetric one: there the local copy is
                        // the original, and it is expired only once the upload has succeeded.
                        planStore?.Expire();
                    }
                }

            }

            return isScriptExecutionSuccessful;
        }

        /// <summary>
        /// See ScriptDispatcher: the same invariant, on the branch that serialises a script
        /// group for the Terraform Runner.
        /// </summary>
        private IDictionary<string, VariableValue> WithheldKeysRemoved(
            IDictionary<string, VariableValue> properties)
        {
            var withheld = properties.Keys.Where(_scriptScopeConfigValues.IsWithheld).ToList();

            if (withheld.Count == 0)
            {
                return properties;
            }

            logger.LogWarning(
                "Removed {Count} withheld key(s) from the Terraform script group before dispatch: {Keys}.",
                withheld.Count,
                string.Join(", ", withheld));

            return properties
                .Where(property => !_scriptScopeConfigValues.IsWithheld(property.Key))
                .ToDictionary(property => property.Key, property => property.Value);
        }

        private ScriptGroup GetScriptGroup(
            string scriptsLocation,
            IDictionary<string, VariableValue> properties,
            int deploymentResultId,
            ComponentApiModel component,
            DeploymentRequestApiModel request,
            ProjectApiModel? project)
        {
            var scriptGroup = new ScriptGroup()
            {
                ID = Guid.NewGuid(),
                DeployResultId = deploymentResultId,
                ScriptsLocation = scriptsLocation,
                CommonProperties = WithheldKeysRemoved(properties),
                ScriptProperties = new List<ScriptProperties>()
            };

            // Use the configurator to set Terraform-specific fields based on source type
            _sourceConfigurator.ConfigureScriptGroup(scriptGroup, component, request, project, properties);

            return scriptGroup;
        }

        private class TerraformExecutionResult
        {
            public bool Success { get; set; }
            public string? Output { get; set; }
            public string? ErrorMessage { get; set; }
        }

        private class TerraformPlanInfo
        {
            public string WorkingDirectory { get; set; } = "";
            public string PlanFileName { get; set; } = "";
            public string PlanFilePath { get; set; } = "";
        }
    }
}