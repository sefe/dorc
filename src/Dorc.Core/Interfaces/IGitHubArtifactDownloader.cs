namespace Dorc.Core.Interfaces
{
    /// <summary>
    /// Downloads and extracts GitHub Actions artifacts from HTTPS URLs
    /// to local file system paths that PowerShell deployment scripts can use.
    /// </summary>
    public interface IGitHubArtifactDownloader
    {
        /// <summary>
        /// Returns true if the given URL is a GitHub Actions artifact download URL.
        /// </summary>
        bool IsGitHubArtifactUrl(string url);

        /// <summary>
        /// Downloads the artifact zip from the given URL and extracts it to a local
        /// temp directory. Returns the root path suitable for use as a DropFolder
        /// (extracted contents are placed in a "drop" subfolder).
        /// </summary>
        string DownloadAndExtract(string artifactUrl);

        /// <summary>
        /// Cleans up a previously extracted artifact directory.
        /// </summary>
        void Cleanup(string extractedPath);
    }
}
