using System.Text.Json.Serialization;

namespace Dorc.Core.BuildServer.GitHubApi
{
    internal class GitHubWorkflowRun
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
}
