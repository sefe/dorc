using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core.AzureStorageAccount;
using Dorc.Core.Configuration;
using Dorc.Monitor.Pipes;
using Dorc.Monitor.RunnerProcess;
using Dorc.Monitor.RunnerProcess.Interop.Windows.Kernel32;
using Dorc.Monitor.TerraformSourceConfig;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Dorc.Monitor
{
    public class TerraformDispatcher : ITerraformDispatcher
    {
        private const string ProdDeployUsernamePropertyName = "DORC_ProdDeployUsername";
        private const string ProdDeployPasswordPropertyName = "DORC_ProdDeployPassword";
        private const string NonProdDeployUsernamePropertyName = "DORC_NonProdDeployUsername";
        private const string NonProdDeployPasswordPropertyName = "DORC_NonProdDeployPassword";

        private readonly ILogger logger;
        private readonly IRequestsPersistentSource _requestsPersistentSource;
        private readonly IConfigValuesPersistentSource _configValuesPersistentSource;
        private readonly IConfigurationSettings _configurationSettingsEngine;
        private readonly IDeploymentRequestProcessesPersistentSource _processesPersistentSource;
        private readonly IScriptGroupPipeServer _scriptGroupPipeServer;
        private readonly IAzureStorageAccountWorker _azureStorageAccountWorker;
        private readonly IProjectsPersistentSource _projectsPersistentSource;
        private readonly TerraformSourceConfigurator _sourceConfigurator;

        private bool isScriptExecutionSuccessful; // This field is needed to be instance-wide since Runner process errors are processed as instance-wide events.

        public TerraformDispatcher(
            ILogger<TerraformDispatcher> logger,
            IRequestsPersistentSource requestsPersistentSource,
            IConfigValuesPersistentSource configValuesPersistentSource,
            IConfigurationSettings configurationSettingsEngine,
            IDeploymentRequestProcessesPersistentSource processesPersistentSource,
            IScriptGroupPipeServer scriptGroupPipeServer,
            IAzureStorageAccountWorker azureStorageAccountWorker,
            IProjectsPersistentSource projectsPersistentSource)
        {
            this.logger = logger;
            this._requestsPersistentSource = requestsPersistentSource;
            this._configValuesPersistentSource = configValuesPersistentSource;
            this._configurationSettingsEngine = configurationSettingsEngine;
            this._processesPersistentSource = processesPersistentSource;
            this._scriptGroupPipeServer = scriptGroupPipeServer;
            this._azureStorageAccountWorker = azureStorageAccountWorker;
            this._projectsPersistentSource = projectsPersistentSource;
            this._sourceConfigurator = new TerraformSourceConfigurator(logger, _configurationSettingsEngine);
        }

        public bool Dispatch(
            ComponentApiModel component,
            DeploymentResultApiModel deploymentResult,
            IDictionary<string, VariableValue> properties,
            int requestId,
            bool isProduction,
            string environmentName,
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

            var processCredentials = GetProcessCredentials(isProduction, environmentName);
            string processAccountName = processCredentials.Item1;
            string processAccountPassword = processCredentials.Item2;
            if (string.IsNullOrEmpty(processAccountName)
                || string.IsNullOrEmpty(processAccountPassword))
            {
                logger.LogError($"Unable to find a valid DOrc Username or Password for environment '{environmentName}'.");

                isScriptExecutionSuccessful = false;
                return isScriptExecutionSuccessful;
            }

            // Get the script root from configuration
            var scriptRoot = _configValuesPersistentSource.GetConfigValue("ScriptRoot");

            // Resolve the full script path by combining script root with component script path
            var fullScriptPath = string.IsNullOrEmpty(scriptRoot)
                ? component.ScriptPath
                : Path.Combine(scriptRoot, component.ScriptPath);

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
                fullScriptPath,
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
                var startedScriptGroupPipeName = "DOrcMonitor-" + requestId;
                Task scriptGroupPipeTask = _scriptGroupPipeServer.Start(
                        startedScriptGroupPipeName,
                        scriptGroup,
                        pipeCancellationTokenSource.Token);
                logger.LogInformation($"Server named pipe with the name '{startedScriptGroupPipeName}' has started.");

                var runnerLogPathSetting = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build()
                .GetSection("AppSettings")["RunnerLogPath"]!;
                var runnerLogPath = runnerLogPathSetting + $"\\{startedScriptGroupPipeName}.txt";
                var uncLogPath = runnerLogPath.Replace("c:", @"\\" + System.Environment.GetEnvironmentVariable("COMPUTERNAME"));

                _requestsPersistentSource.UpdateUncLogPath(requestId, uncLogPath);

                var planStorageDir = Path.Combine(Path.GetTempPath(), "terraform-plans");
                if (!Directory.Exists(planStorageDir))
                    Directory.CreateDirectory(planStorageDir);
                var terraformPlanFileName = deploymentResult.Id.CreateTerraformPlanBlobName();
                var terraformPlanFilePath = Path.Combine(planStorageDir, terraformPlanFileName);
                var terraformPlanContentFileName = deploymentResult.Id.CreateTerraformPlanContentBlobName();
                var terraformPlanContentFilePath = Path.Combine(planStorageDir, terraformPlanContentFileName);
                if (terreformOperation == TerraformRunnerOperations.ApplyPlan)
                {
                    _azureStorageAccountWorker.DownloadFileFromBlobs(terraformPlanFileName, terraformPlanFilePath);
                }

                var processStarter = new TerraformRunnerProcessStarter(logger)
                {
                    RunnerExecutableFullName = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build().GetSection("AppSettings")["TerraformDeploymentRunnerPath"],
                    ScriptPath = fullScriptPath,
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
                        logger.LogInformation("Runner process has been created.");
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
                
                switch (terreformOperation)
                {
                    case TerraformRunnerOperations.CreatePlan:
                        // save Terraform binary plan file to Azure Storage Account
                        _azureStorageAccountWorker.SaveFileToBlobs(terraformPlanFilePath);
                        // save Terraform human-readable plan file to Azure Storage Account
                        _azureStorageAccountWorker.SaveFileToBlobs(terraformPlanContentFilePath);

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

            return true;
        }

        private (string, string) GetProcessCredentials(bool isProduction, string environmentName)
        {
            if (isProduction)
            {
                return (GetConfigValue(ProdDeployUsernamePropertyName),
                    GetConfigValue(ProdDeployPasswordPropertyName));
            }

            return (GetConfigValue(NonProdDeployUsernamePropertyName),
                GetConfigValue(NonProdDeployPasswordPropertyName));
        }

        private string GetConfigValue(string configValue)
        {
            if (string.IsNullOrEmpty(configValue))
                throw new ApplicationException($"Config value name is empty, should have a value");
            return _configValuesPersistentSource.GetConfigValue(configValue);
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
                CommonProperties = properties,
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