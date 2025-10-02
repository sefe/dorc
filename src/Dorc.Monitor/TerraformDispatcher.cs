using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core.AzureStorageAccount;
using Dorc.Core.Configuration;
using Dorc.Monitor.Pipes;
using Dorc.Monitor.RunnerProcess;
using Dorc.Monitor.RunnerProcess.Interop.Windows.Kernel32;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources;
using Dorc.PersistentData.Sources.Interfaces;
using log4net;
using Microsoft.Extensions.Configuration;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Dorc.Monitor
{
    public class TerraformDispatcher : ITerraformDispatcher
    {
        private const string ProdDeployUsernamePropertyName = "DORC_ProdDeployUsername";
        private const string ProdDeployPasswordPropertyName = "DORC_ProdDeployPassword";
        private const string NonProdDeployUsernamePropertyName = "DORC_NonProdDeployUsername";
        private const string NonProdDeployPasswordPropertyName = "DORC_NonProdDeployPassword";

        private readonly ILog logger;
        private readonly IRequestsPersistentSource _requestsPersistentSource;
        private readonly IConfigValuesPersistentSource _configValuesPersistentSource;
        private readonly IConfigurationSettings _configurationSettingsEngine;
        private readonly IDeploymentRequestProcessesPersistentSource _processesPersistentSource;
        private readonly IScriptGroupPipeServer _scriptGroupPipeServer;
        private readonly IAzureStorageAccountWorker _azureStorageAccountWorker;

        private bool isScriptExecutionSuccessful; // This field is needed to be instance-wide since Runner process errors are processed as instance-wide events.

        public TerraformDispatcher(
            ILog logger,
            IRequestsPersistentSource requestsPersistentSource,
            IConfigValuesPersistentSource configValuesPersistentSource,
            IConfigurationSettings configurationSettingsEngine,
            IDeploymentRequestProcessesPersistentSource processesPersistentSource,
            IScriptGroupPipeServer scriptGroupPipeServer,
            IAzureStorageAccountWorker azureStorageAccountWorker)
        {
            this.logger = logger;
            this._requestsPersistentSource = requestsPersistentSource;
            this._configValuesPersistentSource = configValuesPersistentSource;
            this._configurationSettingsEngine = configurationSettingsEngine;
            this._processesPersistentSource = processesPersistentSource;
            this._scriptGroupPipeServer = scriptGroupPipeServer;
            this._azureStorageAccountWorker = azureStorageAccountWorker;
        }

        //public async Task<bool> DispatchAsync(
        //    ComponentApiModel component,
        //    DeploymentResultApiModel deploymentResult,
        //    IDictionary<string, VariableValue> properties,
        //    int requestId,
        //    bool isProduction,
        //    string environmentName,
        //    StringBuilder resultLogBuilder,
        //    CancellationToken cancellationToken)
        //{
        //    cancellationToken.ThrowIfCancellationRequested();

        //    logger.Info($"TerraformDispatcher.DispatchAsync called for component '{component.ComponentName}' with id '{component.ComponentId}', deployment result id '{deploymentResult.Id}', environment '{environmentName}'.");

        //    try
        //    {
        //        // Update status to Running
        //        _requestsPersistentSource.UpdateResultStatus(
        //            deploymentResult,
        //            DeploymentResultStatus.Running);

        //        logger.Info($"Updated deployment result {deploymentResult.Id} status to Running.");

        //        // Create terraform plan (placeholder implementation)
        //        var planContent = await CreateTerraformPlanAsync(component, properties, environmentName, cancellationToken);

        //        // Save plan to blob storage (placeholder implementation)
        //        var blobUrl = await SavePlanToBlobStorageAsync(planContent, deploymentResult.Id, cancellationToken);

        //        resultLogBuilder.AppendLine($"Terraform plan created successfully for component '{component.ComponentName}'");
        //        resultLogBuilder.AppendLine($"Plan stored at: {blobUrl}");

        //        // Update status to WaitingConfirmation
        //        _requestsPersistentSource.UpdateResultStatus(
        //            deploymentResult,
        //            DeploymentResultStatus.WaitingConfirmation);

        //        logger.Info($"Terraform plan created for component '{component.ComponentName}'. Waiting for confirmation.");
        //        return true;
        //    }
        //    catch (Exception ex)
        //    {
        //        // Update status to Failed
        //        _requestsPersistentSource.UpdateResultStatus(
        //            deploymentResult,
        //            DeploymentResultStatus.Failed);

        //        logger.Error($"Failed to create Terraform plan for component '{component.ComponentName}': {ex.Message}", ex);
        //        resultLogBuilder.AppendLine($"Failed to create Terraform plan: {ex.Message}");
        //        return false;
        //    }
        //}

        public async Task<bool> DispatchAsync(
            ComponentApiModel component,
            DeploymentResultApiModel deploymentResult,
            IDictionary<string, VariableValue> properties,
            int requestId,
            bool isProduction,
            string environmentName,
            StringBuilder resultLogBuilder,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            logger.Info($"TerraformDispatcher.DispatchAsync called for component '{component.ComponentName}' with id '{component.ComponentId}', deployment result id '{deploymentResult.Id}', environment '{environmentName}'.");

            // Update status to Running
            _requestsPersistentSource.UpdateResultStatus(
                deploymentResult,
                DeploymentResultStatus.Running);

            logger.Info($"Updated deployment result {deploymentResult.Id} status to Running.");

            isScriptExecutionSuccessful = true;

            var processCredentials = GetProcessCredentials(isProduction, environmentName);
            string processAccountName = processCredentials.Item1;
            string processAccountPassword = processCredentials.Item2;
            if (string.IsNullOrEmpty(processAccountName)
                || string.IsNullOrEmpty(processAccountPassword))
            {
                logger.Error($"Unable to find a valid DOrc Username or Password for environment '{environmentName}'.");

                isScriptExecutionSuccessful = false;
                return isScriptExecutionSuccessful;
            }

            // Get the script root from configuration
            var scriptRoot = _configValuesPersistentSource.GetConfigValue("ScriptRoot");

            // Resolve the full script path by combining script root with component script path
            var fullScriptPath = string.IsNullOrEmpty(scriptRoot)
                ? component.ScriptPath
                : Path.Combine(scriptRoot, component.ScriptPath);

            var scriptGroup = GetScriptGroup(
                fullScriptPath,
                properties,
                deploymentResult.Id);

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
                logger.Info($"Server named pipe with the name '{startedScriptGroupPipeName}' has started.");

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
                var terraformPlanContentFileName = deploymentResult.Id.CreateTerraformPlanContantBlobName();
                var terraformPlanContentFilePath = Path.Combine(planStorageDir, terraformPlanContentFileName);

                var processStarter = new TerraformRunnerProcessStarter(logger)
                {
                    RunnerExecutableFullName = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build().GetSection("AppSettings")["TerraformDeploymentRunnerPath"],
                    ScriptPath = fullScriptPath,
                    ScriptGroupPipeName = startedScriptGroupPipeName,
                    RunnerLogPath = runnerLogPath,
                    PlanFilePath = terraformPlanFilePath,
                    PlanContentFilePath = terraformPlanContentFilePath,
                };
                try
                {
                    Interop.Kernel32.STARTUPINFO startupInfo = new ProcessStartupInfoBuilder(logger).Build();
                    logger.Info("Starting Runner process.");

                    cancellationToken.ThrowIfCancellationRequested();

                    var process = processStarter.Start(startupInfo, securityContext);
                    try
                    {
                        if (Marshal.GetLastWin32Error() != 0)
                        {
                            logger.Error("The process creation was not successful.");
                            throw new Win32Exception(Marshal.GetLastWin32Error());
                        }

                        _processesPersistentSource.AssociateProcessWithRequest((int)process.Id, requestId);

                        if (cancellationToken.IsCancellationRequested)
                        {
                            logger.Debug("Trying to terminate the Runner process.");
                            process.Kill();
                            logger.Info("The Runner process is terminated.");
                            throw new OperationCanceledException("The Runner process is terminated.");
                        }

                        logger.Debug("Waiting for process to exit.");
                        var resultCode = process.WaitForExit();
                        logger.Info($"Runner finished for request ID '{requestId}' with result code [{resultCode}]");

                        cancellationToken.ThrowIfCancellationRequested();

                        if (resultCode == RunnerProcess.RunnerProcess.ProcessTerminatedExitCode)
                        {
                            logger.Info("The Runner process is terminated.");
                            throw new OperationCanceledException("The Runner process is terminated.");
                        }
                        else if (resultCode != 0)
                        {
                            isScriptExecutionSuccessful = false;
                            Exception? ex = new Win32Exception(Marshal.GetLastWin32Error());

                            if (ex != null)
                            {
                                logger.Error("The Win32 exception with HRESULT error code is detected immediately after WaitForExit invocation."
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
                            logger.Error("Waiting the process to exit was not successful.");
                            throw new Win32Exception(Marshal.GetLastWin32Error());
                        }
                        logger.Info("Runner process has been created.");
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
                    logger.Error($"Exception is thrown while operating with the Runner process. Exception: {e}");
                    throw;
                }
                
                //save Terraform binary plan file to Azure Storage Account
                await _azureStorageAccountWorker.SaveFileToBlobsAsync(terraformPlanFilePath);
                //save Terraform human-readable plan file to Azure Storage Account
                await _azureStorageAccountWorker.SaveFileToBlobsAsync(terraformPlanContentFilePath);

                // Update status to WaitingConfirmation
                _requestsPersistentSource.UpdateResultStatus(
                    deploymentResult,
                    DeploymentResultStatus.WaitingConfirmation);

                logger.Info($"Terraform plan created for component '{component.ComponentName}'. Waiting for confirmation.");

            }

            return true;
        }

        private async Task<string> CreateTerraformPlanAsync(
            ComponentApiModel component,
            IDictionary<string, VariableValue> properties,
            string environmentName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var terraformWorkingDir = await SetupTerraformWorkingDirectoryAsync(component, environmentName, cancellationToken);
            
            try
            {
                // Initialize Terraform if needed
                await RunTerraformCommandAsync(terraformWorkingDir, "init", cancellationToken);
                
                // Create Terraform variables file
                await CreateTerraformVariablesFileAsync(terraformWorkingDir, properties, cancellationToken);
                
                // Generate the plan
                var planFileName = $"tfplan-{DateTime.UtcNow:yyyy-MM-dd-HH-mm-ss}";
                var planArgs = $"plan -out={planFileName} -detailed-exitcode";
                
                var planResult = await RunTerraformCommandAsync(terraformWorkingDir, planArgs, cancellationToken);
                
                // Get human-readable plan output
                var showArgs = $"show -no-color {planFileName}";
                var planContent = await RunTerraformCommandAsync(terraformWorkingDir, showArgs, cancellationToken);
                
                logger.Info($"Terraform plan created successfully for component '{component.ComponentName}'");
                return planContent;
            }
            catch (Exception ex)
            {
                logger.Error($"Failed to create Terraform plan for component '{component.ComponentName}': {ex.Message}", ex);
                throw;
            }
        }

        private async Task<string> SetupTerraformWorkingDirectoryAsync(
            ComponentApiModel component, 
            string environmentName, 
            CancellationToken cancellationToken)
        {
            // Get the script root from configuration
            var scriptRoot = _configValuesPersistentSource.GetConfigValue("ScriptRoot");
            
            // Resolve the full script path by combining script root with component script path
            var fullScriptPath = string.IsNullOrEmpty(scriptRoot) 
                ? component.ScriptPath 
                : Path.Combine(scriptRoot, component.ScriptPath);

            logger.Info($"Resolving Terraform script path for component '{component.ComponentName}': ScriptRoot='{scriptRoot}', ComponentScriptPath='{component.ScriptPath}', FullPath='{fullScriptPath}'");
            
            // Create a unique working directory for this deployment
            var workingDir = Path.Combine(
                Path.GetTempPath(), 
                "terraform-workdir", 
                $"{component.ComponentName}-{environmentName}-{DateTime.UtcNow:yyyy-MM-dd-HH-mm-ss}");
            
            Directory.CreateDirectory(workingDir);
            
            // Copy Terraform files from component script path to working directory
            if (!string.IsNullOrEmpty(fullScriptPath) && Directory.Exists(fullScriptPath))
            {
                logger.Info($"Copying Terraform files from '{fullScriptPath}' to working directory '{workingDir}'");
                await CopyDirectoryAsync(fullScriptPath, workingDir, cancellationToken);
            }
            else
            {
                throw new InvalidOperationException($"Terraform script path '{fullScriptPath}' does not exist for component '{component.ComponentName}'. ScriptRoot='{scriptRoot}', ComponentScriptPath='{component.ScriptPath}'");
            }
            
            return workingDir;
        }

        private async Task CopyDirectoryAsync(string sourceDir, string destDir, CancellationToken cancellationToken)
        {
            await Task.Run(() =>
            {
                foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var relativePath = Path.GetRelativePath(sourceDir, file);
                    var destFile = Path.Combine(destDir, relativePath);
                    var destFileDir = Path.GetDirectoryName(destFile);
                    
                    if (!string.IsNullOrEmpty(destFileDir))
                    {
                        Directory.CreateDirectory(destFileDir);
                    }
                    
                    File.Copy(file, destFile, true);
                }
            }, cancellationToken);
        }

        private async Task CreateTerraformVariablesFileAsync(
            string workingDir, 
            IDictionary<string, VariableValue> properties, 
            CancellationToken cancellationToken)
        {
            var variablesContent = new StringBuilder();
            
            foreach (var property in properties)
            {
                // Convert DOrc properties to Terraform variable format
                var value = property.Value.Value?.ToString() ?? "";
                
                // Escape quotes and handle different types
                if (property.Value.Type == typeof(string))
                {
                    value = value.Replace("${", "$${");
                    value = Regex.Replace(value, @"(?<!\\)\\(?!\\|\"")", @"\\");
                    value = $"\"{value.Replace("\"", "\\\"")}\"";
                    value = Regex.Replace(value, @"^""\\{2}", @"""\\\\");
                    value = value.Replace("\r\n", " ");
                }
                else if (property.Value.Type == typeof(bool))
                {
                    value = value.ToLowerInvariant();
                }
                else
                {
                    value = Newtonsoft.Json.JsonConvert.SerializeObject(property.Value.Value);
                }

                // Only [a-zA-Z0-9_] symbols can be used in Terrafrom identifiers. Replace all others with '_'
                var propertyName = Regex.Replace(property.Key, "[^a-zA-Z0-9_]", ""); ;
                
                variablesContent.AppendLine($"{propertyName} = {value}");
            }
            
            var variablesFilePath = Path.Combine(workingDir, "terraform.tfvars");
            await File.WriteAllTextAsync(variablesFilePath, variablesContent.ToString(), cancellationToken);
        }

        private async Task<string> RunTerraformCommandAsync(
            string workingDir, 
            string arguments, 
            CancellationToken cancellationToken)
        {
            using var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = "terraform";
            process.StartInfo.Arguments = arguments;
            process.StartInfo.WorkingDirectory = workingDir;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.CreateNoWindow = true;

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    outputBuilder.AppendLine(e.Data);
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    errorBuilder.AppendLine(e.Data);
                }
            };

            logger.Debug($"Running Terraform command: terraform {arguments} in {workingDir}");
            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // Wait for the process to complete or be cancelled
                while (!process.HasExited)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Delay(100, cancellationToken);
                }
            }
            catch (Exception e)
            {
                logger.Error(e);
            }

            var output = outputBuilder.ToString();
            var error = errorBuilder.ToString();

            if (process.ExitCode != 0)
            {
                var errorMessage = $"Terraform command failed with exit code {process.ExitCode}. Error: {error}";
                logger.Error(errorMessage);
                throw new InvalidOperationException(errorMessage);
            }

            logger.Debug($"Terraform command completed successfully. Output: {output}");
            return output;
        }

        private async Task<string> SavePlanToBlobStorageAsync(
            string planContent,
            int deploymentResultId,
            CancellationToken cancellationToken)
        {
            // For now, save to local file system - this should be replaced with Azure Blob Storage
            var planStorageDir = Path.Combine(Path.GetTempPath(), "terraform-plans");
            Directory.CreateDirectory(planStorageDir);
            
            var fileName = $"plan-{deploymentResultId}-{DateTime.UtcNow:yyyy-MM-dd-HH-mm-ss}.txt";
            var filePath = Path.Combine(planStorageDir, fileName);
            
            await File.WriteAllTextAsync(filePath, planContent, cancellationToken);
            
            // Also save metadata about the plan for later execution
            var metadataFileName = $"plan-{deploymentResultId}-metadata.json";
            var metadataFilePath = Path.Combine(planStorageDir, metadataFileName);
            
            var metadata = new
            {
                DeploymentResultId = deploymentResultId,
                PlanContentPath = filePath,
                CreatedAt = DateTime.UtcNow,
                Status = "WaitingConfirmation"
            };
            
            var metadataJson = System.Text.Json.JsonSerializer.Serialize(metadata, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(metadataFilePath, metadataJson, cancellationToken);
            
            logger.Info($"Terraform plan saved to: {filePath}");
            
            // Return a mock blob URL for now - replace with actual Azure Blob Storage URL
            var blobUrl = $"file://{filePath}";
            
            return blobUrl;
        }

        public async Task<bool> ExecuteConfirmedPlanAsync(
            int deploymentResultId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            logger.Info($"Executing confirmed Terraform plan for deployment result ID: {deploymentResultId}");

            DeploymentResultApiModel? deploymentResult = null;
            try
            {
                // Get the deployment result to check its status
                deploymentResult = _requestsPersistentSource.GetDeploymentResults(deploymentResultId);
                if (deploymentResult == null)
                {
                    throw new InvalidOperationException($"Deployment result with ID {deploymentResultId} not found");
                }

                if (deploymentResult.Status != DeploymentResultStatus.Confirmed.ToString())
                {
                    throw new InvalidOperationException($"Deployment result {deploymentResultId} is not in Confirmed status. Current status: {deploymentResult.Status}");
                }

                // Update status to Running
                _requestsPersistentSource.UpdateResultStatus(
                    deploymentResult,
                    DeploymentResultStatus.Running);

                // Execute the actual Terraform plan
                var executionResult = await ExecuteTerraformPlanAsync(deploymentResultId, cancellationToken);

                if (executionResult.Success)
                {
                    // Update status to Complete
                    _requestsPersistentSource.UpdateResultStatus(
                        deploymentResult,
                        DeploymentResultStatus.Complete);

                    logger.Info($"Terraform plan executed successfully for deployment result ID: {deploymentResultId}");
                    return true;
                }
                else
                {
                    // Update status to Failed
                    _requestsPersistentSource.UpdateResultStatus(
                        deploymentResult,
                        DeploymentResultStatus.Failed);

                    logger.Error($"Terraform plan execution failed for deployment result ID {deploymentResultId}: {executionResult.ErrorMessage}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Failed to execute Terraform plan for deployment result ID {deploymentResultId}: {ex.Message}", ex);
                
                // Update status to Failed
                if (deploymentResult != null)
                {
                    _requestsPersistentSource.UpdateResultStatus(
                        deploymentResult,
                        DeploymentResultStatus.Failed);
                }
                
                return false;
            }
        }

        private async Task<TerraformExecutionResult> ExecuteTerraformPlanAsync(
            int deploymentResultId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // Find the stored plan files and working directory from the temporary storage
                var planInfo = await FindStoredPlanInfoAsync(deploymentResultId, cancellationToken);
                if (planInfo == null)
                {
                    return new TerraformExecutionResult 
                    { 
                        Success = false, 
                        ErrorMessage = "Terraform plan files not found or have been cleaned up" 
                    };
                }

                // Execute terraform apply using the stored plan
                var applyArgs = $"apply -auto-approve {planInfo.PlanFileName}";
                logger.Info($"Executing Terraform apply for deployment result ID: {deploymentResultId}");
                
                var applyOutput = await RunTerraformCommandAsync(planInfo.WorkingDirectory, applyArgs, cancellationToken);

                logger.Info($"Terraform apply completed successfully for deployment result ID: {deploymentResultId}");
                
                return new TerraformExecutionResult 
                { 
                    Success = true, 
                    Output = applyOutput 
                };
            }
            catch (Exception ex)
            {
                logger.Error($"Terraform apply failed for deployment result ID {deploymentResultId}: {ex.Message}", ex);
                return new TerraformExecutionResult 
                { 
                    Success = false, 
                    ErrorMessage = ex.Message,
                    Output = ex.StackTrace 
                };
            }
        }

        private async Task<TerraformPlanInfo?> FindStoredPlanInfoAsync(
            int deploymentResultId,
            CancellationToken cancellationToken)
        {
            await Task.CompletedTask; // Remove async warning
            
            try
            {
                // Look for terraform working directories that match this deployment
                var tempDir = Path.GetTempPath();
                var terraformWorkDir = Path.Combine(tempDir, "terraform-workdir");
                
                if (!Directory.Exists(terraformWorkDir))
                {
                    return null;
                }

                // Find directories that might contain our plan
                var workingDirs = Directory.GetDirectories(terraformWorkDir)
                    .Where(dir => Directory.GetFiles(dir, "tfplan-*").Length > 0)
                    .OrderByDescending(dir => Directory.GetCreationTime(dir))
                    .ToList();

                foreach (var workingDir in workingDirs)
                {
                    var planFiles = Directory.GetFiles(workingDir, "tfplan-*");
                    if (planFiles.Length > 0)
                    {
                        // Use the most recent plan file
                        var planFile = planFiles.OrderByDescending(f => File.GetCreationTime(f)).First();
                        var planFileName = Path.GetFileName(planFile);
                        
                        return new TerraformPlanInfo
                        {
                            WorkingDirectory = workingDir,
                            PlanFileName = planFileName,
                            PlanFilePath = planFile
                        };
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                logger.Error($"Failed to find stored plan info for deployment result ID {deploymentResultId}: {ex.Message}", ex);
                return null;
            }
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
            int deploymentResultId)
        {
            return new ScriptGroup()
            {
                ID = Guid.NewGuid(),
                DeployResultId = deploymentResultId,
                ScriptsLocation = scriptsLocation,
                CommonProperties = properties,
                ScriptProperties = new List<ScriptProperties>()
            };
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