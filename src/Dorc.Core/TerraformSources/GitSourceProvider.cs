using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Dorc.Core.TerraformSources
{
    /// <summary>
    /// Provider for retrieving Terraform code from a Git repository (GitHub or Azure DevOps Git)
    /// </summary>
    public class GitSourceProvider : ITerraformSourceProvider
    {
        private readonly string _repoUrl;
        private readonly string _branch;
        private readonly string? _path;
        private readonly string? _gitUsername;
        private readonly string? _gitPassword;
        private readonly ILogger _logger;

        public GitSourceProvider(
            string repoUrl, 
            string branch, 
            string? path,
            string? gitUsername,
            string? gitPassword,
            ILogger logger)
        {
            _repoUrl = repoUrl ?? throw new ArgumentNullException(nameof(repoUrl));
            _branch = branch ?? throw new ArgumentNullException(nameof(branch));
            _path = path;
            _gitUsername = gitUsername;
            _gitPassword = gitPassword;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<bool> RetrieveSourceAsync(string workingDirectory, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation($"Cloning Git repository from '{SanitizeUrlForLogging(_repoUrl)}' branch '{_branch}' to '{workingDirectory}'");

                var tempCloneDir = Path.Combine(Path.GetTempPath(), $"git-clone-{Guid.NewGuid()}");
                Directory.CreateDirectory(tempCloneDir);

                try
                {
                    // Clone the repository using git command
                    var cloneResult = await RunGitCommandAsync(
                        "clone", 
                        GetCloneArgs(tempCloneDir),
                        Path.GetTempPath(),
                        cancellationToken);

                    if (!cloneResult)
                    {
                        _logger.LogError($"Failed to clone Git repository from '{SanitizeUrlForLogging(_repoUrl)}'");
                        return false;
                    }

                    // Checkout the specific branch
                    var checkoutResult = await RunGitCommandAsync(
                        "checkout",
                        _branch,
                        tempCloneDir,
                        cancellationToken);

                    if (!checkoutResult)
                    {
                        _logger.LogError($"Failed to checkout branch '{_branch}' from repository '{SanitizeUrlForLogging(_repoUrl)}'");
                        return false;
                    }

                    // Copy the files from the specified path (or root) to working directory
                    var sourceDir = string.IsNullOrEmpty(_path) 
                        ? tempCloneDir 
                        : Path.Combine(tempCloneDir, _path);

                    if (!Directory.Exists(sourceDir))
                    {
                        _logger.LogError($"Path '{_path}' does not exist in repository '{SanitizeUrlForLogging(_repoUrl)}'");
                        return false;
                    }

                    await CopyDirectoryAsync(sourceDir, workingDirectory, cancellationToken);
                    _logger.LogInformation($"Successfully retrieved Terraform code from Git repository");
                    
                    return true;
                }
                finally
                {
                    // Clean up temporary clone directory
                    if (Directory.Exists(tempCloneDir))
                    {
                        try
                        {
                            Directory.Delete(tempCloneDir, true);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, $"Failed to clean up temporary Git clone directory: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to retrieve Terraform code from Git repository '{SanitizeUrlForLogging(_repoUrl)}': {ex.Message}");
                return false;
            }
        }

        private string GetCloneArgs(string targetDirectory)
        {
            // Build clone URL with credentials embedded (only used internally, not logged)
            var cloneUrl = _repoUrl;
            if (!string.IsNullOrEmpty(_gitUsername) && !string.IsNullOrEmpty(_gitPassword))
            {
                var uri = new Uri(_repoUrl);
                cloneUrl = $"{uri.Scheme}://{Uri.EscapeDataString(_gitUsername)}:{Uri.EscapeDataString(_gitPassword)}@{uri.Host}{uri.PathAndQuery}";
            }
            
            return $"--branch {_branch} --single-branch \"{cloneUrl}\" \"{targetDirectory}\"";
        }

        private async Task<bool> RunGitCommandAsync(
            string command,
            string arguments,
            string workingDir,
            CancellationToken cancellationToken)
        {
            using var process = new Process();
            process.StartInfo.FileName = "git";
            process.StartInfo.Arguments = $"{command} {arguments}";
            process.StartInfo.WorkingDirectory = workingDir;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.CreateNoWindow = true;

            _logger.LogDebug($"Running Git command: git {command} in {workingDir}");

            try
            {
                process.Start();
                
                var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
                var error = await process.StandardError.ReadToEndAsync(cancellationToken);
                
                await process.WaitForExitAsync(cancellationToken);

                if (process.ExitCode != 0)
                {
                    // Sanitize error output to remove any credentials
                    var sanitizedError = SanitizeForLogging(error);
                    _logger.LogError($"Git command failed with exit code {process.ExitCode}. Error: {sanitizedError}");
                    return false;
                }

                _logger.LogDebug($"Git command completed successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to run Git command: {ex.Message}");
                return false;
            }
        }

        private string SanitizeUrlForLogging(string url)
        {
            // Remove credentials from URL for logging
            try
            {
                var uri = new Uri(url);
                if (!string.IsNullOrEmpty(uri.UserInfo))
                {
                    return $"{uri.Scheme}://***@{uri.Host}{uri.PathAndQuery}";
                }
            }
            catch
            {
                // If URL parsing fails, return a safe placeholder
            }
            return url;
        }

        private string SanitizeForLogging(string text)
        {
            // Remove password from any text for logging
            if (!string.IsNullOrEmpty(_gitPassword))
            {
                text = text.Replace(_gitPassword, "***");
            }
            if (!string.IsNullOrEmpty(_gitUsername))
            {
                text = text.Replace(_gitUsername, "***");
            }
            return text;
        }

        private async Task CopyDirectoryAsync(string sourceDir, string destDir, CancellationToken cancellationToken)
        {
            await Task.Run(() =>
            {
                foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Skip .git directory by checking if any path segment is ".git"
                    var relativePath = Path.GetRelativePath(sourceDir, file);
                    var pathSegments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (pathSegments.Any(segment => segment.Equals(".git", StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

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
    }
}
