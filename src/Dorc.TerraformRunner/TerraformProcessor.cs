using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Runner.Logger;
using Dorc.TerraformRunner.CodeSources;
using Dorc.TerraformRunner.Pipes;
using Microsoft.Extensions.Logging;
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
            
            var terraformWorkingDir = await SetupTerraformWorkingDirectoryAsync(requestId, scriptGroupProperties, cancellationToken);

            try
            {
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
                DeleteTempTerraformFolder(terraformWorkingDir);
            }
        }

        private async Task<string> SetupTerraformWorkingDirectoryAsync(
            int requestId,
            ScriptGroup scriptGroup,
            CancellationToken cancellationToken)
        {
            // Create a unique working directory for this deployment
            var workingDir = Path.Combine(
                Path.GetTempPath(),
                "terraform-workdir",
                $"{requestId}-terraform-{DateTime.UtcNow:yyyy-MM-dd-HH-mm-ss}");

            Directory.CreateDirectory(workingDir);

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

            return workingDir;
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
                logger.Error($"Running of the Terraform process failed. Arguments: {arguments} in {workingDir}", e);
                throw;
            }

            var output = outputBuilder.ToString();
            var error = errorBuilder.ToString();

            if (process.ExitCode == 1)
            {
                var errorMessage = $"Terraform command failed with exit code {process.ExitCode}. Error: {error}";
                logger.Error(errorMessage);
                throw new InvalidOperationException(errorMessage);
            }

            logger.Information($"Terraform command completed successfully. Output:{Environment.NewLine}{output}");
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

            try
            {
                // Execute the actual Terraform plan
                var executionResult = await ExecuteTerraformPlanAsync(requestId, planFile, scriptGroupProperties, cancellationToken);

                if (executionResult.Success)
                {
                    logger.Information($"Terraform plan executed successfully for deployment result ID: {deployResultId}");
                    return true;
                }
                else
                {
                    logger.Error($"Terraform plan execution failed for deployment result ID {deployResultId}: {executionResult.ErrorMessage}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Failed to execute Terraform plan for deployment result ID {deployResultId}: {ex.Message}");
                return false;
            }
        }

        private async Task<TerraformExecutionResult> ExecuteTerraformPlanAsync(
            int requestId,
            string planFile,
            ScriptGroup scriptGroup,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var terraformWorkingDir = string.Empty;
            try
            {
                terraformWorkingDir = await SetupTerraformWorkingDirectoryAsync(requestId, scriptGroup, cancellationToken);

                // Initialize Terraform if needed
                await RunTerraformCommandAsync(terraformWorkingDir, "init  -no-color", cancellationToken);

                // Execute terraform apply using the stored plan
                var applyArgs = $"apply -auto-approve {planFile}  -no-color";
                logger.Information($"Executing Terraform apply for request ID: {requestId}");
                
                var applyOutput = await RunTerraformCommandAsync(terraformWorkingDir, applyArgs, cancellationToken);

                logger.Information($"Terraform apply completed successfully for request ID: {requestId}");
                
                return new TerraformExecutionResult 
                { 
                    Success = true, 
                    Output = applyOutput 
                };
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Terraform apply failed for request ID {requestId}: {ex.Message}");
                return new TerraformExecutionResult 
                { 
                    Success = false, 
                    ErrorMessage = ex.Message,
                    Output = ex.StackTrace 
                };
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

            DirectoryHelper.SafeRemoveDirectory(folderPath);
        }

        private class TerraformExecutionResult
        {
            public bool Success { get; set; }
            public string? Output { get; set; }
            public string? ErrorMessage { get; set; }
        }
    }
}
