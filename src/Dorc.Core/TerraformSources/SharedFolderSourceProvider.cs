using Microsoft.Extensions.Logging;

namespace Dorc.Core.TerraformSources
{
    /// <summary>
    /// Provider for retrieving Terraform code from a shared folder (legacy behavior)
    /// </summary>
    public class SharedFolderSourceProvider : ITerraformSourceProvider
    {
        private readonly string _sourcePath;
        private readonly ILogger _logger;

        public SharedFolderSourceProvider(string sourcePath, ILogger logger)
        {
            _sourcePath = sourcePath ?? throw new ArgumentNullException(nameof(sourcePath));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<bool> RetrieveSourceAsync(string workingDirectory, CancellationToken cancellationToken)
        {
            try
            {
                if (!Directory.Exists(_sourcePath))
                {
                    _logger.LogError($"Terraform source path '{_sourcePath}' does not exist.");
                    return false;
                }

                _logger.LogInformation($"Copying Terraform files from '{_sourcePath}' to '{workingDirectory}'");
                await CopyDirectoryAsync(_sourcePath, workingDirectory, cancellationToken);
                
                _logger.LogInformation($"Successfully copied Terraform files from shared folder");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to copy Terraform files from '{_sourcePath}': {ex.Message}");
                return false;
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
