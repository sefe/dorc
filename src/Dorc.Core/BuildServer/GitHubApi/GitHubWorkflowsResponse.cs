using System.Text.Json.Serialization;

namespace Dorc.Core.BuildServer.GitHubApi
{
    internal class GitHubWorkflowsResponse
    {
        [JsonPropertyName("total_count")]
        public int TotalCount { get; set; }

        [JsonPropertyName("workflows")]
        public List<GitHubWorkflow>? Workflows { get; set; }
    }
}
