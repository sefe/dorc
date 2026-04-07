using Dorc.ApiModel;
using Dorc.Runner.Logger;
using Microsoft.Extensions.Logging;
using System.IO.Compression;
using System.Net.Http.Headers;

namespace Dorc.TerraformRunner.CodeSources
{
    /// <summary>
    /// Provider for downloading Terraform code from GitHub Actions workflow artifacts.
    /// </summary>
    public class GitHubArtifactCodeSourceProvider : ITerraformCodeSourceProvider
    {
        private readonly IRunnerLogger _logger;

        public TerraformSourceType SourceType => TerraformSourceType.GitHubArtifact;

        public GitHubArtifactCodeSourceProvider(IRunnerLogger logger)
        {
            _logger = logger;
        }

        public async Task ProvisionCodeAsync(ScriptGroup scriptGroup, string workingDir, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(scriptGroup.GitHubOwner) ||
                string.IsNullOrEmpty(scriptGroup.GitHubRepo) ||
                string.IsNullOrEmpty(scriptGroup.GitHubRunId))
            {
                throw new InvalidOperationException("GitHub Actions artifact information is not configured. Required: GitHubOwner, GitHubRepo, GitHubRunId.");
            }

            if (string.IsNullOrEmpty(scriptGroup.GitHubToken))
            {
                throw new ArgumentException("Cannot download artifact as no GitHub token provided.");
            }

            _logger.Information($"Downloading GitHub Actions artifact from run '{scriptGroup.GitHubRunId}' in '{scriptGroup.GitHubOwner}/{scriptGroup.GitHubRepo}'");

            var apiBase = string.IsNullOrEmpty(scriptGroup.GitHubApiBaseUrl)
                ? "https://api.github.com"
                : scriptGroup.GitHubApiBaseUrl;

            // Validate the API base URL host to prevent SSRF / token exfiltration
            ValidateApiBaseHost(apiBase);

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DORC", "1.0"));
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", scriptGroup.GitHubToken);

            // List artifacts for the run
            var artifactsUrl = $"{apiBase}/repos/{scriptGroup.GitHubOwner}/{scriptGroup.GitHubRepo}/actions/runs/{scriptGroup.GitHubRunId}/artifacts";
            var response = await httpClient.GetAsync(artifactsUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var artifactsResponse = System.Text.Json.JsonSerializer.Deserialize<GitHubArtifactsListResponse>(json);

            if (artifactsResponse?.Artifacts == null || artifactsResponse.Artifacts.Count == 0)
            {
                throw new InvalidOperationException($"No artifacts found for GitHub Actions run {scriptGroup.GitHubRunId}");
            }

            _logger.FileLogger.LogInformation($"Found {artifactsResponse.Artifacts.Count} artifact(s) for run '{scriptGroup.GitHubRunId}'");

            // Prefer an artifact named "drop" (consistent with AzDO convention), else take the first
            var artifact = artifactsResponse.Artifacts.FirstOrDefault(a =>
                "drop".Equals(a.Name, StringComparison.OrdinalIgnoreCase)) ?? artifactsResponse.Artifacts[0];
            var downloadUrl = artifact.ArchiveDownloadUrl;

            _logger.Information($"Downloading artifact '{artifact.Name}' from {downloadUrl}");

            var downloadResponse = await httpClient.GetAsync(downloadUrl, cancellationToken);
            downloadResponse.EnsureSuccessStatusCode();

            var tempZipFile = Path.Combine(Path.GetTempPath(), $"gh-artifact-{Guid.NewGuid()}.zip");
            try
            {
                using (var fileStream = File.Create(tempZipFile))
                {
                    await downloadResponse.Content.CopyToAsync(fileStream, cancellationToken);
                }

                // Extract with Zip Slip protection: sanitize entry names to prevent directory traversal.
                var fullDestination = Path.GetFullPath(workingDir);
                using (var archive = ZipFile.OpenRead(tempZipFile))
                {
                    foreach (var entry in archive.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name))
                            continue;

                        // Strip any leading path traversal components to sanitize the entry name
                        var sanitizedName = SanitizeZipEntryName(entry.FullName);
                        if (string.IsNullOrEmpty(sanitizedName))
                            continue;

                        var destPath = Path.GetFullPath(Path.Combine(fullDestination, sanitizedName));
                        if (!destPath.StartsWith(fullDestination + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                            !destPath.Equals(fullDestination, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidOperationException(
                                $"Zip entry '{entry.FullName}' would extract outside the target directory. Aborting.");
                        }

                        var entryDir = Path.GetDirectoryName(destPath);
                        if (entryDir != null)
                            Directory.CreateDirectory(entryDir);

                        using var entryStream = entry.Open();
                        using var outputStream = File.Create(destPath);
                        entryStream.CopyTo(outputStream);
                    }
                }
                _logger.Information($"Successfully extracted GitHub artifact '{artifact.Name}'");
            }
            finally
            {
                if (File.Exists(tempZipFile))
                {
                    File.Delete(tempZipFile);
                }
            }
        }

        /// <summary>
        /// Strips directory traversal components (.., leading slashes/backslashes) from a zip entry name
        /// to prevent Zip Slip attacks.
        /// </summary>
        private static string SanitizeZipEntryName(string entryFullName)
        {
            // Replace backslashes with forward slashes for consistent handling
            var normalized = entryFullName.Replace('\\', '/');

            // Split into segments and filter out traversal components
            var segments = normalized.Split('/')
                .Where(s => s != ".." && s != "." && !string.IsNullOrEmpty(s))
                .ToArray();

            return string.Join(Path.DirectorySeparatorChar.ToString(), segments);
        }

        private static void ValidateApiBaseHost(string apiBase)
        {
            var uri = new Uri(apiBase);
            if (uri.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
                return;

            // GitHub Enterprise hosts must use HTTPS
            if (!uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"GitHub API base URL must use HTTPS, got '{uri.Scheme}'");
        }

        private class GitHubArtifactsListResponse
        {
            [System.Text.Json.Serialization.JsonPropertyName("total_count")]
            public int TotalCount { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("artifacts")]
            public List<GitHubArtifactItem>? Artifacts { get; set; }
        }

        private class GitHubArtifactItem
        {
            [System.Text.Json.Serialization.JsonPropertyName("id")]
            public long Id { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("name")]
            public string? Name { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("archive_download_url")]
            public string ArchiveDownloadUrl { get; set; } = string.Empty;
        }
    }
}
