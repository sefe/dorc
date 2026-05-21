using System.Text.Json.Serialization;

namespace Dorc.Core.BuildServer.GitHubApi
{
    internal class GitHubArtifactsResponse
    {
        [JsonPropertyName("total_count")]
        public int TotalCount { get; set; }

        [JsonPropertyName("artifacts")]
        public List<GitHubArtifact>? Artifacts { get; set; }
    }
}
