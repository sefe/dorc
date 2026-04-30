namespace Dorc.ApiModel
{
    /// <summary>
    /// Component-level Terraform code source for a single Terraform component
    /// (drives <c>TerraformCodeSourceProviderFactory.GetProvider</c>). Distinct
    /// from <see cref="SourceControlType"/>, which is project-level — the two
    /// vary independently per component.
    /// </summary>
    public enum TerraformSourceType
    {
        SharedFolder = 0,
        Git = 1,
        AzureArtifact = 2,
        GitHubArtifact = 3
    }
}
