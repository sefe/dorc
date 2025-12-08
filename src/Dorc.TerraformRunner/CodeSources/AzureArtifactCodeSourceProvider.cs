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
        private readonly string azureBaseUrl = "https://dev.azure.com";
        private readonly IRunnerLogger _logger;

        public TerraformSourceType SourceType => TerraformSourceType.AzureArtifact;

        public AzureArtifactCodeSourceProvider(IRunnerLogger logger)
        {
            _logger = logger;
        }

        public async Task ProvisionCodeAsync(ScriptGroup scriptGroup, string scriptPath, string workingDir, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(scriptGroup.AzureOrganization) || 
                string.IsNullOrEmpty(scriptGroup.AzureProjects) ||
                string.IsNullOrEmpty(scriptGroup.AzureBuildId))
            {
                throw new InvalidOperationException("Azure DevOps artifact information is not configured.");
            }

            if (string.IsNullOrEmpty(scriptGroup.AzureBearerToken))
            {
                throw new ArgumentException("No Azure DevOps bearer token provided by Monitor, check its logs. Cannot download artifact");
            }

            _logger.FileLogger.LogInformation($"Downloading Azure artifact from build '{scriptGroup.AzureBuildId}' in projects '{scriptGroup.AzureProjects}'");

            var projectNames = scriptGroup.AzureProjects.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

            // Configure the Azure DevOps API client
            var config = new Configuration
            {
                BasePath = azureBaseUrl,
                AccessToken = scriptGroup.AzureBearerToken
            };

            var artifactsApi = new ArtifactsApi(config);
            var apiVersion = "6.0";
            var downloaded = false;
            foreach (var projName in projectNames)
            {
                _logger.FileLogger.LogInformation($"Processing project '{projName}'");
                try
                {
                    // Get the list of artifacts for the build
                    var artifacts = await artifactsApi.ArtifactsListAsync(
                        scriptGroup.AzureOrganization,
                        projName,
                        int.Parse(scriptGroup.AzureBuildId),
                        apiVersion,
                        cancellationToken: cancellationToken
                    );

                    if (artifacts == null || artifacts.Count == 0)
                    {
                        _logger.FileLogger.LogInformation($"No artifacts found for build '{scriptGroup.AzureBuildId}' in project '{projName}'");
                        continue;
                    }

                    _logger.FileLogger.LogInformation($"Found {artifacts.Count} artifact(s) for build '{scriptGroup.AzureBuildId}' in project '{projName}'");

                    foreach (var artifact in artifacts)
                    {
                        // Download the artifact
                        if (artifact.Resource?.Type.ToLower() == "container")
                        {
                            // For container artifacts, download as ZIP
                            downloaded = await DownloadContainerArtifactAsync(
                                scriptGroup.AzureOrganization,
                                projName,
                                scriptGroup.AzureBuildId,
                                artifact.Name,
                                workingDir,
                                scriptGroup.AzureBearerToken,
                                cancellationToken
                            );
                        }
                        else if (artifact.Resource?.Type.ToLower() == "filepath")
                        {
                            // For file path artifacts, use the download URL
                            if (!string.IsNullOrEmpty(artifact.Resource?.DownloadUrl))
                            {
                                downloaded = await DownloadFromUrlAsync(
                                    artifact.Resource.DownloadUrl,
                                    workingDir,
                                    scriptGroup.AzureBearerToken,
                                    cancellationToken
                                );
                            }
                            else
                            {
                                _logger.FileLogger.LogInformation($"No download URL found for artifact '{artifact.Name}' in project '{projName}'");
                                continue;
                            }
                        }
                        else
                        {
                            _logger.FileLogger.LogDebug($"Unsupported artifact type: {artifact.Resource?.Type} in project '{projName}'");
                            continue;
                        }
                    }

                    if (downloaded)
                    {
                        // If a sub-path is specified, extract only that directory
                        if (!string.IsNullOrEmpty(scriptGroup.TerraformSubPath))
                        {
                            await DirectoryHelper.ExtractSubPathAsync(workingDir, scriptGroup.TerraformSubPath, cancellationToken);
                        }

                        _logger.FileLogger.LogInformation($"Successfully downloaded artifact to '{workingDir}' from project '{projName}'");
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.FileLogger.LogError(ex, $"Failed to download Azure artifact: {ex.Message}");
                    continue;
                }
            }

            if (!downloaded)
            {
                throw new InvalidOperationException("Failed to download Azure DevOps artifact from all specified projects.");
            }
        }

        private async Task<bool> DownloadContainerArtifactAsync(
            string organization,
            string project,
            string buildId,
            string artifactName,
            string workingDir,
            string bearerToken,
            CancellationToken cancellationToken)
        {
            // Container artifacts are downloaded as ZIP files
            var downloadUrl = $"{azureBaseUrl}/{organization}/{project}/_apis/build/builds/{buildId}/artifacts?artifactName={Uri.EscapeDataString(artifactName)}&api-version=6.0&$format=zip";

            _logger.FileLogger.LogInformation($"Downloading artifact '{artifactName}' (Type: Container) from project '{project}'");

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

                return true;
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

        private async Task<bool> DownloadFromUrlAsync(
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

            _logger.FileLogger.LogInformation($"Downloading artifact from Url {downloadUrl}");

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

                    return true;
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

                return true;
            }
        }
    }
}
