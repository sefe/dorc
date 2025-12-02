using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
//using Dorc.PersistentData.Sources.Interfaces;
using Dorc.Runner.Logger;
using Dorc.TerraformmRunner.Pipes;
using Microsoft.Extensions.Logging;
using System.Net.Http;

//using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Dorc.TerraformmRunner
{
    internal class TerraformProcessor : ITerraformProcessor
    {
        private readonly IRunnerLogger logger;
        private readonly IScriptGroupPipeClient _scriptGroupPipeClient;
        //private readonly IRequestsPersistentSource requestsPersistentSource;

        public TerraformProcessor(
            IRunnerLogger logger,
            IScriptGroupPipeClient scriptGroupPipeClient)
            //IRequestsPersistentSource requestsPersistentSource)
        {
            this.logger = logger;
            this._scriptGroupPipeClient = scriptGroupPipeClient;
            //this.requestsPersistentSource = requestsPersistentSource;
        }

        public async Task<bool> PreparePlanAsync(
            string pipeName,
            int requestId,
            string scriptPath,
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

            logger.FileLogger.LogInformation($"TerraformProcessor.PreparePlan called for request' with id '{requestId}', deployment result id '{deployResultId}'.");
            
            var terraformWorkingDir = await SetupTerraformWorkingDirectoryAsync(requestId, scriptPath, scriptGroupProperties, cancellationToken);

            try
            {
                // Create terraform plan (placeholder implementation)
                var planContent = await CreateTerraformPlanAsync(properties, terraformWorkingDir, resultFilePath, planContentFilePath, requestId, cancellationToken);

                logger.FileLogger.LogInformation($"Terraform plan created for request '{requestId}'. Waiting for confirmation.");
                return true;
            }
            catch (Exception ex)
            {
                logger.FileLogger.LogError(ex, $"Failed to create Terraform plan for request '{requestId}': {ex.Message}");
                return false;
            }
            finally
            {
                DeleteTempTerraformFolder(terraformWorkingDir);
            }
        }

        private async Task<string> SetupTerraformWorkingDirectoryAsync(
            int requestId,
            string scriptPath,
            ScriptGroup scriptGroup,
            CancellationToken cancellationToken)
        {
            // Create a unique working directory for this deployment
            var workingDir = Path.Combine(
                Path.GetTempPath(),
                "terraform-workdir",
                $"{requestId}-terraform-{DateTime.UtcNow:yyyy-MM-dd-HH-mm-ss}");

            Directory.CreateDirectory(workingDir);

            // Handle different Terraform source types
            switch (scriptGroup.TerraformSourceType)
            {
                case TerraformSourceType.Git:
                    await CloneGitRepositoryAsync(scriptGroup, workingDir, cancellationToken);
                    break;

                case TerraformSourceType.AzureArtifact:
                    await DownloadAzureArtifactAsync(scriptGroup, workingDir, cancellationToken);
                    break;

                case TerraformSourceType.SharedFolder:
                default:
                    // Copy Terraform files from component script path to working directory
                    if (!string.IsNullOrEmpty(scriptPath) && Directory.Exists(scriptPath))
                    {
                        logger.FileLogger.LogInformation($"Copying Terraform files from '{scriptPath}' to working directory '{workingDir}'");
                        await CopyDirectoryAsync(scriptPath, workingDir, cancellationToken);
                    }
                    else
                    {
                        throw new InvalidOperationException($"Terraform script path '{scriptPath}' does not exist.");
                    }
                    break;
            }

            return workingDir;
        }

        private async Task CloneGitRepositoryAsync(
            ScriptGroup scriptGroup,
            string workingDir,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(scriptGroup.TerraformGitRepoUrl))
            {
                throw new InvalidOperationException("Git repository URL is not configured.");
            }

            // Validate and sanitize branch name to prevent command injection
            var branchName = SanitizeGitParameter(scriptGroup.TerraformGitBranch ?? "main");
            
            logger.FileLogger.LogInformation($"Cloning Git repository '{scriptGroup.TerraformGitRepoUrl}' branch '{branchName}'");

            // Determine if this is GitHub or Azure DevOps
            bool isGitHub = scriptGroup.TerraformGitRepoUrl.Contains("github.com", StringComparison.OrdinalIgnoreCase);
            bool isAzureDevOps = scriptGroup.TerraformGitRepoUrl.Contains("dev.azure.com", StringComparison.OrdinalIgnoreCase) ||
                                 scriptGroup.TerraformGitRepoUrl.Contains("visualstudio.com", StringComparison.OrdinalIgnoreCase);

            string repoUrl = InjectGitCredentials(scriptGroup, isGitHub, isAzureDevOps);

            // Use git command to clone the repository with validated parameters
            var cloneArgs = $"clone --branch \"{branchName}\" --depth 1 \"{repoUrl}\" \"{workingDir}\"";
            
            try
            {
                await RunCommandAsync("git", cloneArgs, Path.GetTempPath(), cancellationToken, sanitizeForLog: true);
            }
            catch (Exception ex)
            {
                logger.FileLogger.LogError(ex, $"Failed to clone Git repository: {ex.Message}");
                throw new InvalidOperationException($"Failed to clone Git repository: {ex.Message}", ex);
            }

            // If a sub-path is specified, move only that directory to the root
            if (!string.IsNullOrEmpty(scriptGroup.TerraformSubPath))
            {
                var subPathDir = Path.Combine(workingDir, scriptGroup.TerraformSubPath);
                if (Directory.Exists(subPathDir))
                {
                    var tempDir = Path.Combine(Path.GetTempPath(), $"terraform-temp-{Guid.NewGuid()}");
                    Directory.Move(workingDir, tempDir);
                    Directory.CreateDirectory(workingDir);
                    
                    var subPathInTemp = Path.Combine(tempDir, scriptGroup.TerraformSubPath);
                    await CopyDirectoryAsync(subPathInTemp, workingDir, cancellationToken);
                    
                    // Clean up temp directory
                    Directory.Delete(tempDir, true);
                }
                else
                {
                    logger.FileLogger.LogWarning($"Terraform sub-path '{scriptGroup.TerraformSubPath}' not found in repository.");
                }
            }

            logger.FileLogger.LogInformation($"Successfully cloned Git repository to '{workingDir}'");
        }

        private string InjectGitCredentials(ScriptGroup scriptGroup, bool isGitHub, bool isAzureDevOps)
        {
            string repoUrl = scriptGroup.TerraformGitRepoUrl;

            // Inject credentials into URL if available
            if (!string.IsNullOrEmpty(scriptGroup.TerraformGitPat))
            {
                var uri = new Uri(repoUrl);
                if (isGitHub)
                {
                    // For GitHub: https://[PAT]@github.com/...
                    repoUrl = $"{uri.Scheme}://{Uri.EscapeDataString(scriptGroup.TerraformGitPat)}@{uri.Host}{uri.PathAndQuery}";
                }
                else if (isAzureDevOps)
                {
                    // For Azure DevOps: https://[PAT]@dev.azure.com/...
                    repoUrl = $"{uri.Scheme}://{Uri.EscapeDataString(scriptGroup.TerraformGitPat)}@{uri.Host}{uri.PathAndQuery}";
                }
            }
            else if (isAzureDevOps && !string.IsNullOrEmpty(scriptGroup.AzureBearerToken))
            {
                // For Azure DevOps with bearer token: use PAT format with token
                var uri = new Uri(repoUrl);
                repoUrl = $"{uri.Scheme}://{Uri.EscapeDataString(scriptGroup.AzureBearerToken)}@{uri.Host}{uri.PathAndQuery}";
            }

            return repoUrl;
        }

        private string SanitizeGitParameter(string parameter)
        {
            // Only allow alphanumeric characters, hyphens, underscores, forward slashes, and dots
            // This prevents command injection while allowing valid branch names
            if (string.IsNullOrEmpty(parameter))
            {
                return "main";
            }

            var sanitized = Regex.Replace(parameter, @"[^a-zA-Z0-9\-_/\.]", "");
            
            if (string.IsNullOrEmpty(sanitized))
            {
                throw new InvalidOperationException($"Invalid branch name: '{parameter}'");
            }

            return sanitized;
        }

        private async Task DownloadAzureArtifactAsync(
            ScriptGroup scriptGroup,
            string workingDir,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(scriptGroup.AzureOrganization) || 
                string.IsNullOrEmpty(scriptGroup.AzureProject) ||
                string.IsNullOrEmpty(scriptGroup.AzureBuildId))
            {
                throw new InvalidOperationException("Azure DevOps artifact information is not configured.");
            }

            logger.FileLogger.LogInformation($"Downloading Azure artifact from build '{scriptGroup.AzureBuildId}' in project '{scriptGroup.AzureProject}'");

            // Use Azure DevOps REST API to download artifacts
            var artifactUrl = $"https://dev.azure.com/{scriptGroup.AzureOrganization}/{scriptGroup.AzureProject}/_apis/build/builds/{scriptGroup.AzureBuildId}/artifacts?api-version=6.0";
            
            using (var httpClient = new HttpClient())
            {
                if (!string.IsNullOrEmpty(scriptGroup.AzureBearerToken))
                {
                    httpClient.DefaultRequestHeaders.Authorization = 
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", scriptGroup.AzureBearerToken);
                }

                try
                {
                    var response = await httpClient.GetAsync(artifactUrl, cancellationToken);
                    response.EnsureSuccessStatusCode();
                    
                    var content = await response.Content.ReadAsStringAsync();
                    logger.FileLogger.LogInformation($"Retrieved artifact list from Azure DevOps");
                    
                    // Parse the response and download the appropriate artifact
                    // This is a simplified implementation - in production you'd want to:
                    // 1. Parse the JSON response to find the right artifact
                    // 2. Download and extract the artifact
                    // 3. Extract to the working directory
                    
                    // For now, log a message indicating this needs more implementation
                    logger.FileLogger.LogWarning("Azure artifact download is not fully implemented yet. This requires parsing the artifact list and downloading the correct artifact.");
                    throw new NotImplementedException("Azure artifact download feature is not yet fully implemented.");
                }
                catch (Exception ex)
                {
                    logger.FileLogger.LogError(ex, $"Failed to download Azure artifact: {ex.Message}");
                    throw new InvalidOperationException($"Failed to download Azure artifact: {ex.Message}", ex);
                }
            }
        }

        private async Task<string> RunCommandAsync(
            string fileName,
            string arguments,
            string workingDir,
            CancellationToken cancellationToken,
            bool sanitizeForLog = false)
        {
            using var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = fileName;
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

            // Sanitize arguments for logging if they contain credentials
            var logArguments = sanitizeForLog ? SanitizeArgumentsForLogging(arguments) : arguments;
            logger.FileLogger.LogDebug($"Running command: {fileName} {logArguments} in {workingDir}");

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
                logger.Error($"Running command failed: {fileName} {logArguments} in {workingDir}", e);
                throw;
            }

            var output = outputBuilder.ToString();
            var error = errorBuilder.ToString();

            if (process.ExitCode != 0)
            {
                var errorMessage = $"Command failed with exit code {process.ExitCode}. Error: {error}";
                logger.Error(errorMessage);
                throw new InvalidOperationException(errorMessage);
            }

            logger.Information($"Command completed successfully. Output:{Environment.NewLine}{output}");
            return output;
        }

        private string SanitizeArgumentsForLogging(string arguments)
        {
            // Replace any URLs containing credentials with sanitized versions
            // Pattern matches: https://[anything]@[domain]
            return Regex.Replace(arguments, @"https://[^@]+@([a-zA-Z0-9\-\.]+)", "https://***@$1");
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
                
                logger.FileLogger.LogInformation($"Terraform plan created successfully for request '{requestId}'");
                return planContent;
            }
            catch (Exception ex)
            {
                logger.FileLogger.LogError(ex, $"Failed to create Terraform plan for request '{requestId}': {ex.Message}");
                throw;
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

            logger.FileLogger.LogDebug($"Running Terraform command: terraform {arguments} in {workingDir}");

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
            string scriptPath,
            string planFile,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ScriptGroup scriptGroupProperties = this._scriptGroupPipeClient.GetScriptGroupProperties(pipeName);
            var deployResultId = scriptGroupProperties.DeployResultId;

            this.logger.SetRequestId(requestId);
            this.logger.SetDeploymentResultId(deployResultId);

            logger.FileLogger.LogInformation($"TerraformProcessor.ExecuteConfirmedPlan called for request' with id '{requestId}', deployment result id '{deployResultId}'.");

            try
            {

                // Execute the actual Terraform plan
                var executionResult = await ExecuteTerraformPlanAsync(requestId, scriptPath, planFile, scriptGroupProperties, cancellationToken);

                if (executionResult.Success)
                {

                    logger.FileLogger.LogInformation($"Terraform plan executed successfully for deployment result ID: {deployResultId}");
                    return true;
                }
                else
                {

                    logger.FileLogger.LogError($"Terraform plan execution failed for deployment result ID {deployResultId}: {executionResult.ErrorMessage}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                logger.FileLogger.LogError(ex, $"Failed to execute Terraform plan for deployment result ID {deployResultId}: {ex.Message}");
                return false;
            }
        }

        private async Task<TerraformExecutionResult> ExecuteTerraformPlanAsync(
            int requestId,
            string scriptPath,
            string planFile,
            ScriptGroup scriptGroup,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var terraformWorkingDir = string.Empty;
            try
            {
                terraformWorkingDir = await SetupTerraformWorkingDirectoryAsync(requestId, scriptPath, scriptGroup, cancellationToken);

                // Initialize Terraform if needed
                await RunTerraformCommandAsync(terraformWorkingDir, "init  -no-color", cancellationToken);

                // Execute terraform apply using the stored plan
                var applyArgs = $"apply -auto-approve {planFile}  -no-color";
                logger.FileLogger.LogInformation($"Executing Terraform apply for request ID: {requestId}");
                
                var applyOutput = await RunTerraformCommandAsync(terraformWorkingDir, applyArgs, cancellationToken);

                logger.FileLogger.LogInformation($"Terraform apply completed successfully for request ID: {requestId}");
                
                return new TerraformExecutionResult 
                { 
                    Success = true, 
                    Output = applyOutput 
                };
            }
            catch (Exception ex)
            {
                logger.FileLogger.LogError(ex, $"Terraform apply failed for request ID {requestId}: {ex.Message}");
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

            Directory.Delete(folderPath, true);
        }

        private class TerraformExecutionResult
        {
            public bool Success { get; set; }
            public string? Output { get; set; }
            public string? ErrorMessage { get; set; }
        }
    }
}