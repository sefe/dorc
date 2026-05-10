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

        // sentinel scheme. Catalog-only deploy requests have no build
        // artifact; the controller substitutes this prefix for the empty
        // BuildUrl after verifying every component in the request is
        // Catalog-mode. The sentinel keeps RequestService.CheckRequest's
        // ArtefactsUrl-fallback bypassed (it only fires on empty URLs) and
        // routes the request to CatalogDeployableBuild via the factory.
        public const string CatalogSentinel = "dorc-catalog://";

        private BuildType GetBuildType(string url)
        {
            if (string.IsNullOrEmpty(url)) return BuildType.UnknownBuildType;
            if (url.StartsWith(CatalogSentinel, StringComparison.OrdinalIgnoreCase)) return BuildType.Catalog;
            if (url.ToLower().StartsWith("http")) return BuildType.TfsBuild;
            if (url.ToLower().StartsWith("file")) return BuildType.FileShareBuild;
            return BuildType.UnknownBuildType;
        }
    }

    public enum BuildType
    {
        TfsBuild,
        FileShareBuild,
        UnknownBuildType,
        // Catalog-mode deploy requests use a sentinel BuildUrl
        // (BuildDetails.CatalogSentinel) and dispatch via CatalogDeployableBuild.
        Catalog
    }
}