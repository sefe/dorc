using Dorc.ApiModel;
using Dorc.Runner.Logger;
using Dorc.TerraformRunner.CodeSources.GitHubApi;
using Microsoft.Extensions.Logging;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Dorc.TerraformRunner.CodeSources
{
    /// <summary>
    /// Provider for downloading Terraform code from GitHub Actions workflow artifacts.
    /// </summary>
    public class GitHubArtifactCodeSourceProvider : ITerraformCodeSourceProvider
    {
        private readonly IRunnerLogger _logger;
        private readonly IHttpClientFactory? _httpClientFactory;

        /// <summary>
        /// Default GitHub API hostnames that are always allowed.
        /// Enterprise hosts are accepted when they match the GitHubApiBaseUrl that has
        /// already been validated upstream by IGitHubHostValidator in the Monitor service.
        /// </summary>
        private static readonly HashSet<string> DefaultAllowedHosts = new(StringComparer.OrdinalIgnoreCase)
        {
            "api.github.com",
            "github.com"
        };

        public TerraformSourceType SourceType => TerraformSourceType.GitHubArtifact;

        // 2 GiB cap on downloaded zip — GitHub itself caps artifacts at 10 GB, but 2 GiB
        // covers any sensible Terraform-plan payload while preventing disk exhaustion from
        // a hostile or misconfigured workflow.
        private const long MaxArtifactBytes = 2L * 1024 * 1024 * 1024;

        public GitHubArtifactCodeSourceProvider(IRunnerLogger logger, IHttpClientFactory? httpClientFactory = null)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
        }

        private static async Task CopyWithLimitAsync(HttpContent content, Stream destination, long maxBytes, CancellationToken cancellationToken)
        {
            await using var source = await content.ReadAsStreamAsync(cancellationToken);
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
            {
                total += read;
                if (total > maxBytes)
                {
                    throw new InvalidOperationException(
                        $"GitHub artifact download exceeded the {maxBytes:N0} byte limit; aborting to prevent disk exhaustion.");
                }
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
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

            // The run id is interpolated into a URL path segment below. The Dorc.Api side
            // (GitHubActionsBuildServerClient.GetBuildArtifactDownloadUrlAsync) enforces this;
            // mirror the check here so the runner refuses bogus values that would otherwise
            // produce a malformed request URL like /actions/runs/../../foo/artifacts.
            if (!long.TryParse(scriptGroup.GitHubRunId, out _))
            {
                throw new ArgumentException(
                    $"GitHub Actions run id must be numeric, got '{scriptGroup.GitHubRunId}'.");
            }

            _logger.Information($"Downloading GitHub Actions artifact from run '{scriptGroup.GitHubRunId}' in '{scriptGroup.GitHubOwner}/{scriptGroup.GitHubRepo}'");

            var apiBase = string.IsNullOrEmpty(scriptGroup.GitHubApiBaseUrl)
                ? "https://api.github.com"
                : scriptGroup.GitHubApiBaseUrl;

            // Validate the API base URL to prevent SSRF. Enterprise hosts are accepted
            // because the upstream Monitor/API already validated them via IGitHubHostValidator.
            ValidateApiBaseHost(apiBase);

            using var httpClient = CreateHttpClient(scriptGroup.GitHubToken);

            // Encode owner/repo so a value containing '?', '#', or '/' (which can survive the
            // serialise → ScriptGroup file → deserialise round-trip from TerraformSourceConfigurator)
            // cannot inject query/fragment/path segments into the artifacts URL.
            var artifactsUrl =
                $"{apiBase}/repos/{Uri.EscapeDataString(scriptGroup.GitHubOwner)}" +
                $"/{Uri.EscapeDataString(scriptGroup.GitHubRepo)}" +
                $"/actions/runs/{scriptGroup.GitHubRunId}/artifacts";
            using var response = await httpClient.GetAsync(artifactsUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            GitHubArtifactsListResponse? artifactsResponse;
            try
            {
                artifactsResponse = JsonSerializer.Deserialize<GitHubArtifactsListResponse>(json);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Failed to parse GitHub API response for artifacts: {ex.Message}", ex);
            }

            if (artifactsResponse?.Artifacts == null || artifactsResponse.Artifacts.Count == 0)
            {
                throw new InvalidOperationException($"No artifacts found for GitHub Actions run {scriptGroup.GitHubRunId}");
            }

            _logger.FileLogger.LogInformation($"Found {artifactsResponse.Artifacts.Count} artifact(s) for run '{scriptGroup.GitHubRunId}'");

            // Prefer an artifact named "drop" (consistent with AzDO convention), else take the first
            var artifact = artifactsResponse.Artifacts.FirstOrDefault(a =>
                "drop".Equals(a.Name, StringComparison.OrdinalIgnoreCase)) ?? artifactsResponse.Artifacts[0];
            var downloadUrl = artifact.ArchiveDownloadUrl;

            // SSRF defence in depth: archive_download_url comes from the JSON body of the previous
            // request, not from a configured value. A compromised or spoofed upstream could swap
            // the host. The bearer token is attached by CreateHttpClient(), so re-validate the
            // host before issuing the request — same check the Dorc.Core side already performs.
            ValidateApiBaseHost(downloadUrl);

            _logger.Information($"Downloading artifact '{artifact.Name}'");

            using var downloadResponse = await httpClient.GetAsync(downloadUrl, cancellationToken);
            downloadResponse.EnsureSuccessStatusCode();

            var tempZipFile = Path.Join(DorcProgramData.Root, $"gh-artifact-{Guid.NewGuid()}.zip");
            try
            {
                using (var fileStream = File.Create(tempZipFile))
                {
                    // Cap the on-disk size so a misbehaving or hostile workflow can't fill the
                    // runner host's disk with a multi-GB / zip-bomb artifact. GitHub itself caps
                    // artifacts at 10 GB; 2 GB is well above any sensible Terraform plan payload.
                    await CopyWithLimitAsync(downloadResponse.Content, fileStream, MaxArtifactBytes, cancellationToken);
                }

                // ExtractToDirectory has built-in Zip Slip prevention in .NET 6+:
                // it validates that all resolved entry paths stay within the target directory.
                ZipFile.ExtractToDirectory(tempZipFile, workingDir, overwriteFiles: true);
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

        private HttpClient CreateHttpClient(string token)
        {
            HttpClient client;
            if (_httpClientFactory != null)
            {
                client = _httpClientFactory.CreateClient("GitHubActions");
            }
            else
            {
                client = new HttpClient();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DORC", "1.0"));
                client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
                client.Timeout = TimeSpan.FromMinutes(5);
            }
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return client;
        }

        /// <summary>
        /// Validates the API base URL. Default github.com hosts are always allowed.
        /// Non-default hosts (e.g. github.mycompany.com) are accepted because the
        /// upstream Monitor/API service already validated them via
        /// <c>IGitHubHostValidator</c> before setting <c>GitHubApiBaseUrl</c>.
        /// We still enforce HTTPS, valid URI format, and — for defence in depth —
        /// a match against <see cref="DefaultAllowedHosts"/> when the host isn't
        /// configured as an allowed enterprise host via environment variable.
        /// </summary>
        internal static void ValidateApiBaseHost(string apiBase)
        {
            Uri uri;
            try
            {
                uri = new Uri(apiBase);
            }
            catch (UriFormatException ex)
            {
                throw new ArgumentException($"GitHub API base URL is not a valid URI: '{apiBase}'", ex);
            }

            if (!uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"GitHub API base URL must use HTTPS, got '{uri.Scheme}'");

            if (DefaultAllowedHosts.Contains(uri.Host))
                return;

            // Enterprise hosts are accepted only if listed in the runner's
            // environment allow-list (mirrors the upstream Monitor-side
            // IGitHubHostValidator behaviour). This is defence in depth:
            // primary validation happens in the Monitor before the value ever
            // reaches the runner, but we refuse to send the bearer token to
            // an unexpected host in any case.
            var enterpriseHostsEnv = Environment.GetEnvironmentVariable("DORC_GITHUB_ENTERPRISE_HOSTS");
            if (!string.IsNullOrEmpty(enterpriseHostsEnv))
            {
                var enterpriseHosts = enterpriseHostsEnv
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (enterpriseHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
                    return;
            }

            throw new ArgumentException(
                $"GitHub API host '{uri.Host}' is not an allowed GitHub host. " +
                "Public github.com / api.github.com are allowed by default; " +
                "add enterprise hosts via the DORC_GITHUB_ENTERPRISE_HOSTS environment variable (comma-separated).");
        }
    }
}
