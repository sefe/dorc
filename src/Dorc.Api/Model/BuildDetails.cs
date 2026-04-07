using Dorc.ApiModel;

namespace Dorc.Api.Model
{
    public class BuildDetails
    {
        public BuildDetails(string url)
        {
            BuildUrl = url;
        }
        public BuildDetails(RequestDto request, SourceControlType sourceControlType = SourceControlType.AzureDevOps)
        {
            BuildUrl = request.BuildUrl;
            BuildText = request.BuildText;
            BuildNum = request.BuildNum;
            VstsUrl = request.VstsUrl;
            Project = request.Project;
            Pinned = request.Pinned;
            SourceControlType = sourceControlType;
        }

        public BuildType Type => GetBuildType(BuildUrl);
        public string BuildUrl { set; get; }
        public string BuildText { set; get; }
        public string BuildNum { set; get; }
        public string? VstsUrl { set; get; } = default!;
        public string Project { set; get; }
        public bool? Pinned { set; get; }
        public SourceControlType SourceControlType { set; get; }

        private BuildType GetBuildType(string url)
        {
            if (string.IsNullOrEmpty(url)) return BuildType.UnknownBuildType;
            if (url.StartsWith("file", StringComparison.OrdinalIgnoreCase)) return BuildType.FileShareBuild;

            if (SourceControlType == SourceControlType.GitHub)
            {
                // GitHub builds: numeric run IDs or GitHub API URLs
                if (long.TryParse(url, out _)) return BuildType.GitHubBuild;
                if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
                    (url.Contains("github.com", StringComparison.OrdinalIgnoreCase) ||
                     url.Contains("/repos/", StringComparison.OrdinalIgnoreCase)))
                    return BuildType.GitHubBuild;
            }

            if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return BuildType.TfsBuild;
            return BuildType.UnknownBuildType;
        }
    }

    public enum BuildType
    {
        TfsBuild,
        FileShareBuild,
        GitHubBuild,
        UnknownBuildType
    }
}