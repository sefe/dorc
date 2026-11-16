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
        GitHubArtifact = 3,
        // component references a stock template from the catalog by
        // (TerraformTemplateName, TerraformTemplateVersion). The runner
        // resolves the manifest at provisioning time and delegates fetch to
        // the underlying source kind (typically Git, pinned at the manifest's
        // source ref). Mutually exclusive with ScriptPath - enforced by
        // TerraformExclusivityValidator at component-save time.
        Catalog = 4
    }
}
