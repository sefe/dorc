using Dorc.ApiModel;
using Dorc.Runner.Logger;
using LibGit2Sharp;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace Dorc.TerraformmRunner.CodeSources
{
    /// <summary>
    /// Provider for cloning Terraform code from Git repositories (GitHub and Azure DevOps Git)
    /// </summary>
    public class GitCodeSourceProvider : ITerraformCodeSourceProvider
    {
        private readonly IRunnerLogger _logger;

        public TerraformSourceType SourceType => TerraformSourceType.Git;

        public GitCodeSourceProvider(IRunnerLogger logger)
        {
            _logger = logger;
        }

        public async Task ProvisionCodeAsync(ScriptGroup scriptGroup, string scriptPath, string workingDir, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(scriptGroup.TerraformGitRepoUrl))
            {
                throw new InvalidOperationException("Git repository URL is not configured.");
            }

            // Validate and sanitize branch name to prevent command injection
            var branchName = SanitizeGitParameter(scriptGroup.TerraformGitBranch ?? "main");
            
            _logger.FileLogger.LogInformation($"Cloning Git repository '{scriptGroup.TerraformGitRepoUrl}' branch '{branchName}'");

            // Determine if this is GitHub or Azure DevOps
            bool isGitHub = scriptGroup.TerraformGitRepoUrl.Contains("github.com", StringComparison.OrdinalIgnoreCase);
            bool isAzureDevOps = scriptGroup.TerraformGitRepoUrl.Contains("dev.azure.com", StringComparison.OrdinalIgnoreCase) ||
                                 scriptGroup.TerraformGitRepoUrl.Contains("visualstudio.com", StringComparison.OrdinalIgnoreCase);

            await Task.Run(() =>
            {
                var cloneOptions = new CloneOptions();
                cloneOptions.BranchName = branchName;
                cloneOptions.FetchOptions.CredentialsProvider = (_url, _user, _cred) => CreateCredentials(scriptGroup, isGitHub, isAzureDevOps);
                cloneOptions.FetchOptions.OnProgress = (serverProgressOutput) =>
                {
                    _logger.FileLogger.LogDebug($"Git clone progress: {serverProgressOutput}");
                    return !cancellationToken.IsCancellationRequested;
                };

                try
                {
                    Repository.Clone(scriptGroup.TerraformGitRepoUrl, workingDir, cloneOptions);
                }
                catch (Exception ex)
                {
                    _logger.FileLogger.LogError(ex, $"Failed to clone Git repository: {ex.Message}");
                    throw new InvalidOperationException($"Failed to clone Git repository: {ex.Message}", ex);
                }
            }, cancellationToken);

            _logger.FileLogger.LogInformation($"Successfully cloned Git repository to '{workingDir}'");

            // If a sub-path is specified, move only that directory to the root
            if (!string.IsNullOrEmpty(scriptGroup.TerraformSubPath))
            {
                await ExtractSubPathAsync(workingDir, scriptGroup.TerraformSubPath, cancellationToken);
                _logger.FileLogger.LogInformation($"Successfully extracted path {scriptGroup.TerraformSubPath}");
            }
        }

        private UsernamePasswordCredentials CreateCredentials(ScriptGroup scriptGroup, bool isGitHub, bool isAzureDevOps)
        {
            // For GitHub and Azure DevOps Git, PAT is used as username with empty password
            // or as password with any username (both work)
            if (!string.IsNullOrEmpty(scriptGroup.TerraformGitPat))
            {
                // Use PAT as password with empty username (standard for GitHub/Azure DevOps)
                return new UsernamePasswordCredentials
                {
                    Username = string.Empty,
                    Password = scriptGroup.TerraformGitPat
                };
            }
            else if (isAzureDevOps && !string.IsNullOrEmpty(scriptGroup.AzureBearerToken))
            {
                // For Azure DevOps with bearer token, use it as PAT
                return new UsernamePasswordCredentials
                {
                    Username = string.Empty,
                    Password = scriptGroup.AzureBearerToken
                };
            }

            throw new InvalidOperationException("No valid credentials found for Git authentication.");
        }

        private async Task ExtractSubPathAsync(string workingDir, string subPath, CancellationToken cancellationToken)
        {
            var subPathDir = Path.Combine(workingDir, subPath);
            if (Directory.Exists(subPathDir))
            {
                var tempDir = Path.Combine(Path.GetTempPath(), $"terraform-temp-{Guid.NewGuid()}");
                Directory.Move(workingDir, tempDir);
                Directory.CreateDirectory(workingDir);
                
                var subPathInTemp = Path.Combine(tempDir, subPath);
                await CopyDirectoryAsync(subPathInTemp, workingDir, cancellationToken);

                // Clean up temp directory
                try
                {
                    RemoveReadOnlyAttributes(tempDir);
                    Directory.Delete(tempDir, true);
                }
                catch (Exception ex)
                {
                    _logger.FileLogger.LogWarning($"Failed to delete temporary directory '{tempDir}': {ex.Message}");
                }
            }
            else
            {
                _logger.FileLogger.LogWarning($"Terraform sub-path '{subPath}' not found in repository.");
            }
        }

        private void RemoveReadOnlyAttributes(string directory)
        {
            var directoryInfo = new DirectoryInfo(directory);

            // Remove readonly from the directory itself
            if (directoryInfo.Attributes.HasFlag(FileAttributes.ReadOnly))
            {
                directoryInfo.Attributes &= ~FileAttributes.ReadOnly;
            }

            // Remove readonly from all files
            foreach (var file in directoryInfo.GetFiles("*", SearchOption.AllDirectories))
            {
                if (file.Attributes.HasFlag(FileAttributes.ReadOnly))
                {
                    file.Attributes &= ~FileAttributes.ReadOnly;
                }
            }

            // Remove readonly from all subdirectories
            foreach (var dir in directoryInfo.GetDirectories("*", SearchOption.AllDirectories))
            {
                if (dir.Attributes.HasFlag(FileAttributes.ReadOnly))
                {
                    dir.Attributes &= ~FileAttributes.ReadOnly;
                }
            }
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
    }
}
