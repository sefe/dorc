using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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
        private readonly ILogger<GitHubActionsBuildServerClient> _logger;
        private readonly string _gitHubToken;

        private static readonly IConfigurationSection AppSettings =
            new ConfigurationBuilder().AddJsonFile("appsettings.json").Build().GetSection("AppSettings");

        public GitHubActionsBuildServerClient(ILogger<GitHubActionsBuildServerClient> logger)
        {
            _logger = logger;
            _gitHubToken = AppSettings["GitHubToken"] ?? string.Empty;
        }

        public IEnumerable<DeployableArtefact> GetBuildDefinitions(string serverUrl, string projectPaths, string buildRegex)
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
                _logger.LogWarning(ex, "Invalid build regex pattern provided");
                return Enumerable.Empty<DeployableArtefact>();
            }

            var result = new List<DeployableArtefact>();

            using var client = CreateHttpClient();

            foreach (var workflowFile in workflowFiles)
            {
                var url = $"{GetApiBase(serverUrl)}/repos/{owner}/{repo}/actions/workflows/{workflowFile.Trim()}";

                try
                {
                    var response = client.GetAsync(url).ConfigureAwait(false).GetAwaiter().GetResult();
                    response.EnsureSuccessStatusCode();
                    var json = response.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                    var workflow = JsonSerializer.Deserialize<GitHubWorkflow>(json);

                    if (workflow != null && regex.IsMatch(workflow.Name ?? workflowFile))
                    {
                        result.Add(new DeployableArtefact
                        {
                            Id = workflow.Id.ToString(),
                            Name = workflow.Name ?? workflowFile
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to fetch GitHub workflow '{Workflow}' for {Owner}/{Repo}", workflowFile, owner, repo);
                }
            }

            return result.OrderBy(d => d.Name);
        }

        public async Task<IEnumerable<DeployableArtefact>> GetBuildsAsync(string serverUrl, string projectPaths,
            string buildRegex, string definitionName, bool filterPinnedOnly)
        {
            var (owner, repo) = ParseOwnerRepo(serverUrl);

            using var client = CreateHttpClient();

            // Find the workflow ID by name
            var workflowId = await GetWorkflowIdByNameAsync(client, serverUrl, owner, repo, definitionName);

            if (workflowId == null)
            {
                _logger.LogWarning("Could not find GitHub workflow with name '{DefinitionName}'", definitionName);
                return Enumerable.Empty<DeployableArtefact>();
            }

            var runsUrl = $"{GetApiBase(serverUrl)}/repos/{owner}/{repo}/actions/workflows/{workflowId}/runs?status=completed&per_page=100";
            var response = await client.GetAsync(runsUrl);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var runsResponse = JsonSerializer.Deserialize<GitHubWorkflowRunsResponse>(json);

            if (runsResponse?.WorkflowRuns == null)
                return Enumerable.Empty<DeployableArtefact>();

            var filteredRuns = runsResponse.WorkflowRuns
                .Where(r => r.Conclusion == "success");

            if (!string.IsNullOrEmpty(buildRegex))
            {
                try
                {
                    var regex = new Regex(buildRegex, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(5));
                    filteredRuns = filteredRuns.Where(r => regex.IsMatch(r.DisplayTitle ?? r.RunNumber.ToString()));
                }
                catch (ArgumentException)
                {
                    // Invalid regex pattern - skip filtering
                }
            }

            // Note: GitHub Actions does not have a direct equivalent to Azure DevOps "KeepForever" (pinned).
            // filterPinnedOnly is not applicable for GitHub Actions runs.

            var runs = filteredRuns
                .Select(r => new DeployableArtefact
                {
                    Id = r.Id.ToString(),
                    Name = r.DisplayTitle ?? r.RunNumber.ToString(),
                    Date = r.UpdatedAt
                })
                .ToList();

            return runs.OrderByDescending(r => r.Date);
        }

        public async Task<string> GetBuildArtifactDownloadUrlAsync(string serverUrl, string projectPaths,
            string buildRegex, string definitionName, string buildUrl)
        {
            var (owner, repo) = ParseOwnerRepo(serverUrl);

            using var client = CreateHttpClient();

            // buildUrl is the run ID for GitHub Actions
            var runId = buildUrl;

            var artifactsUrl = $"{GetApiBase(serverUrl)}/repos/{owner}/{repo}/actions/runs/{runId}/artifacts";
            var response = await client.GetAsync(artifactsUrl);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var artifactsResponse = JsonSerializer.Deserialize<GitHubArtifactsResponse>(json);

            if (artifactsResponse?.Artifacts == null || artifactsResponse.Artifacts.Count == 0)
                throw new ApplicationException($"No artifacts found for GitHub Actions run {runId}");

            // Return the first artifact's download URL
            return artifactsResponse.Artifacts[0].ArchiveDownloadUrl;
        }

        public async Task<BuildServerBuildInfo?> ValidateBuildAsync(string serverUrl, string projectPaths,
            string buildRegex, string? buildText, string? buildNum, string? vstsUrl, bool pinnedOnly)
        {
            var (owner, repo) = ParseOwnerRepo(serverUrl);

            using var client = CreateHttpClient();

            // For GitHub, buildText is the workflow name
            if (string.IsNullOrEmpty(buildText))
                return null;

            var workflowId = await GetWorkflowIdByNameAsync(client, serverUrl, owner, repo, buildText);
            if (workflowId == null)
                return null;

            var runsUrl = $"{GetApiBase(serverUrl)}/repos/{owner}/{repo}/actions/workflows/{workflowId}/runs?status=completed&per_page=100";
            var response = await client.GetAsync(runsUrl);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var runsResponse = JsonSerializer.Deserialize<GitHubWorkflowRunsResponse>(json);

            if (runsResponse?.WorkflowRuns == null || !runsResponse.WorkflowRuns.Any())
                return null;

            var successfulRuns = runsResponse.WorkflowRuns
                .Where(r => r.Conclusion == "success")
                .ToList();

            if (!successfulRuns.Any())
                return null;

            // If a specific run ID was provided (via vstsUrl or buildUrl), find that specific run
            if (!string.IsNullOrEmpty(vstsUrl))
            {
                var run = successfulRuns.FirstOrDefault(r => r.Id.ToString() == vstsUrl);
                if (run == null) return null;
                return MapRunToInfo(run, buildText);
            }

            // Find by build number (display_title or run_number)
            if (!string.IsNullOrEmpty(buildNum))
            {
                var cleanBuildNum = buildNum.Replace(" [PINNED]", "");

                if (cleanBuildNum.ToLower().Equals("latest"))
                {
                    var latest = successfulRuns.First();
                    return MapRunToInfo(latest, buildText);
                }

                var matchedRun = successfulRuns.FirstOrDefault(r =>
                    (r.DisplayTitle ?? r.RunNumber.ToString()).Trim()
                        .Equals(cleanBuildNum.Trim(), StringComparison.OrdinalIgnoreCase));

                if (matchedRun != null)
                    return MapRunToInfo(matchedRun, buildText);
            }

            return null;
        }

        private static BuildServerBuildInfo MapRunToInfo(GitHubWorkflowRun run, string workflowName)
        {
            return new BuildServerBuildInfo
            {
                BuildUri = run.Id.ToString(),
                ProjectName = workflowName,
                DefinitionName = workflowName,
                BuildId = (int)run.Id,
                BuildNumber = run.DisplayTitle ?? run.RunNumber.ToString()
            };
        }

        private async Task<long?> GetWorkflowIdByNameAsync(HttpClient client, string serverUrl, string owner, string repo, string workflowName)
        {
            var url = $"{GetApiBase(serverUrl)}/repos/{owner}/{repo}/actions/workflows?per_page=100";
            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var workflowsResponse = JsonSerializer.Deserialize<GitHubWorkflowsResponse>(json);

            var workflow = workflowsResponse?.Workflows?.FirstOrDefault(w =>
                (w.Name ?? "").Equals(workflowName, StringComparison.OrdinalIgnoreCase));

            return workflow?.Id;
        }

        private HttpClient CreateHttpClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DORC", "1.0"));
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

            // Fallback: treat last two segments as owner/repo
            if (segments.Length >= 2)
            {
                return (segments[^2], segments[^1]);
            }

            throw new ArgumentException($"Cannot parse owner/repo from URL: {serverUrl}");
        }

        private static string GetApiBase(string serverUrl)
        {
            var uri = new Uri(serverUrl);
            // For api.github.com, return https://api.github.com
            // For GitHub Enterprise, return https://hostname/api/v3
            if (uri.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase))
                return "https://api.github.com";

            return $"{uri.Scheme}://{uri.Host}/api/v3";
        }

        #region GitHub API Response Models

        private class GitHubWorkflow
        {
            [JsonPropertyName("id")]
            public long Id { get; set; }

            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("path")]
            public string? Path { get; set; }

            [JsonPropertyName("state")]
            public string? State { get; set; }
        }

        private class GitHubWorkflowsResponse
        {
            [JsonPropertyName("total_count")]
            public int TotalCount { get; set; }

            [JsonPropertyName("workflows")]
            public List<GitHubWorkflow>? Workflows { get; set; }
        }

        private class GitHubWorkflowRun
        {
            [JsonPropertyName("id")]
            public long Id { get; set; }

            [JsonPropertyName("run_number")]
            public int RunNumber { get; set; }

            [JsonPropertyName("display_title")]
            public string? DisplayTitle { get; set; }

            [JsonPropertyName("conclusion")]
            public string? Conclusion { get; set; }

            [JsonPropertyName("updated_at")]
            public DateTime UpdatedAt { get; set; }

            [JsonPropertyName("html_url")]
            public string? HtmlUrl { get; set; }
        }

        private class GitHubWorkflowRunsResponse
        {
            [JsonPropertyName("total_count")]
            public int TotalCount { get; set; }

            [JsonPropertyName("workflow_runs")]
            public List<GitHubWorkflowRun>? WorkflowRuns { get; set; }
        }

        private class GitHubArtifact
        {
            [JsonPropertyName("id")]
            public long Id { get; set; }

            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("archive_download_url")]
            public string ArchiveDownloadUrl { get; set; } = string.Empty;
        }

        private class GitHubArtifactsResponse
        {
            [JsonPropertyName("total_count")]
            public int TotalCount { get; set; }

            [JsonPropertyName("artifacts")]
            public List<GitHubArtifact>? Artifacts { get; set; }
        }

        #endregion
    }
}
