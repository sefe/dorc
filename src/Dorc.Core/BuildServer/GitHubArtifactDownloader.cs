using System.IO.Compression;
using System.Net.Http.Headers;
using Dorc.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Dorc.Core.BuildServer
{
    /// <summary>
    /// Downloads GitHub Actions artifacts from the API and extracts them
    /// into a directory so PowerShell deployment scripts can use Join-Path
    /// on the resulting file system path. The directory defaults to the
    /// local temp folder, but can be pointed at a UNC share so deployment
    /// targets can read the artifact over the network — see
    /// <c>AppSettings:GitHubArtifactDownloadFolder</c>.
    /// </summary>
    public class GitHubArtifactDownloader : IGitHubArtifactDownloader
    {
        private readonly ILogger<GitHubArtifactDownloader> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _gitHubToken;
        private readonly string _downloadFolder;

        public GitHubArtifactDownloader(
            ILogger<GitHubArtifactDownloader> logger,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            var appSettings = configuration.GetSection("AppSettings");
            _gitHubToken = appSettings["GitHubToken"] ?? string.Empty;
            _downloadFolder = appSettings["GitHubArtifactDownloadFolder"] ?? string.Empty;

            if (string.IsNullOrEmpty(_gitHubToken))
            {
                _logger.LogWarning("GitHubToken is not configured in AppSettings. GitHub artifact downloads " +
                    "will fail with 401 Unauthorized — the /actions/artifacts/{id}/zip endpoint requires a token " +
                    "with 'actions:read' on the target repository.");
            }

            if (!string.IsNullOrEmpty(_downloadFolder))
            {
                _logger.LogInformation("GitHub artifacts will be downloaded under: {Folder}", _downloadFolder);
            }
            else
            {
                _logger.LogWarning("GitHubArtifactDownloadFolder is not configured in AppSettings. " +
                    "Downloads will use the monitor host's local temp folder, which is NOT reachable from " +
                    "deployment targets. Set this to a UNC path accessible from every target if deploy " +
                    "scripts need to invoke binaries out of the artifact.");
            }
        }

        public bool IsGitHubArtifactUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return false;

            // GitHub Actions artifact download URLs contain /actions/artifacts/
            // e.g. https://api.github.com/repos/{owner}/{repo}/actions/artifacts/{id}/zip
            return url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                && url.Contains("/actions/artifacts/", StringComparison.OrdinalIgnoreCase);
        }

        public string DownloadAndExtract(string artifactUrl)
        {
            _logger.LogInformation("Downloading GitHub Actions artifact from: {Url}", artifactUrl);

            var client = _httpClientFactory.CreateClient("GitHubActions");
            if (!string.IsNullOrEmpty(_gitHubToken))
            {
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", _gitHubToken);
            }

            var baseDir = !string.IsNullOrEmpty(_downloadFolder)
                ? _downloadFolder
                : Path.Combine(Path.GetTempPath(), "dorc-artifacts");
            var tempDir = Path.Combine(baseDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var zipPath = Path.Combine(tempDir, "artifact.zip");

                using (var response = client.GetAsync(artifactUrl).GetAwaiter().GetResult())
                {
                    response.EnsureSuccessStatusCode();
                    using var stream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
                    using var fileStream = File.Create(zipPath);
                    stream.CopyTo(fileStream);
                }

                _logger.LogInformation("Downloaded artifact zip ({Size} bytes) to: {Path}",
                    new FileInfo(zipPath).Length, zipPath);

                // Extract into a "drop" subfolder so that deployment scripts using
                // Join-Path $DropFolder "drop\..." resolve correctly.
                var extractDir = Path.Combine(tempDir, "drop");
                ZipFile.ExtractToDirectory(zipPath, extractDir);

                _logger.LogInformation("Extracted artifact to: {Path}", extractDir);

                File.Delete(zipPath);

                return tempDir;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to download/extract GitHub artifact from: {Url}", artifactUrl);
                Cleanup(tempDir);
                throw;
            }
        }

        public void Cleanup(string extractedPath)
        {
            try
            {
                if (string.IsNullOrEmpty(extractedPath) || !Directory.Exists(extractedPath))
                    return;

                Directory.Delete(extractedPath, recursive: true);
                _logger.LogInformation("Cleaned up artifact directory: {Path}", extractedPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clean up artifact directory: {Path}", extractedPath);
            }
        }
    }
}
