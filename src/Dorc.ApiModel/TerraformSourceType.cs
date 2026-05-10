namespace Dorc.ApiModel
{
    public enum TerraformSourceType
    {
        SharedFolder = 0,
        Git = 1,
        AzureArtifact = 2,
        // S-008: component references a stock template from the catalog by
        // (TerraformTemplateName, TerraformTemplateVersion). The runner
        // resolves the manifest at provisioning time and delegates fetch to
        // the underlying source kind (typically Git, pinned at the manifest's
        // source ref). Mutually exclusive with ScriptPath - enforced by
        // TerraformExclusivityValidator at component-save time (S-009).
        Catalog = 3
    }
}
