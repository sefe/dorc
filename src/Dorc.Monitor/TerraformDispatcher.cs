using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.PersistentData.Sources.Interfaces;
using log4net;
using System.Diagnostics;
using System.Text;

namespace Dorc.Monitor
{
    public class TerraformDispatcher : ITerraformDispatcher
    {
        private readonly ILog logger;
        private readonly IRequestsPersistentSource requestsPersistentSource;
        private readonly IConfigValuesPersistentSource configValuesPersistentSource;

        public TerraformDispatcher(
            ILog logger,
            IRequestsPersistentSource requestsPersistentSource,
            IConfigValuesPersistentSource configValuesPersistentSource)
        {
            this.logger = logger;
            this.requestsPersistentSource = requestsPersistentSource;
            this.configValuesPersistentSource = configValuesPersistentSource;
        }

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

            try
            {
                // Update status to Running
                requestsPersistentSource.UpdateResultStatus(
                    deploymentResult,
                    DeploymentResultStatus.Running);

                logger.Info($"Updated deployment result {deploymentResult.Id} status to Running.");

                // Create terraform plan (placeholder implementation)
                var planContent = await CreateTerraformPlanAsync(component, properties, environmentName, cancellationToken);
                
                // Save plan to blob storage (placeholder implementation)
                var blobUrl = await SavePlanToBlobStorageAsync(planContent, deploymentResult.Id, cancellationToken);

                resultLogBuilder.AppendLine($"Terraform plan created successfully for component '{component.ComponentName}'");
                resultLogBuilder.AppendLine($"Plan stored at: {blobUrl}");

                // Update status to WaitingConfirmation
                requestsPersistentSource.UpdateResultStatus(
                    deploymentResult,
                    DeploymentResultStatus.WaitingConfirmation);

                logger.Info($"Terraform plan created for component '{component.ComponentName}'. Waiting for confirmation.");
                return true;
            }
            catch (Exception ex)
            {
                logger.Error($"Failed to create Terraform plan for component '{component.ComponentName}': {ex.Message}", ex);
                resultLogBuilder.AppendLine($"Failed to create Terraform plan: {ex.Message}");
                return false;
            }
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
            var scriptRoot = configValuesPersistentSource.GetConfigValue("ScriptRoot");
            
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
                    value = $"\"{value.Replace("\"", "\\\"")}\"";
                }
                else if (property.Value.Type == typeof(bool))
                {
                    value = value.ToLowerInvariant();
                }
                
                variablesContent.AppendLine($"{property.Key} = {value}");
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

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Wait for the process to complete or be cancelled
            while (!process.HasExited)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(100, cancellationToken);
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
                deploymentResult = requestsPersistentSource.GetDeploymentResults(deploymentResultId);
                if (deploymentResult == null)
                {
                    throw new InvalidOperationException($"Deployment result with ID {deploymentResultId} not found");
                }

                if (deploymentResult.Status != DeploymentResultStatus.Confirmed.ToString())
                {
                    throw new InvalidOperationException($"Deployment result {deploymentResultId} is not in Confirmed status. Current status: {deploymentResult.Status}");
                }

                // Update status to Running
                requestsPersistentSource.UpdateResultStatus(
                    deploymentResult,
                    DeploymentResultStatus.Running);

                // Execute the actual Terraform plan
                var executionResult = await ExecuteTerraformPlanAsync(deploymentResultId, cancellationToken);

                if (executionResult.Success)
                {
                    // Update status to Complete
                    requestsPersistentSource.UpdateResultStatus(
                        deploymentResult,
                        DeploymentResultStatus.Complete);

                    logger.Info($"Terraform plan executed successfully for deployment result ID: {deploymentResultId}");
                    return true;
                }
                else
                {
                    // Update status to Failed
                    requestsPersistentSource.UpdateResultStatus(
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
                    requestsPersistentSource.UpdateResultStatus(
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