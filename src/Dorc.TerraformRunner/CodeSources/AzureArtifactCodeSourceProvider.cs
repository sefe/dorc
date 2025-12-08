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
            var downloaded = false;

            if (!string.IsNullOrEmpty(scriptGroup.ScriptsLocation) && scriptGroup.ScriptsLocation.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                downloaded = await DownloadFromFileAsync(new Uri(scriptGroup.ScriptsLocation), workingDir, cancellationToken);
            }
            else
            {
                downloaded = await DownloadFromAzure(scriptGroup, workingDir, cancellationToken);
            }

            if (!downloaded)
            {
                throw new InvalidOperationException("Failed to download Azure DevOps artifact from all specified projects.");
            }

            // If a sub-path is specified, move only that directory to the root
            if (!string.IsNullOrEmpty(scriptGroup.TerraformSubPath))
            {
                await DirectoryHelper.ExtractSubPathAsync(workingDir, scriptGroup.TerraformSubPath, cancellationToken);
                _logger.FileLogger.LogInformation($"Successfully extracted path {scriptGroup.TerraformSubPath}");
            }
        }

        private async Task<bool> DownloadFromAzure(ScriptGroup scriptGroup, string workingDir, CancellationToken cancellationToken)
        {
            var downloaded = false;
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
                        if (artifact.Resource?.Type.ToLower() == "filepath" || artifact.Resource?.Type.ToLower() == "container")
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
                }
                catch (Exception ex)
                {
                    _logger.FileLogger.LogError(ex, $"Failed to download Azure artifact: {ex.Message}");
                    continue;
                }
            }

            return downloaded;
        }

        private async Task<bool> DownloadFromUrlAsync(
            string downloadUrl,
            string workingDir,
            string bearerToken,
            CancellationToken cancellationToken)
        {
            _logger.FileLogger.LogInformation($"Downloading artifact from Url {downloadUrl}");

            // Parse the URL to determine the scheme
            Uri uri;
            try
            {
                uri = new Uri(downloadUrl);
            }
            catch (UriFormatException ex)
            {
                _logger.FileLogger.LogError(ex, $"Invalid URL format: {downloadUrl}");
                throw new ArgumentException($"Invalid URL format: {downloadUrl}", nameof(downloadUrl), ex);
            }

            // Handle file:// URLs
            if (uri.Scheme.Equals("file", StringComparison.OrdinalIgnoreCase))
            {
                return await DownloadFromFileAsync(uri, workingDir, cancellationToken);
            }

            // Handle http:// and https:// URLs
            if (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) || 
                uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            {
                return await DownloadFromHttpAsync(downloadUrl, workingDir, bearerToken, cancellationToken);
            }

            throw new NotSupportedException($"The '{uri.Scheme}' scheme is not supported. Only 'file', 'http', and 'https' schemes are allowed.");
        }

        private async Task<bool> DownloadFromFileAsync(
            Uri fileUri,
            string workingDir,
            CancellationToken cancellationToken)
        {
            var sourcePath = fileUri.LocalPath;
            
            _logger.FileLogger.LogInformation($"Copying artifact from local path: {sourcePath}");

            if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
            {
                throw new FileNotFoundException($"File or directory not found: {sourcePath}");
            }

            // Check if source is a file or directory
            var fileAttributes = File.GetAttributes(sourcePath);
            if (fileAttributes.HasFlag(FileAttributes.Directory))
            {
                // Copy entire directory
                _logger.FileLogger.LogInformation($"Copying directory from {sourcePath} to {workingDir}");
                await DirectoryHelper.CopyDirectoryAsync(sourcePath, workingDir, cancellationToken);
            }
            else
            {
                // Check if it's a ZIP file
                if (sourcePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.FileLogger.LogInformation($"Extracting ZIP file from {sourcePath} to {workingDir}");
                    ZipFile.ExtractToDirectory(sourcePath, workingDir, true);
                }
                else
                {
                    // Copy single file
                    var fileName = Path.GetFileName(sourcePath);
                    var destFile = Path.Combine(workingDir, fileName);
                    _logger.FileLogger.LogInformation($"Copying file from {sourcePath} to {destFile}");
                    File.Copy(sourcePath, destFile, true);
                }
            }

            return true;
        }

        private async Task<bool> DownloadFromHttpAsync(
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

            _logger.FileLogger.LogInformation($"Downloading artifact from HTTP(S) URL: {downloadUrl}");

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
