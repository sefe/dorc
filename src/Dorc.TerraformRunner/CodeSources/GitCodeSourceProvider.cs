using Dorc.ApiModel;
using Dorc.Runner.Logger;
using LibGit2Sharp;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace Dorc.TerraformRunner.CodeSources
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

        public async Task ProvisionCodeAsync(ScriptGroup scriptGroup, string workingDir, CancellationToken cancellationToken)
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
