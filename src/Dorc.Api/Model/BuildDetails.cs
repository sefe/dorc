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
            if (SourceControlType == SourceControlType.FileShare) return BuildType.FileShareBuild;

            // SourceControlType is the authoritative classifier when set explicitly. URL-shape
            // sniffing below remains as a fallback for projects that haven't been migrated to
            // the new column. GitHub Enterprise URLs ("https://github.acme.local/owner/repo/
            // actions/runs/12345") contain neither "github.com" nor "/repos/" so the legacy
            // sniff would mis-route them to TfsBuild.
            if (SourceControlType == SourceControlType.GitHub)
            {
                if (long.TryParse(url, out _)) return BuildType.GitHubBuild;
                if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
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