using System.Text.Json.Serialization;

namespace Dorc.TerraformRunner.CodeSources.GitHubApi
{
    internal class GitHubArtifactsListResponse
    {
        [JsonPropertyName("total_count")]
        public int TotalCount { get; set; }

        [JsonPropertyName("artifacts")]
        public List<GitHubArtifactItem>? Artifacts { get; set; }
    }
}
