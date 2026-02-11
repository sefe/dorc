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
            // Copy Terraform files from component script path to working directory
            if (!string.IsNullOrEmpty(scriptGroup.ScriptsLocation) && Directory.Exists(scriptGroup.ScriptsLocation))
            {
                _logger.Information($"Copying files from '{scriptGroup.ScriptsLocation}' to working directory");
                await CopyDirectoryAsync(scriptGroup.ScriptsLocation, workingDir, cancellationToken);
            }
            else
            {
                throw new InvalidOperationException($"Terraform script path '{scriptGroup.ScriptsLocation}' does not exist.");
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
    }
}
