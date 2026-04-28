using Dorc.Core.Models;

namespace Dorc.Core.BuildServer
{
    /// <summary>
    /// Platform-agnostic interface for interacting with a CI/CD build server.
    /// Implementations exist for Azure DevOps and GitHub Actions.
    /// </summary>
    public interface IBuildServerClient
    {
        /// <summary>
        /// Gets workflow/pipeline definitions matching the given project and regex filter.
        /// </summary>
        IEnumerable<DeployableArtefact> GetBuildDefinitions(string serverUrl, string projectPaths, string buildRegex);

        /// <summary>
        /// Gets completed builds/runs for a specific workflow/pipeline definition.
        /// </summary>
        Task<IEnumerable<DeployableArtefact>> GetBuildsAsync(string serverUrl, string projectPaths, string buildRegex,
            string definitionName, bool filterPinnedOnly);

        /// <summary>
        /// Gets the artifact download URL for a specific build.
        /// </summary>
        Task<string> GetBuildArtifactDownloadUrlAsync(string serverUrl, string projectPaths, string buildRegex,
            string definitionName, string buildUrl);

        /// <summary>
        /// Validates that a build exists and returns its URI identifier.
        /// Returns null if the build cannot be found.
        /// </summary>
        Task<BuildServerBuildInfo?> ValidateBuildAsync(string serverUrl, string projectPaths, string buildRegex,
            string? buildText, string? buildNum, string? vstsUrl, bool pinnedOnly);
    }
}
