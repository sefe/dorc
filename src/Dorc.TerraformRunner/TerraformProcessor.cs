using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Runner.Logger;
using Dorc.Terraform.Catalog;
using Dorc.TerraformRunner.CodeSources;
using Dorc.TerraformRunner.Pipes;
using Dorc.TerraformRunner.State;
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
            IScriptGroupPipeClient scriptGroupPipeClient,
            ITemplateCatalog catalog)
        {
            this.logger = logger;
            this._scriptGroupPipeClient = scriptGroupPipeClient;
            this._codeSourceFactory = new TerraformCodeSourceProviderFactory(logger, catalog);
        }

        public async Task<bool> PreparePlanAsync(
            string pipeName,
            int requestId,
            string resultFilePath,
            string planContentFilePath,
            string? lockFilePath,
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

                // S-006b: persist .terraform.lock.hcl alongside the plan binary
                // so the apply phase resolves identical provider versions. The
                // dispatcher uploads this to blob storage; the apply path
                // restores it into the working dir before init.
                if (!string.IsNullOrEmpty(lockFilePath))
                {
                    PersistLockFile(terraformWorkingDir, lockFilePath);
                }

                logger.Information($"Terraform plan created for request '{requestId}'. Waiting for confirmation.");
                DeleteTempTerraformFolder(terraformWorkingDir);

                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Failed to create Terraform plan for request '{requestId}': {ex.Message}");
                return false;
            }
        }

        // S-006b helpers. Per-operation execution-bundle persistence is bound
        // to .terraform.lock.hcl only at this stage; full .terraform/ tarball
        // + SHA-256 verification is the follow-up under the consolidated
        // lifecycle owner (S-006d).
        private void PersistLockFile(string workingDir, string lockFilePath)
        {
            // Defence-in-depth: reject `..` segments before composing paths.
            // Both inputs are platform-supplied today, but the rejection is
            // documented contract that matches DOrc's path-traversal posture.
            if (workingDir.Contains("..") || lockFilePath.Contains(".."))
            {
                throw new ArgumentException("paths must not contain parent-directory segments");
            }
            var source = Path.Join(workingDir, ".terraform.lock.hcl");
            if (!File.Exists(source))
            {
                logger.Warning(
                    $".terraform.lock.hcl not found in working dir; lock-file persistence skipped (no provider lock to record).");
                return;
            }
            var destDir = Path.GetDirectoryName(lockFilePath);
            if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);
            File.Copy(source, lockFilePath, true);
            logger.FileLogger.LogInformation($"Persisted .terraform.lock.hcl to {lockFilePath}");
        }

        private void RestoreLockFile(string workingDir, string lockFilePath)
        {
            if (workingDir.Contains("..") || (lockFilePath?.Contains("..") ?? false))
            {
                throw new ArgumentException("paths must not contain parent-directory segments");
            }
            if (string.IsNullOrEmpty(lockFilePath) || !File.Exists(lockFilePath))
            {
                logger.Warning(
                    $"Persisted .terraform.lock.hcl not found at '{lockFilePath}'; apply will resolve provider versions afresh.");
                return;
            }
            var dest = Path.Join(workingDir, ".terraform.lock.hcl");
            File.Copy(lockFilePath, dest, true);
            logger.FileLogger.LogInformation(
                $"Restored .terraform.lock.hcl into working dir from {lockFilePath}");
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
                await TerraformSourceSubPath.ApplyAsync(workingDir, scriptGroup.TerraformSubPath, cancellationToken);
                logger.FileLogger.LogInformation($"Successfully extracted path {scriptGroup.TerraformSubPath}");
            }

            // Reject any user-checked-in `terraform { backend ... }` block before
            // we render the platform backend; DOrc owns the backend (HLPS SC-01).
            // The validator throws with a precise error string identifying the
            // offending file when the engineer must remove the declaration.
            TerraformBackendValidator.RejectIfUserBackendBlocksPresent(workingDir);

            // Render the platform-managed backend if the dispatcher provided one.
            // Empty TerraformStateKey is the legacy path (no backend rendered);
            // this preserves backward compatibility until the consolidated
            // lifecycle is the default.
            if (!string.IsNullOrEmpty(scriptGroup.TerraformStateKey)
                && !string.IsNullOrEmpty(scriptGroup.TerraformStateStorageAccount)
                && !string.IsNullOrEmpty(scriptGroup.TerraformStateContainerName))
            {
                TerraformBackendRenderer.WriteToWorkingDirectory(
                    workingDir,
                    new TerraformBackendRenderer.AzureBlobBackend(
                        StorageAccount: scriptGroup.TerraformStateStorageAccount,
                        ContainerName: scriptGroup.TerraformStateContainerName,
                        Key: scriptGroup.TerraformStateKey,
                        ResourceGroup: scriptGroup.TerraformStateResourceGroup));
                logger.FileLogger.LogInformation(
                    $"Rendered platform backend (key={scriptGroup.TerraformStateKey})");
            }

            logger.Information($"Terraform working directory has been set up at: {workingDir}");

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
                await RunTerraformCommandAsync(
                    terraformWorkingDir,
                    TerraformCommand.Init,
                    new[] { "init", "-no-color" },
                    cancellationToken);

                await CreateTerraformVariablesFileAsync(terraformWorkingDir, properties, cancellationToken);

                await RunTerraformCommandAsync(
                    terraformWorkingDir,
                    TerraformCommand.PlanDetailedExitCode,
                    new[] { "plan", $"-out={resultFilePath}", "-detailed-exitcode", "-no-color" },
                    cancellationToken);

                var planContent = await RunTerraformCommandAsync(
                    terraformWorkingDir,
                    TerraformCommand.Show,
                    new[] { "show", resultFilePath, "-no-color" },
                    cancellationToken);
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
            TerraformCommand command,
            IReadOnlyList<string> commandArgs,
            CancellationToken cancellationToken)
        {
            using var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = "terraform";
            // ArgumentList passes each argument as a discrete value; the runtime
            // applies platform-correct quoting. This eliminates the prior
            // "Arguments = string concatenation" injection surface where paths
            // with spaces or shell metacharacters could split into extra tokens.
            foreach (var arg in commandArgs) process.StartInfo.ArgumentList.Add(arg);
            process.StartInfo.WorkingDirectory = workingDir;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.CreateNoWindow = true;

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    outputBuilder.AppendLine(e.Data);
                }
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    errorBuilder.AppendLine(e.Data);
                }
            };

            logger.Debug($"Running Terraform command: terraform {command}");

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                try
                {
                    await process.WaitForExitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            // Kill the entire tree so any terraform-spawned
                            // provider plugins are also reaped.
                            process.Kill(entireProcessTree: true);
                        }
                    }
                    catch (InvalidOperationException killEx)
                    {
                        // Process exited between HasExited check and Kill - benign.
                        logger.Debug($"Process exited before kill could fire: {killEx.Message}");
                    }
                    catch (Win32Exception killEx)
                    {
                        // OS-level kill failure (e.g. permissions). Log but do
                        // not re-throw so the cancellation flow proceeds.
                        logger.Warning($"OS-level kill failed for terraform process: {killEx.Message}");
                    }
                    logger.Warning($"Terraform command {command} was cancelled; killed process tree.");
                    throw;
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
                throw;
            }
            catch (Exception e)
            {
                logger.Error($"Running of the Terraform process failed. Command: {command}, args: [{string.Join(" ", commandArgs)}] in {workingDir}", e);
                throw;
            }

            var output = outputBuilder.ToString();
            var error = errorBuilder.ToString();

            InterpretExitCode(command, process.ExitCode, error);

            logger.Information($"Terraform command {command} completed successfully. Output:{Environment.NewLine}{output}");
            return output;
        }

        // Per-command exit-code semantics. Documented at SC-03 of the
        // terraform-hardening HLPS.
        //
        // - PlanDetailedExitCode: 0 = no changes, 1 = error, 2 = changes
        //   pending (success). Anything not 0/1/2 is also treated as an
        //   error so we don't silently accept unknown codes.
        // - Init / Apply / Show: any non-zero exit code is an error.
        private void InterpretExitCode(TerraformCommand command, int exitCode, string errorOutput)
        {
            if (command == TerraformCommand.PlanDetailedExitCode)
            {
                if (exitCode == 0 || exitCode == 2) return;
                var planMsg = $"terraform plan failed with exit code {exitCode}";
                logger.Error($"{planMsg}. Error: {errorOutput}");
                throw new InvalidOperationException(planMsg);
            }

            if (exitCode != 0)
            {
                var msg = $"terraform {command} failed with exit code {exitCode}";
                logger.Error($"{msg}. Error: {errorOutput}");
                throw new InvalidOperationException(msg);
            }
        }

        public async Task<bool> ExecuteConfirmedPlanAsync(
            string pipeName,
            int requestId,
            string planFile,
            string? lockFilePath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ScriptGroup scriptGroupProperties = this._scriptGroupPipeClient.GetScriptGroupProperties(pipeName);
            var deployResultId = scriptGroupProperties.DeployResultId;

            this.logger.SetRequestId(requestId);
            this.logger.SetDeploymentResultId(deployResultId);

            logger.Information($"TerraformProcessor.ExecuteConfirmedPlan called for request' with id '{requestId}', deployment result id '{deployResultId}'.");

            // Execute the actual Terraform plan
            return await ExecuteTerraformPlanAsync(requestId, planFile, lockFilePath, scriptGroupProperties, cancellationToken);
        }

        private async Task<bool> ExecuteTerraformPlanAsync(
            int requestId,
            string planFile,
            string? lockFilePath,
            ScriptGroup scriptGroup,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var terraformWorkingDir = string.Empty;
            try
            {
                terraformWorkingDir = await SetupTerraformWorkingDirectoryAsync(requestId, scriptGroup, cancellationToken);

                // S-006b: restore the persisted .terraform.lock.hcl into the
                // working dir before init so apply resolves identical provider
                // versions to the plan run.
                if (!string.IsNullOrEmpty(lockFilePath))
                {
                    RestoreLockFile(terraformWorkingDir, lockFilePath);
                }

                await RunTerraformCommandAsync(
                    terraformWorkingDir,
                    TerraformCommand.Init,
                    new[] { "init", "-no-color" },
                    cancellationToken);

                await RunTerraformCommandAsync(
                    terraformWorkingDir,
                    TerraformCommand.Apply,
                    new[] { "apply", "-auto-approve", planFile, "-no-color" },
                    cancellationToken);

                logger.Information($"Terraform apply completed successfully for request ID: {requestId}");

                DeleteTempTerraformFolder(terraformWorkingDir);

                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Terraform apply failed for request ID {requestId}: {ex.Message}");
                return false;
            }
        }

        private void DeleteTempTerraformFolder(string folderPath)
        {
            if (String.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                return;

            ResilientDirectoryDeletion.Delete(folderPath);
        }

    }
}
