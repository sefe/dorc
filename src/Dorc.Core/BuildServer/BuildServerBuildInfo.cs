namespace Dorc.Core.BuildServer
{
    /// <summary>
    /// Represents the key identifiers of a validated build from any build server platform.
    /// </summary>
    public class BuildServerBuildInfo
    {
        public string BuildUri { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public string DefinitionName { get; set; } = string.Empty;
        public long BuildId { get; set; }
        public string BuildNumber { get; set; } = string.Empty;
        public string ArtifactDownloadUrl { get; set; } = string.Empty;
    }
}
