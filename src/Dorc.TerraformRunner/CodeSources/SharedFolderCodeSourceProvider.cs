using Dorc.ApiModel;
using Dorc.Runner.Logger;
using Microsoft.Extensions.Logging;

namespace Dorc.TerraformRunner.CodeSources
{
    /// <summary>
    /// Provider for copying Terraform code from a shared folder
    /// </summary>
    public class SharedFolderCodeSourceProvider : ITerraformCodeSourceProvider
    {
        private readonly IRunnerLogger _logger;

        public TerraformSourceType SourceType => TerraformSourceType.SharedFolder;

        public SharedFolderCodeSourceProvider(IRunnerLogger logger)
        {
            _logger = logger;
        }

        public async Task ProvisionCodeAsync(ScriptGroup scriptGroup, string workingDir, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(scriptGroup.ScriptsLocation))
            {
                throw new InvalidOperationException($"Terraform script path '{scriptGroup.ScriptsLocation}' does not exist.");
            }

            var scriptsLocation = scriptGroup.ScriptsLocation;
            if (SourceHost.IsRemoteOrUnc(scriptsLocation))
            {
                ValidateRemoteSharedFolderHost(scriptsLocation, scriptGroup.AllowedTerraformSourceHosts);
            }

            scriptsLocation = SourceHost.NormalizeExtendedUnc(scriptsLocation);

            // Copy Terraform files from component script path to working directory
            if (Directory.Exists(scriptsLocation))
            {
                _logger.Information($"Copying files from '{scriptsLocation}' to working directory");
                await CopyDirectoryAsync(scriptsLocation, workingDir, cancellationToken);
            }
            else
            {
                throw new InvalidOperationException($"Terraform script path '{scriptGroup.ScriptsLocation}' does not exist.");
            }
        }

        internal static void ValidateRemoteSharedFolderHost(string scriptsLocation, IList<string>? allowedHosts)
        {
            if (allowedHosts == null || allowedHosts.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Refusing to copy Terraform scripts from remote path '{scriptsLocation}' because no Terraform source host allow-list was provided.");
            }

            if (!SourceHost.TryExtractRemoteHost(scriptsLocation, out var host) || !IsExactPermittedHost(host, allowedHosts))
            {
                throw new InvalidOperationException(
                    $"Refusing to copy Terraform scripts from remote path '{scriptsLocation}': host '{host ?? "<unrecognised>"}' is not in the configured Terraform source host allow-list.");
            }
        }

        private static bool IsExactPermittedHost(string host, IList<string> allowedHosts)
        {
            for (var i = 0; i < allowedHosts.Count; i++)
            {
                var entry = allowedHosts[i]?.Trim();
                if (!string.IsNullOrEmpty(entry) && string.Equals(host, entry, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
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
    }
}
