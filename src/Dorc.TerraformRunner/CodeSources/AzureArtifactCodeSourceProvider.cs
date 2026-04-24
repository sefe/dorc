using Dorc.ApiModel;
using Dorc.Runner.Logger;
using Microsoft.Extensions.Logging;
using Org.OpenAPITools.Api;
using Org.OpenAPITools.Client;
using Org.OpenAPITools.Model;
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
        private const string DefaultArtifactTypePriority = "filepath,Container,PipelineArtifact";

        public TerraformSourceType SourceType => TerraformSourceType.AzureArtifact;

        public AzureArtifactCodeSourceProvider(IRunnerLogger logger)
        {
            _logger = logger;
        }

        public async Task ProvisionCodeAsync(ScriptGroup scriptGroup, string workingDir, CancellationToken cancellationToken)
        {
            var downloaded = false;

            var isFilePathSpecified = !string.IsNullOrEmpty(scriptGroup.ScriptsLocation) && scriptGroup.ScriptsLocation.StartsWith("file://", StringComparison.OrdinalIgnoreCase);

            if (isFilePathSpecified)
            {
                downloaded = await DownloadFromFileAsync(new Uri(scriptGroup.ScriptsLocation), workingDir, cancellationToken);
            }
            else
            {
                downloaded = await DownloadFromAzure(scriptGroup, workingDir, cancellationToken);
            }

            if (!downloaded)
            {
                var errMsg = isFilePathSpecified ?
                    $"file path {scriptGroup.ScriptsLocation}" :
                    $"Azure DevOps projects '{scriptGroup.AzureProjects}', buildId {scriptGroup.AzureBuildId}";
                throw new InvalidOperationException($"Failed to download Azure DevOps artifact from {errMsg}");
            }
        }

        private async Task<bool> DownloadFromAzure(ScriptGroup scriptGroup, string workingDir, CancellationToken cancellationToken)
        {
            ValidateAzureConfiguration(scriptGroup);

            _logger.Information($"Downloading Azure artifact from build '{scriptGroup.AzureBuildId}' in projects '{scriptGroup.AzureProjects}'");

            var projectNames = scriptGroup.AzureProjects.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            var artifactsApi = CreateArtifactsApi(scriptGroup.AzureBearerToken);

            foreach (var projName in projectNames)
            {
                _logger.FileLogger.LogInformation($"Processing project '{projName}'");
                try
                {
                    var downloaded = await TryDownloadFromProjectAsync(artifactsApi, scriptGroup, projName, workingDir, cancellationToken);
                    if (downloaded)
                        return true;
                }
                catch (Exception ex)
                {
                    _logger.FileLogger.LogError(ex, $"Failed to download Azure artifact: {ex.Message}");
                }
            }

            return false;
        }

        private void ValidateAzureConfiguration(ScriptGroup scriptGroup)
        {
            if (string.IsNullOrEmpty(scriptGroup.AzureOrganization) ||
                string.IsNullOrEmpty(scriptGroup.AzureProjects) ||
                string.IsNullOrEmpty(scriptGroup.AzureBuildId))
            {
                throw new InvalidOperationException("Azure DevOps artifact information is not configured.");
            }

            if (string.IsNullOrEmpty(scriptGroup.AzureBearerToken))
            {
                throw new ArgumentException("Cannot download artifact as no Azure DevOps bearer token provided by Monitor service, check its logs.");
            }
        }

        private ArtifactsApi CreateArtifactsApi(string bearerToken)
        {
            var config = new Configuration
            {
                BasePath = azureBaseUrl,
                AccessToken = bearerToken
            };
            return new ArtifactsApi(config);
        }

        private async Task<bool> TryDownloadFromProjectAsync(
            ArtifactsApi artifactsApi,
            ScriptGroup scriptGroup,
            string projName,
            string workingDir,
            CancellationToken cancellationToken)
        {
            var artifacts = await artifactsApi.ArtifactsListAsync(
                scriptGroup.AzureOrganization,
                projName,
                int.Parse(scriptGroup.AzureBuildId),
                "6.0",
                cancellationToken: cancellationToken
            );

            if (artifacts == null || artifacts.Count == 0)
            {
                _logger.FileLogger.LogInformation($"No artifacts found for build '{scriptGroup.AzureBuildId}' in project '{projName}'");
                return false;
            }

            _logger.FileLogger.LogInformation($"Found {artifacts.Count} artifact(s) for build '{scriptGroup.AzureBuildId}' in project '{projName}'");

            var artifactTypePriorities = DefaultArtifactTypePriority.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .ToList();

            var prioritizedArtifacts = artifactTypePriorities
                .Select(priority => artifacts.FirstOrDefault(a => a.Resource?.Type == priority))
                .Where(artifact => artifact != null)
                .ToList();

            if (!prioritizedArtifacts.Any())
            {
                _logger.FileLogger.LogInformation($"No artifacts matched the priority types: {string.Join(", ", artifactTypePriorities)}");
                prioritizedArtifacts = artifacts;
            }

            return await TryDownloadArtifactsAsync(prioritizedArtifacts, projName, workingDir, scriptGroup.AzureBearerToken, cancellationToken);
        }

        private async Task<bool> TryDownloadArtifactsAsync(
            List<BuildArtifact> artifacts,
            string projName,
            string workingDir,
            string bearerToken,
            CancellationToken cancellationToken)
        {
            foreach (var artifact in artifacts)
            {
                if (artifact.Resource == null || string.IsNullOrEmpty(artifact.Resource.DownloadUrl))
                {
                    _logger.FileLogger.LogWarning($"Artifact '{artifact.Name}' does not have a valid download URL.");
                    continue;
                }

                _logger.FileLogger.LogDebug($"Found artifact '{artifact.Name}' of type '{artifact.Resource.Type}'");

                var downloaded = await DownloadFromUrlAsync(artifact.Resource.DownloadUrl, workingDir, bearerToken, cancellationToken);
                if (downloaded)
                {
                    _logger.Information($"Successfully downloaded artifact of type '{artifact.Resource.Type}' from project '{projName}'");
                    return true;
                }
            }

            return false;
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
            
            if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
            {
                throw new FileNotFoundException($"File or directory not found: {sourcePath}");
            }

            _logger.Information($"Copying artifact from local path: {sourcePath}");

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

            _logger.Information($"Downloading artifact from URL: {downloadUrl}");

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
