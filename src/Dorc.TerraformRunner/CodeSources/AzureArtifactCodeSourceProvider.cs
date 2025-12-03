using Dorc.ApiModel;
using Dorc.Runner.Logger;
using Microsoft.Extensions.Logging;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using System.IO.Compression;
using System.Net.Http.Headers;

namespace Dorc.TerraformRunner.CodeSources
{
    /// <summary>
    /// Provider for downloading Terraform code from Azure DevOps build artifacts
    /// </summary>
    public class AzureArtifactCodeSourceProvider : ITerraformCodeSourceProvider
    {
        private readonly IRunnerLogger _logger;

        public TerraformSourceType SourceType => TerraformSourceType.AzureArtifact;

        public AzureArtifactCodeSourceProvider(IRunnerLogger logger)
        {
            _logger = logger;
        }

        public async Task ProvisionCodeAsync(ScriptGroup scriptGroup, string scriptPath, string workingDir, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(scriptGroup.AzureOrganization) || 
                string.IsNullOrEmpty(scriptGroup.AzureProject) ||
                string.IsNullOrEmpty(scriptGroup.AzureBuildId))
            {
                throw new InvalidOperationException("Azure DevOps artifact information is not configured.");
            }

            _logger.FileLogger.LogInformation($"Downloading Azure artifact from build '{scriptGroup.AzureBuildId}' in project '{scriptGroup.AzureProject}'");

            // Configure the Azure DevOps API client
            var basePath = $"https://dev.azure.com/{scriptGroup.AzureOrganization}";
            var config = new Configuration
            {
                BasePath = basePath,
                AccessToken = scriptGroup.AzureBearerToken
            };

            var artifactsApi = new ArtifactsApi(config);
            var apiVersion = "6.0";

            try
            {
                // Get the list of artifacts for the build
                var artifacts = await artifactsApi.ArtifactsListAsync(
                    scriptGroup.AzureOrganization,
                    scriptGroup.AzureProject,
                    int.Parse(scriptGroup.AzureBuildId),
                    apiVersion,
                    cancellationToken: cancellationToken
                );

                if (artifacts == null || artifacts.Count == 0)
                {
                    throw new InvalidOperationException($"No artifacts found for build '{scriptGroup.AzureBuildId}'");
                }

                _logger.FileLogger.LogInformation($"Found {artifacts.Count} artifact(s) for build '{scriptGroup.AzureBuildId}'");

                // Find the first artifact (or you could filter by name if needed)
                var artifact = artifacts[0];
                
                _logger.FileLogger.LogInformation($"Downloading artifact '{artifact.Name}' (Type: {artifact.Resource?.Type})");

                // Download the artifact
                if (artifact.Resource?.Type == "Container")
                {
                    // For container artifacts, download as ZIP
                    await DownloadContainerArtifactAsync(
                        scriptGroup.AzureOrganization,
                        scriptGroup.AzureProject,
                        scriptGroup.AzureBuildId,
                        artifact.Name,
                        workingDir,
                        scriptGroup.AzureBearerToken,
                        cancellationToken
                    );
                }
                else if (artifact.Resource?.Type == "FilePath")
                {
                    // For file path artifacts, use the download URL
                    if (!string.IsNullOrEmpty(artifact.Resource?.DownloadUrl))
                    {
                        await DownloadFromUrlAsync(
                            artifact.Resource.DownloadUrl,
                            workingDir,
                            scriptGroup.AzureBearerToken,
                            cancellationToken
                        );
                    }
                    else
                    {
                        throw new InvalidOperationException($"No download URL found for artifact '{artifact.Name}'");
                    }
                }
                else
                {
                    throw new InvalidOperationException($"Unsupported artifact type: {artifact.Resource?.Type}");
                }

                // If a sub-path is specified, extract only that directory
                if (!string.IsNullOrEmpty(scriptGroup.TerraformSubPath))
                {
                    await DirectoryHelper.ExtractSubPathAsync(workingDir, scriptGroup.TerraformSubPath, cancellationToken);
                }

                _logger.FileLogger.LogInformation($"Successfully downloaded artifact to '{workingDir}'");
            }
            catch (Exception ex)
            {
                _logger.FileLogger.LogError(ex, $"Failed to download Azure artifact: {ex.Message}");
                throw new InvalidOperationException($"Failed to download Azure artifact: {ex.Message}", ex);
            }
        }

        private async Task DownloadContainerArtifactAsync(
            string organization,
            string project,
            string buildId,
            string artifactName,
            string workingDir,
            string bearerToken,
            CancellationToken cancellationToken)
        {
            // Container artifacts are downloaded as ZIP files
            var downloadUrl = $"https://dev.azure.com/{organization}/{project}/_apis/build/builds/{buildId}/artifacts?artifactName={Uri.EscapeDataString(artifactName)}&api-version=6.0&$format=zip";

            using var httpClient = new HttpClient();
            if (!string.IsNullOrEmpty(bearerToken))
            {
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            }

            var response = await httpClient.GetAsync(downloadUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            var tempZipFile = Path.Combine(Path.GetTempPath(), $"artifact-{Guid.NewGuid()}.zip");
            try
            {
                // Download to temp file
                using (var fileStream = File.Create(tempZipFile))
                {
                    await response.Content.CopyToAsync(fileStream, cancellationToken);
                }

                _logger.FileLogger.LogInformation($"Downloaded artifact ZIP to '{tempZipFile}'");

                // Extract to working directory
                ZipFile.ExtractToDirectory(tempZipFile, workingDir, true);
                
                _logger.FileLogger.LogInformation($"Extracted artifact to '{workingDir}'");
            }
            finally
            {
                // Clean up temp file
                if (File.Exists(tempZipFile))
                {
                    File.Delete(tempZipFile);
                }
            }
        }

        private async Task DownloadFromUrlAsync(
            string downloadUrl,
            string workingDir,
            string bearerToken,
            CancellationToken cancellationToken)
        {
            using var httpClient = new HttpClient();
            if (!string.IsNullOrEmpty(bearerToken))
            {
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            }

            var response = await httpClient.GetAsync(downloadUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            // Determine if it's a ZIP file based on content type or URL
            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (contentType == "application/zip" || downloadUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                var tempZipFile = Path.Combine(Path.GetTempPath(), $"artifact-{Guid.NewGuid()}.zip");
                try
                {
                    using (var fileStream = File.Create(tempZipFile))
                    {
                        await response.Content.CopyToAsync(fileStream, cancellationToken);
                    }

                    ZipFile.ExtractToDirectory(tempZipFile, workingDir, true);
                }
                finally
                {
                    if (File.Exists(tempZipFile))
                    {
                        File.Delete(tempZipFile);
                    }
                }
            }
            else
            {
                // Direct file download
                var fileName = Path.GetFileName(new Uri(downloadUrl).LocalPath);
                var destFile = Path.Combine(workingDir, fileName);
                
                using var fileStream = File.Create(destFile);
                await response.Content.CopyToAsync(fileStream, cancellationToken);
            }
        }
    }
}
