using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Runner.Logger;
using Dorc.TerraformRunner.CodeSources;
using Dorc.TerraformRunner.Pipes;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;

namespace Dorc.TerraformRunner
{
    internal class TerraformProcessor : ITerraformProcessor
    {
        private readonly IRunnerLogger logger;
        private readonly IScriptGroupPipeClient _scriptGroupPipeClient;
        private readonly TerraformCodeSourceProviderFactory _codeSourceFactory;

        public TerraformProcessor(
            IRunnerLogger logger,
            IScriptGroupPipeClient scriptGroupPipeClient)
        {
            this.logger = logger;
            this._scriptGroupPipeClient = scriptGroupPipeClient;
            this._codeSourceFactory = new TerraformCodeSourceProviderFactory(logger);
        }

        public async Task<bool> PreparePlanAsync(
            string pipeName,
            int requestId,
            string resultFilePath,
            string planContentFilePath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ScriptGroup scriptGroupProperties = this._scriptGroupPipeClient.GetScriptGroupProperties(pipeName);
            var deployResultId = scriptGroupProperties.DeployResultId;
            var properties = scriptGroupProperties.CommonProperties;

            this.logger.SetRequestId(requestId);
            this.logger.SetDeploymentResultId(deployResultId);

            logger.Information($"TerraformProcessor.PreparePlan called for request' with id '{requestId}', deployment result id '{deployResultId}'.");
            
            var terraformWorkingDir = string.Empty;

            try
            {
                terraformWorkingDir = CreateTerraformWorkingDirectory(requestId);
                await ProvisionTerraformWorkingDirectoryAsync(terraformWorkingDir, scriptGroupProperties, cancellationToken);

                // Create terraform plan
                var planContent = await CreateTerraformPlanAsync(properties, terraformWorkingDir, resultFilePath, planContentFilePath, requestId, cancellationToken);

                logger.Information($"Terraform plan created for request '{requestId}'. Waiting for confirmation.");

                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Failed to create Terraform plan for request '{requestId}': {ex.Message}");
                return false;
            }
            finally
            {
                // The working directory holds terraform.tfvars - every resolved deployment
                // property in plain text. Deleting it only on the success path left it behind
                // precisely when a deployment failed, which is when a host is most likely to
                // be looked at by someone other than the deployment account.
                DeleteTempTerraformFolder(terraformWorkingDir);
            }
        }

        /// <summary>
        /// Creates the working directory and returns its path. Separate from provisioning so
        /// that the caller records the path before anything can be written into it, and can
        /// therefore delete it whatever provisioning goes on to do.
        /// </summary>
        private static string CreateTerraformWorkingDirectory(int requestId)
        {
            var workingDir = Path.Join(
                DorcProgramData.Root,
                "terraform-workdir",
                $"{requestId}-terraform-{DateTime.UtcNow:yyyy-MM-dd-HH-mm-ss}");

            RestrictedWorkingDirectory.Create(workingDir);

            return workingDir;
        }

        private async Task ProvisionTerraformWorkingDirectoryAsync(
            string workingDir,
            ScriptGroup scriptGroup,
            CancellationToken cancellationToken)
        {
            // Get the appropriate provider for the source type
            var provider = _codeSourceFactory.GetProvider(scriptGroup.TerraformSourceType);
            
            logger.Information($"Using Terraform source type: {scriptGroup.TerraformSourceType}");
            
            // Provision the code using the selected provider
            await provider.ProvisionCodeAsync(scriptGroup, workingDir, cancellationToken);

            // If a sub-path is specified, move only that directory to the root
            if (!string.IsNullOrEmpty(scriptGroup.TerraformSubPath))
            {
                await DirectoryHelper.ExtractSubPathAsync(workingDir, scriptGroup.TerraformSubPath, cancellationToken);
                logger.FileLogger.LogInformation($"Successfully extracted path {scriptGroup.TerraformSubPath}");
            }

            logger.Information($"Terraform working directory has been set up at: {workingDir}");
        }

        private async Task<string> CreateTerraformPlanAsync(
            IDictionary<string, VariableValue> properties,
            string terraformWorkingDir,
            string resultFilePath,
            string planContentFilePath,
            int requestId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
                        
            try
            {
                // Initialize Terraform if needed
                await RunTerraformCommandAsync(terraformWorkingDir, "init  -no-color", cancellationToken);
                
                // Create Terraform variables file
                await CreateTerraformVariablesFileAsync(terraformWorkingDir, properties, cancellationToken);
                
                // Generate the plan
                var planArgs = $"plan -out={resultFilePath} -detailed-exitcode -no-color";
                
                var planResult = await RunTerraformCommandAsync(terraformWorkingDir, planArgs, cancellationToken);
                
                // Get human-readable plan output
                var showArgs = $"show {resultFilePath} -no-color";
                var planContent = await RunTerraformCommandAsync(terraformWorkingDir, showArgs, cancellationToken);
                if (!String.IsNullOrEmpty(planContent))
                {
                    File.WriteAllText(planContentFilePath, planContent);
                }
                
                logger.Information($"Terraform plan created successfully for request '{requestId}'");
                return planContent;
            }
            finally
            {
                logger.FlushLogMessages();
            }
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

                // Only [a-zA-Z0-9_] symbols can be used in Terraform identifiers. Replace all others with '_'
                var propertyName = Regex.Replace(property.Key, "[^a-zA-Z0-9_]", "");

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

            var tfCommand = arguments.Split(' ')[0];

            logger.Debug($"Running Terraform command: terraform {tfCommand}");

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
            catch (Win32Exception ex) when (ex.NativeErrorCode == 2)
            {
                var errorMessage = "Terraform executable not found. Please ensure Terraform is installed and available in the system PATH.";
                logger.Error(errorMessage, ex);
                throw new InvalidOperationException(errorMessage, ex);
            }
            catch (Win32Exception ex)
            {
                var errorMessage = $"Failed to start Terraform process. Win32 error code: {ex.NativeErrorCode}. " +
                                   "Please ensure Terraform is properly installed and the user has necessary permissions.";
                logger.Error(errorMessage, ex);
                throw new InvalidOperationException(errorMessage, ex);
            }
            catch (OperationCanceledException)
            {
                logger.Warning($"Terraform command {tfCommand} was cancelled.");
                throw;
            }
            catch (Exception e)
            {
                logger.Error($"Running of the Terraform process failed. Arguments: {arguments} in {workingDir}", e);
                throw;
            }

            var output = outputBuilder.ToString();
            var error = errorBuilder.ToString();

            if (process.ExitCode == 1)
            {
                var errorMessage = $"Terraform command {tfCommand} failed with exit code {process.ExitCode}";
                logger.Error($"{errorMessage}. Error: {error}");
                throw new InvalidOperationException(errorMessage);
            }

            // Command stdout is not logged. "terraform show" renders variable values, which
            // include the resolved deployment property set, so echoing output here wrote
            // secrets into the runner log - whose path is published on the deployment
            // request. The command and its outcome are enough for diagnosis; the plan itself
            // remains available through the API to callers authorised for the environment.
            logger.Information($"Terraform command {tfCommand} completed successfully ({output?.Length ?? 0} characters of output, not logged).");
            return output;
        }

        public async Task<bool> ExecuteConfirmedPlanAsync(
            string pipeName,
            int requestId,
            string planFile,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ScriptGroup scriptGroupProperties = this._scriptGroupPipeClient.GetScriptGroupProperties(pipeName);
            var deployResultId = scriptGroupProperties.DeployResultId;

            this.logger.SetRequestId(requestId);
            this.logger.SetDeploymentResultId(deployResultId);

            logger.Information($"TerraformProcessor.ExecuteConfirmedPlan called for request' with id '{requestId}', deployment result id '{deployResultId}'.");

            // Execute the actual Terraform plan
            return await ExecuteTerraformPlanAsync(requestId, planFile, scriptGroupProperties, cancellationToken);
        }

        private async Task<bool> ExecuteTerraformPlanAsync(
            int requestId,
            string planFile,
            ScriptGroup scriptGroup,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var terraformWorkingDir = string.Empty;
            try
            {
                terraformWorkingDir = CreateTerraformWorkingDirectory(requestId);
                await ProvisionTerraformWorkingDirectoryAsync(terraformWorkingDir, scriptGroup, cancellationToken);

                // Initialize Terraform if needed
                await RunTerraformCommandAsync(terraformWorkingDir, "init  -no-color", cancellationToken);

                // Execute terraform apply using the stored plan
                var applyArgs = $"apply -auto-approve {planFile}  -no-color";
                await RunTerraformCommandAsync(terraformWorkingDir, applyArgs, cancellationToken);

                logger.Information($"Terraform apply completed successfully for request ID: {requestId}");

                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Terraform apply failed for request ID {requestId}: {ex.Message}");
                return false;
            }
            finally
            {
                DeleteTempTerraformFolder(terraformWorkingDir);
            }
        }

        private void DeleteTempTerraformFolder(string folderPath)
        {
            if (String.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                return;

            try
            {
                DirectoryHelper.SafeRemoveDirectory(folderPath);
            }
            // Invoked from a finally block. A directory that will not delete is worth
            // recording - it is deployment properties left on disk - but it must not displace
            // the exception that caused the deployment to fail. SafeRemoveDirectory has
            // already retried and wrapped whatever it could not delete in an IOException.
            catch (IOException ex) { LogUndeletedWorkingDirectory(ex, folderPath); }
            catch (UnauthorizedAccessException ex) { LogUndeletedWorkingDirectory(ex, folderPath); }
        }

        private void LogUndeletedWorkingDirectory(Exception ex, string folderPath) =>
            logger.Error(ex, $"Failed to remove the Terraform working directory '{folderPath}': {ex.Message}");

        private class TerraformExecutionResult
        {
            public bool Success { get; set; }
            public string? Output { get; set; }
            public string? ErrorMessage { get; set; }
        }
    }
}
