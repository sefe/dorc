using System.Text.Json.Serialization;

namespace Dorc.Core.BuildServer.GitHubApi
{
    internal class GitHubWorkflowRunsResponse
    {
        [JsonPropertyName("total_count")]
        public int TotalCount { get; set; }

        [JsonPropertyName("workflow_runs")]
        public List<GitHubWorkflowRun>? WorkflowRuns { get; set; }
    }
}
