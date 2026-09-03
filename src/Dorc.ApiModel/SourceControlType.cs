namespace Dorc.ApiModel
{
    /// <summary>
    /// Project-level CI/CD backend (drives <c>BuildServerClientFactory.Create</c>
    /// and <c>BuildDetails.GetBuildType</c>). Distinct from
    /// <see cref="TerraformSourceType"/>, which is component-level — overlapping
    /// value names are vendor coincidence, not shared concept.
    /// </summary>
    public enum SourceControlType
    {
        AzureDevOps = 0,
        GitHub = 1,
        FileShare = 2
    }
}
