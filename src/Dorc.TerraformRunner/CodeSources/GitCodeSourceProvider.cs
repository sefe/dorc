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

            // Validate and sanitize the ref to prevent command injection. It
            // may name a branch, a tag (the catalog pins module versions as
            // git tags, e.g. stock-modules/sql-database/v1.0.0), or a commit.
            var gitRef = SanitizeGitParameter(scriptGroup.TerraformGitBranch ?? "main");

            _logger.Information($"Cloning Git repository '{scriptGroup.TerraformGitRepoUrl}' ref '{gitRef}'");

            // Determine if this is GitHub or Azure DevOps
            bool isGitHub = scriptGroup.TerraformGitRepoUrl.Contains("github.com", StringComparison.OrdinalIgnoreCase);
            bool isAzureDevOps = scriptGroup.TerraformGitRepoUrl.Contains("dev.azure.com", StringComparison.OrdinalIgnoreCase) ||
                                 scriptGroup.TerraformGitRepoUrl.Contains("visualstudio.com", StringComparison.OrdinalIgnoreCase);

            await Task.Run(() =>
            {
                // Clone the whole repo (all branches + tags) rather than
                // passing the ref as CloneOptions.BranchName: BranchName only
                // resolves remote-tracking BRANCHES, so a tag or commit ref
                // makes Clone throw. We then resolve the ref (branch, tag, or
                // commit) and check it out explicitly - a superset of the old
                // branch-only behaviour.
                var cloneOptions = new CloneOptions();
                cloneOptions.FetchOptions.CredentialsProvider = (_url, _user, _cred) => CreateCredentials(scriptGroup, isGitHub, isAzureDevOps);
                cloneOptions.FetchOptions.OnProgress = (serverProgressOutput) =>
                {
                    _logger.FileLogger.LogDebug($"Git clone progress: {serverProgressOutput}");
                    return !cancellationToken.IsCancellationRequested;
                };

                try
                {
                    var repoPath = Repository.Clone(scriptGroup.TerraformGitRepoUrl, workingDir, cloneOptions);
                    using var repo = new Repository(repoPath);
                    CheckoutRef(repo, gitRef);
                }
                catch (Exception ex)
                {
                    _logger.FileLogger.LogError(ex, $"Failed to clone Git repository: {ex.Message}");
                    throw new InvalidOperationException($"Failed to clone Git repository: {ex.Message}", ex);
                }
            }, cancellationToken);
        }

        // Resolves gitRef against a freshly cloned repo as a branch, tag, or
        // commit (in that order) and checks it out. Non-default branches are
        // remote-tracking after a full clone, so we also try the origin/ prefix.
        private void CheckoutRef(Repository repo, string gitRef)
        {
            // 1. Local branch (typically only the default branch exists locally).
            var localBranch = repo.Branches[gitRef];
            if (localBranch != null)
            {
                Commands.Checkout(repo, localBranch);
                return;
            }

            // 2. Remote-tracking branch.
            var remoteBranch = repo.Branches["origin/" + gitRef];
            if (remoteBranch != null)
            {
                Commands.Checkout(repo, remoteBranch);
                return;
            }

            // 3. Tag -> its target commit (detached HEAD, fine for a build).
            var tag = repo.Tags[gitRef];
            if (tag != null)
            {
                var tagCommit = tag.PeeledTarget as Commit ?? tag.Target.Peel<Commit>();
                if (tagCommit != null)
                {
                    Commands.Checkout(repo, tagCommit);
                    return;
                }
            }

            // 4. Commit SHA / other committish.
            var commit = repo.Lookup(gitRef)?.Peel<Commit>();
            if (commit != null)
            {
                Commands.Checkout(repo, commit);
                return;
            }

            throw new InvalidOperationException(
                $"Could not resolve git ref '{gitRef}' as a branch, tag, or commit in '{repo.Info.WorkingDirectory}'.");
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
