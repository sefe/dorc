using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dorc.Core.BuildServer.GitHubApi;
using Dorc.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Dorc.Core.BuildServer
{
    /// <summary>
    /// IBuildServerClient implementation for GitHub Actions.
    /// Uses the GitHub REST API to query workflow runs and artifacts.
    ///
    /// Project configuration mapping:
    ///   ArtefactsUrl      = "https://api.github.com/repos/{owner}/{repo}" (or GitHub Enterprise URL)
    ///   ArtefactsSubPaths = Workflow file names, semicolon-separated (e.g., "build.yml;deploy.yml")
    ///   ArtefactsBuildRegex = Regex to filter workflow run names/titles
    /// </summary>
    public class GitHubActionsBuildServerClient : IBuildServerClient
    {
        private const int PerPage = 100;

        // Hard cap on pages followed via Link: rel="next". 10 * 100 = 1000 items, well above typical
        // workflow / run counts. When exceeded the client raises an explicit pagination-exhausted
        // error rather than silently returning a not-found result.
        private const int MaxPages = 10;

        private readonly ILogger<GitHubActionsBuildServerClient> _logger;
        private readonly string _gitHubToken;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IGitHubHostValidator _hostValidator;

        public GitHubActionsBuildServerClient(ILogger<GitHubActionsBuildServerClient> logger, IConfiguration configuration,
            IHttpClientFactory httpClientFactory, IGitHubHostValidator hostValidator)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _hostValidator = hostValidator;
            var appSettings = configuration.GetSection("AppSettings");
            _gitHubToken = appSettings["GitHubToken"] ?? string.Empty;

            if (string.IsNullOrEmpty(_gitHubToken))
            {
                _logger.LogWarning("GitHubToken is not configured in AppSettings. GitHub API requests will be unauthenticated " +
                    "with a rate limit of 60 requests/hour and no access to private repositories.");
            }
        }

        public IEnumerable<DeployableArtefact> GetDefinitions(string serverUrl, string projectPaths, string buildRegex)
        {
            var (owner, repo) = ParseOwnerRepo(serverUrl);
            var workflowFiles = projectPaths.Split(new[] { ";" }, StringSplitOptions.RemoveEmptyEntries);
            Regex regex;
            try
            {
                regex = new Regex(buildRegex, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(5));
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "Invalid build regex pattern provided");
                return Enumerable.Empty<DeployableArtefact>();
            }

            var result = new List<DeployableArtefact>();

            var client = CreateHttpClient();

            foreach (var workflowFile in workflowFiles)
            {
                var url = $"{_hostValidator.GetApiBase(serverUrl)}/repos/{owner}/{repo}/actions/workflows/{Uri.EscapeDataString(workflowFile.Trim())}";

                try
                {
                    using var response = client.GetAsync(url).ConfigureAwait(false).GetAwaiter().GetResult();
                    response.EnsureSuccessStatusCode();
                    var json = response.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                    var workflow = DeserializeResponse<GitHubWorkflow>(json);

                    if (workflow != null && regex.IsMatch(workflow.Name ?? workflowFile))
                    {
                        result.Add(new DeployableArtefact
                        {
                            Id = workflow.Id.ToString(),
                            Name = workflow.Name ?? workflowFile
                        });
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException
                                           or TaskCanceledException
                                           or System.Text.Json.JsonException
                                           or RegexMatchTimeoutException)
                {
                    _logger.LogError(ex, "Failed to fetch GitHub workflow definition");
                }
            }

            return result.OrderBy(d => d.Name);
        }

        public async Task<IEnumerable<DeployableArtefact>> GetBuildsAsync(string serverUrl, string projectPaths,
            string buildRegex, string definitionName, bool filterPinnedOnly)
        {
            var (owner, repo) = ParseOwnerRepo(serverUrl);

            var client = CreateHttpClient();

            // Find the workflow ID by name
            var workflowId = await GetWorkflowIdByNameAsync(client, serverUrl, owner, repo, definitionName);

            if (workflowId == null)
            {
                _logger.LogError("Could not find matching GitHub workflow by name");
                return Enumerable.Empty<DeployableArtefact>();
            }

            var firstPage = $"{_hostValidator.GetApiBase(serverUrl)}/repos/{owner}/{repo}/actions/workflows/{workflowId}/runs?status=completed&per_page={PerPage}";
            var allRuns = await ReadAllPagesAsync<GitHubWorkflowRunsResponse, GitHubWorkflowRun>(
                client, firstPage, r => r.WorkflowRuns);

            // GitHub Actions does not have a direct equivalent to Azure DevOps "KeepForever" (pinned).
            if (filterPinnedOnly)
            {
                _logger.LogWarning("filterPinnedOnly is not supported for GitHub Actions. " +
                    "All successful runs will be returned regardless of pinned status.");
            }

            var runs = allRuns
                .Where(r => r.Conclusion == "success")
                .Select(r => new DeployableArtefact
                {
                    // Build identity is the run number (stable, monotonic, unique within a workflow).
                    // display_title is derived from the head commit and is NOT unique — re-runs and
                    // parallel workflows on the same commit share titles.
                    Id = r.Id.ToString(),
                    Name = r.RunNumber.ToString(),
                    Date = r.UpdatedAt
                })
                .ToList();

            return runs.OrderByDescending(r => r.Date);
        }

        public async Task<string> GetBuildArtifactDownloadUrlAsync(string serverUrl, string projectPaths,
            string buildRegex, string definitionName, string buildUrl)
        {
            var (owner, repo) = ParseOwnerRepo(serverUrl);

            var client = CreateHttpClient();

            // buildUrl is the run ID for GitHub Actions — must be numeric
            var runId = buildUrl;
            if (!long.TryParse(runId, out _))
                throw new ArgumentException($"GitHub Actions run ID must be numeric, got '{runId}'");

            var artifactsUrl = $"{_hostValidator.GetApiBase(serverUrl)}/repos/{owner}/{repo}/actions/runs/{runId}/artifacts";
            using var response = await client.GetAsync(artifactsUrl);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var artifactsResponse = DeserializeResponse<GitHubArtifactsResponse>(json);

            if (artifactsResponse?.Artifacts == null || artifactsResponse.Artifacts.Count == 0)
                throw new ApplicationException($"No artifacts found for GitHub Actions run {runId}");

            // Prefer an artifact named "drop" (consistent with AzDO convention), else take the first
            var artifact = artifactsResponse.Artifacts.FirstOrDefault(a =>
                "drop".Equals(a.Name, StringComparison.OrdinalIgnoreCase)) ?? artifactsResponse.Artifacts[0];
            return artifact.ArchiveDownloadUrl;
        }

        public async Task<BuildServerBuildInfo?> ValidateBuildAsync(string serverUrl, string projectPaths,
            string buildRegex, string? buildText, string? buildNum, string? vstsUrl, bool pinnedOnly)
        {
            var (owner, repo) = ParseOwnerRepo(serverUrl);

            var client = CreateHttpClient();

            // For GitHub, buildText is the workflow name
            if (string.IsNullOrEmpty(buildText))
                return null;

            // If a specific run ID was provided, fetch it directly instead of listing all runs.
            // vstsUrl can be either a numeric run id or a full GitHub run URL — extract the id.
            var directRunId = TryExtractRunId(vstsUrl);
            if (directRunId != null)
            {
                var run = await GetRunByIdAsync(client, serverUrl, owner, repo, directRunId);
                if (run != null && run.Conclusion == "success")
                    return MapRunToInfo(run, buildText);
                return null;
            }

            var workflowId = await GetWorkflowIdByNameAsync(client, serverUrl, owner, repo, buildText);
            if (workflowId == null)
                return null;

            // "latest" — fetch the single most recent successful run via per_page=1 so that an
            // older latest never gets aged off the first page.
            var cleanBuildNum = buildNum?.Replace(" [PINNED]", "");
            if (!string.IsNullOrEmpty(cleanBuildNum) &&
                cleanBuildNum.Equals("latest", StringComparison.OrdinalIgnoreCase))
            {
                var latestUrl = $"{_hostValidator.GetApiBase(serverUrl)}/repos/{owner}/{repo}/actions/workflows/{workflowId}/runs?status=success&per_page=1";
                using var latestResponse = await client.GetAsync(latestUrl);
                latestResponse.EnsureSuccessStatusCode();
                var latestJson = await latestResponse.Content.ReadAsStringAsync();
                var latestPage = DeserializeResponse<GitHubWorkflowRunsResponse>(latestJson);
                var latestRun = latestPage?.WorkflowRuns?.FirstOrDefault();
                return latestRun != null ? MapRunToInfo(latestRun, buildText) : null;
            }

            if (string.IsNullOrEmpty(cleanBuildNum))
                return null;

            // Match by run_number, falling back to display_title for backwards-compat with any
            // existing requests that stored the title. run_number is unique per workflow; title
            // is not (it defaults to the head-commit message and re-runs share it).
            var firstPage = $"{_hostValidator.GetApiBase(serverUrl)}/repos/{owner}/{repo}/actions/workflows/{workflowId}/runs?status=completed&per_page={PerPage}";
            var trimmed = cleanBuildNum.Trim();
            var allRuns = await ReadAllPagesAsync<GitHubWorkflowRunsResponse, GitHubWorkflowRun>(
                client, firstPage, r => r.WorkflowRuns,
                stopWhen: r => r.Conclusion == "success" &&
                               (r.RunNumber.ToString() == trimmed ||
                                (r.DisplayTitle ?? string.Empty).Trim()
                                    .Equals(trimmed, StringComparison.OrdinalIgnoreCase)));

            var matchedRun = allRuns.FirstOrDefault(r => r.Conclusion == "success" &&
                                                          (r.RunNumber.ToString() == trimmed ||
                                                           (r.DisplayTitle ?? string.Empty).Trim()
                                                               .Equals(trimmed, StringComparison.OrdinalIgnoreCase)));

            return matchedRun != null ? MapRunToInfo(matchedRun, buildText) : null;
        }

        /// <summary>
        /// Extracts a numeric GitHub Actions run id from either a bare numeric string or a full
        /// GitHub run URL such as <c>https://api.github.com/repos/o/r/actions/runs/12345</c> or
        /// <c>https://github.com/o/r/actions/runs/12345</c>.
        /// </summary>
        private static string? TryExtractRunId(string? candidate)
        {
            if (string.IsNullOrEmpty(candidate))
                return null;

            if (long.TryParse(candidate, out _))
                return candidate;

            if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
            {
                var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                var runsIndex = Array.IndexOf(segments, "runs");
                if (runsIndex >= 0 && runsIndex + 1 < segments.Length &&
                    long.TryParse(segments[runsIndex + 1], out _))
                {
                    return segments[runsIndex + 1];
                }
            }

            return null;
        }

        private async Task<GitHubWorkflowRun?> GetRunByIdAsync(HttpClient client, string serverUrl, string owner, string repo, string runId)
        {
            var url = $"{_hostValidator.GetApiBase(serverUrl)}/repos/{owner}/{repo}/actions/runs/{runId}";
            using var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync();
            return DeserializeResponse<GitHubWorkflowRun>(json);
        }

        private static BuildServerBuildInfo MapRunToInfo(GitHubWorkflowRun run, string workflowName)
        {
            return new BuildServerBuildInfo
            {
                BuildUri = run.Id.ToString(),
                ProjectName = workflowName,
                DefinitionName = workflowName,
                BuildId = run.Id,
                // run_number is the canonical, unique-per-workflow identifier; display_title is
                // derived from the head commit and collides on re-runs / parallel workflows.
                BuildNumber = run.RunNumber.ToString()
            };
        }

        private async Task<long?> GetWorkflowIdByNameAsync(HttpClient client, string serverUrl, string owner, string repo, string workflowName)
        {
            var firstPage = $"{_hostValidator.GetApiBase(serverUrl)}/repos/{owner}/{repo}/actions/workflows?per_page={PerPage}";
            var workflows = await ReadAllPagesAsync<GitHubWorkflowsResponse, GitHubWorkflow>(
                client, firstPage, r => r.Workflows,
                stopWhen: w => (w.Name ?? "").Equals(workflowName, StringComparison.OrdinalIgnoreCase));

            return workflows
                .FirstOrDefault(w => (w.Name ?? "").Equals(workflowName, StringComparison.OrdinalIgnoreCase))
                ?.Id;
        }

        /// <summary>
        /// Page through GitHub responses by following the <c>Link: ...; rel="next"</c> header until
        /// either <paramref name="stopWhen"/> matches, the link chain ends, or <see cref="MaxPages"/>
        /// is reached. Throws <see cref="ApplicationException"/> when the cap is hit without finding
        /// the target so the operator gets a clear pagination-exhausted error rather than a silent
        /// not-found.
        /// </summary>
        private async Task<List<TItem>> ReadAllPagesAsync<TResponse, TItem>(
            HttpClient client,
            string firstPageUrl,
            Func<TResponse, IEnumerable<TItem>?> selector,
            Func<TItem, bool>? stopWhen = null)
            where TResponse : class
        {
            var aggregated = new List<TItem>();
            var url = firstPageUrl;

            for (var page = 0; page < MaxPages; page++)
            {
                using var response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync();
                var parsed = DeserializeResponse<TResponse>(json);
                var items = parsed != null ? selector(parsed) : null;
                if (items != null)
                {
                    foreach (var item in items)
                    {
                        aggregated.Add(item);
                        if (stopWhen != null && stopWhen(item))
                            return aggregated;
                    }
                }

                var next = TryGetNextLink(response);
                if (next == null)
                    return aggregated;
                url = next;
            }

            throw new ApplicationException(
                $"GitHub API pagination exceeded {MaxPages} pages ({MaxPages * PerPage} items) " +
                $"without finding the target. Starting URL: {firstPageUrl}");
        }

        private static string? TryGetNextLink(HttpResponseMessage response)
        {
            if (!response.Headers.TryGetValues("Link", out var values))
                return null;

            // RFC 5988: <https://...>; rel="next", <https://...>; rel="last"
            foreach (var value in values)
            {
                foreach (var segment in value.Split(',').Select(p => p.Trim()))
                {
                    if (!segment.Contains("rel=\"next\"", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var start = segment.IndexOf('<');
                    var end = segment.IndexOf('>');
                    if (start >= 0 && end > start)
                        return segment.Substring(start + 1, end - start - 1);
                }
            }
            return null;
        }

        private static T? DeserializeResponse<T>(string json) where T : class
        {
            try
            {
                return JsonSerializer.Deserialize<T>(json);
            }
            catch (JsonException ex)
            {
                throw new ApplicationException(
                    $"Failed to parse GitHub API response as {typeof(T).Name}: {ex.Message}. " +
                    $"Response begins with: {json[..Math.Min(json.Length, 200)]}", ex);
            }
        }

        private HttpClient CreateHttpClient()
        {
            var client = _httpClientFactory.CreateClient("GitHubActions");
            // Static headers (Accept, UserAgent, X-GitHub-Api-Version, Timeout) are configured
            // at named client registration time. Authorization is set per-instance here since
            // the token varies by configuration. Each CreateClient() call returns a new HttpClient
            // instance (only the underlying handler is pooled), so this is thread-safe.
            if (!string.IsNullOrEmpty(_gitHubToken))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _gitHubToken);
            }
            return client;
        }

        private static (string owner, string repo) ParseOwnerRepo(string serverUrl)
        {
            // Expected format: https://api.github.com/repos/{owner}/{repo}
            // Or GitHub Enterprise: https://github.example.com/api/v3/repos/{owner}/{repo}
            var uri = new Uri(serverUrl);
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var reposIndex = Array.IndexOf(segments, "repos");
            if (reposIndex >= 0 && reposIndex + 2 < segments.Length)
            {
                return (segments[reposIndex + 1], segments[reposIndex + 2]);
            }

            throw new ArgumentException(
                $"Cannot parse owner/repo from URL: {serverUrl}. " +
                "Expected format: https://api.github.com/repos/{{owner}}/{{repo}}");
        }
    }
}
