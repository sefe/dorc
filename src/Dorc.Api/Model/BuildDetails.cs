using Dorc.ApiModel;

namespace Dorc.Api.Model
{
    public class BuildDetails
    {
        public BuildDetails(string url)
        {
            BuildUrl = url;
        }
        public BuildDetails(RequestDto request)
        {
            BuildUrl = request.BuildUrl;
            BuildText = request.BuildText;
            BuildNum = request.BuildNum;
            VstsUrl = request.VstsUrl;
            Project = request.Project;
            Pinned = request.Pinned;
        }

        public BuildType Type => GetBuildType(BuildUrl);
        public string BuildUrl { set; get; }
        public string BuildText { set; get; }
        public string BuildNum { set; get; }
        public string? VstsUrl { set; get; } = default!;
        public string Project { set; get; }
        public bool? Pinned { set; get; }

        private BuildType GetBuildType(string url)
        {
            if (string.IsNullOrEmpty(url)) return BuildType.UnknownBuildType;
            if (url.ToLower().StartsWith("http")) return BuildType.TfsBuild;
            if (url.ToLower().StartsWith("file")) return BuildType.FileShareBuild;
            return BuildType.UnknownBuildType;
        }
    }

    public enum BuildType
    {
        TfsBuild,
        FileShareBuild,
        UnknownBuildType
    }
}