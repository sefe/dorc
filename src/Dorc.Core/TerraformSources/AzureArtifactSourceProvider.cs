using Dorc.Core.AzureDevOpsServer;
using Microsoft.Extensions.Logging;
using Org.OpenAPITools.Model;
using System.IO.Compression;
using System.Net.Http;

namespace Dorc.Core.TerraformSources
{
    /// <summary>
    /// Provider for retrieving Terraform code from Azure DevOps build artifacts
    /// </summary>
    public class AzureArtifactSourceProvider : ITerraformSourceProvider
    {
        private readonly int _buildId;
        private readonly string _collection;
        private readonly string _project;
        private readonly IAzureDevOpsServerWebClient _azureDevOpsClient;
        private readonly ILogger _logger;

        public AzureArtifactSourceProvider(
            int buildId,
            string collection,
            string project,
            IAzureDevOpsServerWebClient azureDevOpsClient,
            ILogger logger)
        {
            _buildId = buildId;
            _collection = collection ?? throw new ArgumentNullException(nameof(collection));
            _project = project ?? throw new ArgumentNullException(nameof(project));
            _azureDevOpsClient = azureDevOpsClient ?? throw new ArgumentNullException(nameof(azureDevOpsClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<bool> RetrieveSourceAsync(string workingDirectory, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation($"Retrieving Terraform code from Azure DevOps build artifact. Build ID: {_buildId}, Project: {_project}");

                // Get build artifacts
                var artifacts = _azureDevOpsClient.GetBuildArtifacts(_collection, _project, _buildId);

                if (artifacts == null || artifacts.Count == 0)
                {
                    _logger.LogError($"No artifacts found for build ID {_buildId}");
                    return false;
                }

                // Find the artifact to download (prefer "drop" artifact if multiple exist)
                BuildArtifact artifact;
                if (artifacts.Count > 1)
                {
                    artifact = artifacts.FirstOrDefault(a => a.Name.Equals("drop", StringComparison.OrdinalIgnoreCase))
                        ?? artifacts[0];
                }
                else
                {
                    artifact = artifacts[0];
                }

                _logger.LogInformation($"Downloading artifact '{artifact.Name}' from {artifact.Resource.DownloadUrl}");

                // Download and extract the artifact
                var downloadUrl = artifact.Resource.DownloadUrl;
                await DownloadAndExtractArtifactAsync(downloadUrl, workingDirectory, cancellationToken);

                _logger.LogInformation($"Successfully retrieved Terraform code from Azure DevOps artifact");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to retrieve Terraform code from Azure DevOps artifact: {ex.Message}");
                return false;
            }
        }

        private async Task DownloadAndExtractArtifactAsync(string downloadUrl, string targetDirectory, CancellationToken cancellationToken)
        {
            var tempZipPath = Path.Combine(Path.GetTempPath(), $"artifact-{Guid.NewGuid()}.zip");

            try
            {
                // Download the artifact zip file
                using (var httpClient = new HttpClient())
                {
                    httpClient.Timeout = TimeSpan.FromMinutes(10); // Longer timeout for large artifacts
                    
                    _logger.LogDebug($"Downloading artifact from {downloadUrl}");
                    var response = await httpClient.GetAsync(downloadUrl, cancellationToken);
                    response.EnsureSuccessStatusCode();

                    await using (var fileStream = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await response.Content.CopyToAsync(fileStream, cancellationToken);
                    }
                }

                _logger.LogDebug($"Extracting artifact to {targetDirectory}");
                
                // Extract the zip file
                ZipFile.ExtractToDirectory(tempZipPath, targetDirectory, true);

                _logger.LogDebug($"Artifact extracted successfully");
            }
            finally
            {
                // Clean up temporary zip file
                if (File.Exists(tempZipPath))
                {
                    try
                    {
                        File.Delete(tempZipPath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Failed to delete temporary artifact file: {ex.Message}");
                    }
                }
            }
        }
    }
}
