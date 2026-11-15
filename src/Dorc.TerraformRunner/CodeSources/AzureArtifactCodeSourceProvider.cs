using Dorc.ApiModel;
using Dorc.AzureDevOps.Client;
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
        private readonly HttpMessageHandler? _httpMessageHandler;
        private readonly string? _stagingDirectory;
        private const string DefaultArtifactTypePriority = "filepath,Container,PipelineArtifact";

        public TerraformSourceType SourceType => TerraformSourceType.AzureArtifact;

        public AzureArtifactCodeSourceProvider(IRunnerLogger logger)
            : this(logger, null, null)
        {
        }

        internal AzureArtifactCodeSourceProvider(
            IRunnerLogger logger,
            HttpMessageHandler? httpMessageHandler,
            string? stagingDirectory = null)
        {
            _logger = logger;
            _httpMessageHandler = httpMessageHandler;
            _stagingDirectory = stagingDirectory;
        }

        public async Task ProvisionCodeAsync(ScriptGroup scriptGroup, string workingDir, CancellationToken cancellationToken)
        {
            var downloaded = false;

            var isFilePathSpecified = !string.IsNullOrEmpty(scriptGroup.ScriptsLocation) && scriptGroup.ScriptsLocation.StartsWith("file://", StringComparison.OrdinalIgnoreCase);

            if (isFilePathSpecified)
            {
                ValidateFileShareHost(scriptGroup.ScriptsLocation, scriptGroup.AllowedArtefactHosts);
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
            var downloaded = false;
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

            _logger.Information($"Downloading Azure artifact from build '{scriptGroup.AzureBuildId}' in projects '{scriptGroup.AzureProjects}'");

            var projectNames = scriptGroup.AzureProjects.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

            var artifactTypePriorities = DefaultArtifactTypePriority.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .ToList();
            
            // Configure the Azure DevOps API client
            var config = new Configuration
            {
                BasePath = azureBaseUrl,
                AccessToken = scriptGroup.AzureBearerToken
            };

            var apiClient = AzureDevOpsApiClientFactory.Create(config.BasePath);
            var artifactsApi = new ArtifactsApi(apiClient, apiClient, config);
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

                    var prioritizedArtifacts = artifactTypePriorities
                        .Select(priority => artifacts.FirstOrDefault(a => a.Resource?.Type == priority))
                        .Where(artifact => artifact != null)
                        .ToList();

                    if (!prioritizedArtifacts.Any())
                    {
                        _logger.FileLogger.LogInformation($"No artifacts matched the priority types: {string.Join(", ", artifactTypePriorities)}");
                        prioritizedArtifacts = artifacts;
                    }

                    // Try to find and download artifacts based on priority
                    foreach (var artifact in prioritizedArtifacts)
                    {
                        if (artifact.Resource != null && !string.IsNullOrEmpty(artifact.Resource.DownloadUrl))
                        {
                            _logger.FileLogger.LogDebug($"Found artifact '{artifact.Name}' of type '{artifact.Resource?.Type}'");

                            downloaded = await DownloadFromUrlAsync(
                                artifact.Resource.DownloadUrl,
                                workingDir,
                                scriptGroup.AzureBearerToken,
                                scriptGroup.AllowedArtefactHosts,
                                cancellationToken
                            );
                        }
                        else
                        {
                            _logger.FileLogger.LogWarning($"Artifact '{artifact.Name}' does not have a valid download URL.");
                        }

                        if (downloaded)
                        {
                            _logger.Information($"Successfully downloaded artifact of type '{artifact.Resource.Type}' from project '{projName}'");
                            break;
                        }                       
                    }
                }
                catch (InvalidOperationException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.FileLogger.LogError(ex, $"Failed to download Azure artifact: {ex.Message}");
                    continue;
                }
            }

            return downloaded;
        }

        internal async Task<bool> DownloadFromUrlAsync(
            string downloadUrl,
            string workingDir,
            string bearerToken,
            IList<string>? allowedArtefactHosts,
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
                ValidateFileShareHost(downloadUrl, allowedArtefactHosts);
                return await DownloadFromFileAsync(uri, workingDir, cancellationToken);
            }

            // Handle http:// and https:// URLs
            if (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) || 
                uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            {
                ValidateArtifactDownloadUrl(downloadUrl, allowedArtefactHosts);
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
                await DirectoryTreeCopy.CopyAsync(sourcePath, workingDir, cancellationToken);
            }
            else
            {
                // Check if it's a ZIP file
                if (sourcePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.FileLogger.LogInformation($"Extracting ZIP file from {sourcePath} to {workingDir}");
                    new ZipArchiveExtractor(ZipArchiveExtractionOptions.Default).Extract(sourcePath, workingDir, cancellationToken);
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
            var handler = _httpMessageHandler ?? new HttpClientHandler { AllowAutoRedirect = false };
            using var httpClient = new HttpClient(handler, disposeHandler: _httpMessageHandler == null);
            var authorization = !string.IsNullOrEmpty(bearerToken)
                ? new AuthenticationHeaderValue("Bearer", bearerToken)
                : null;

            _logger.Information($"Downloading artifact from URL: {downloadUrl}");

            using var response = await OriginBoundHttpSender.GetAsync(
                httpClient,
                new Uri(downloadUrl),
                authorization,
                cancellationToken: cancellationToken);
            response.EnsureSuccessStatusCode();

            // Determine if it's a ZIP file based on content type or URL
            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (contentType == "application/zip" || downloadUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                string? ephemeralStagingDirectory = null;
                string stagingRoot;
                if (_stagingDirectory is not null)
                {
                    stagingRoot = _stagingDirectory;
                    Directory.CreateDirectory(stagingRoot);
                }
                else
                {
                    ephemeralStagingDirectory = Path.Join(
                        DorcProgramData.Root,
                        $"terraform-artifact-{Guid.NewGuid():N}");
                    RestrictedWorkingDirectory.Create(ephemeralStagingDirectory);
                    stagingRoot = ephemeralStagingDirectory;
                }

                var tempZipFile = Path.Join(stagingRoot, $"artifact-{Guid.NewGuid()}.zip");
                try
                {
                    using (var fileStream = File.Create(tempZipFile))
                    {
                        await response.Content.CopyToAsync(fileStream, cancellationToken);
                    }

                    new ZipArchiveExtractor(ZipArchiveExtractionOptions.Default).Extract(tempZipFile, workingDir, cancellationToken);

                    return true;
                }
                finally
                {
                    if (File.Exists(tempZipFile))
                    {
                        File.Delete(tempZipFile);
                    }

                    if (ephemeralStagingDirectory is not null)
                    {
                        ResilientDirectoryDeletion.Delete(ephemeralStagingDirectory);
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

        internal static void ValidateArtifactDownloadUrl(string downloadUrl, IList<string>? allowedArtefactHosts = null)
        {
            if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri) ||
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Refusing to send Azure DevOps bearer token to '{downloadUrl}': HTTPS is required.");
            }

            var isAzureDevOps = SourceHost.Is(downloadUrl, "dev.azure.com")
                || SourceHost.Is(downloadUrl, "visualstudio.com");
            if (!isAzureDevOps)
            {
                throw new InvalidOperationException(
                    $"Refusing to send Azure DevOps bearer token to '{downloadUrl}': host '{uri.Host}' is not an Azure DevOps authority.");
            }

            if (allowedArtefactHosts != null && allowedArtefactHosts.Count > 0)
            {
                var permitted = IsHostInAllowList(downloadUrl, allowedArtefactHosts)
                    || AllowsAzureDevOpsCloud(allowedArtefactHosts);
                if (!permitted)
                {
                    throw new InvalidOperationException(
                        $"Refusing to download Azure DevOps artifact from '{downloadUrl}': host '{uri.Host}' is not permitted by the configured artefact host allow-list.");
                }
            }
        }

        private static void ValidateFileShareHost(string fileUrl, IList<string>? allowedArtefactHosts)
        {
            if (!SourceHost.IsRemoteOrUnc(fileUrl))
            {
                return;
            }

            if (!SourceHost.TryExtractRemoteHost(fileUrl, out var host) ||
                allowedArtefactHosts == null ||
                allowedArtefactHosts.Count == 0 ||
                !IsExactHostInAllowList(host, allowedArtefactHosts))
            {
                throw new InvalidOperationException(
                    $"Refusing to read Azure DevOps file-share artifact from '{fileUrl}': host '{host ?? "<unrecognised>"}' is not in the configured artefact host allow-list.");
            }
        }

        private static bool IsExactHostInAllowList(string host, IList<string> allowedHosts)
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

        private static bool IsHostInAllowList(string url, IList<string> allowedHosts)
        {
            for (var i = 0; i < allowedHosts.Count; i++)
            {
                var entry = allowedHosts[i]?.Trim().TrimStart('.');
                if (!string.IsNullOrEmpty(entry) && SourceHost.Is(url, entry))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool AllowsAzureDevOpsCloud(IList<string> allowedHosts)
        {
            for (var i = 0; i < allowedHosts.Count; i++)
            {
                var entry = allowedHosts[i]?.Trim().TrimStart('.');
                if (!string.IsNullOrEmpty(entry) &&
                    (SourceHost.Is("https://" + entry, "dev.azure.com")
                     || SourceHost.Is("https://" + entry, "visualstudio.com")))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
