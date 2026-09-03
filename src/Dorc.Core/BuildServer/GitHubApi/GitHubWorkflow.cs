using System.Text.Json.Serialization;

namespace Dorc.Core.BuildServer.GitHubApi
{
    internal class GitHubWorkflow
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
}
