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
            
            _logger.Information($"Cloning Git repository '{scriptGroup.TerraformGitRepoUrl}' branch '{branchName}'");

            var isGitHub = IsHost(scriptGroup.TerraformGitRepoUrl, "github.com");
            var isAzureDevOps = IsHost(scriptGroup.TerraformGitRepoUrl, "dev.azure.com")
                || IsHost(scriptGroup.TerraformGitRepoUrl, "visualstudio.com");

            await Task.Run(() =>
            {
                var cloneOptions = new CloneOptions();
                cloneOptions.BranchName = branchName;
                cloneOptions.FetchOptions.CredentialsProvider = (url, _user, _cred) =>
                    CreateCredentials(scriptGroup, url, isGitHub, isAzureDevOps);
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
        }

        /// <summary>
        /// Supplies the credential libgit2 asks for - but only for the repository this clone was
        /// asked to perform.
        ///
        /// The url argument was previously ignored, and libgit2 calls this back for every URL it
        /// authenticates against during a clone, redirect targets included. A repository that
        /// redirected elsewhere therefore collected the Terraform PAT or the Entra access token,
        /// neither of which is scoped to a repository, without the redirect ever appearing in
        /// project configuration for anyone to notice.
        /// </summary>
        internal UsernamePasswordCredentials CreateCredentials(
            ScriptGroup scriptGroup, string url, bool isGitHub, bool isAzureDevOps)
        {
            var requested = HostOf(url);
            var expected = HostOf(scriptGroup.TerraformGitRepoUrl);

            if (requested == null || expected == null
                || !string.Equals(requested, expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Refusing to authenticate to '{url}': it is not the configured Terraform repository"
                    + " host. Credentials are supplied only to the repository the deployment names.");
            }

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

        internal static bool IsHost(string? url, string host)
        {
            var actual = HostOf(url);

            return actual != null
                && (string.Equals(actual, host, StringComparison.OrdinalIgnoreCase)
                    || actual.EndsWith("." + host, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// The authority of a URL, or null when there is none.
        ///
        /// Deliberately a local copy of what SourceHostAllowList does on the API side, rather
        /// than a shared reference: that type lives in Dorc.PersistentData, and referencing it
        /// from the Terraform Runner would drag Entity Framework into a process whose whole job
        /// is to run terraform. The duplicated part is one string operation; the two are doing
        /// different jobs with it - one compares against a configured list, this compares two
        /// URLs to each other.
        /// </summary>
        private static string? HostOf(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            {
                return null;
            }

            return string.IsNullOrEmpty(uri.Host) ? null : uri.Host;
        }

        private string SanitizeGitParameter(string parameter)
        {
            // Only allow alphanumeric characters, hyphens, underscores, forward slashes, and dots
            // This prevents command injection while allowing valid branch names
            if (string.IsNullOrWhiteSpace(parameter))
            {
                return "main";
            }

            var sanitized = Regex.Replace(parameter, @"[^a-zA-Z0-9\-_/\.]", "");

            if (string.IsNullOrEmpty(sanitized))
            {
                throw new InvalidOperationException($"Invalid branch name: '{parameter}'. The sanitized branch name is empty.");
            }

            return sanitized;
        }
    }
}
