using System.Text.Json.Serialization;

namespace Dorc.TerraformRunner.CodeSources.GitHubApi
{
    internal class GitHubArtifactItem
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("archive_download_url")]
        public string ArchiveDownloadUrl { get; set; } = string.Empty;
    }
}
