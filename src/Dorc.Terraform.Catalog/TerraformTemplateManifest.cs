namespace Dorc.Terraform.Catalog
{
    // Manifest for one published version of a stock template.
    // Resolved by ITemplateCatalog implementations (e.g. GitTemplateCatalog).
    // Schema mirrored as docs/Terraform/MANIFEST-SCHEMA.json (planned).
    // additive fields: TerraformTemplateSource.SubPath defaults to null 
    // the resolver falls back to stock-modules/<Name>. TerraformTemplateParameter.Sensitive
    // defaults to false.
    public sealed record TerraformTemplateManifest(
        string Name,
        string Version,
        TerraformTemplateSource Source,
        IReadOnlyList<TerraformTemplateParameter> Parameters,
        IReadOnlyList<TerraformTemplateOutput> Outputs,
        string? Description,
        IReadOnlyList<string> Tags,
        string? Category,
        IReadOnlyDictionary<string, string> RequiredProviders,
        string RequiredTerraformVersion,
        string? Owner,
        bool Deprecated,
        string? DeprecationReason);

    public sealed record TerraformTemplateSource(
        string Kind,
        string Locator,
        string Ref,
        string? SubPath = null);

    public sealed record TerraformTemplateParameter(
        string Name,
        TerraformParameterType Type,
        bool Required,
        string? Description,
        string? Default,
        IReadOnlyList<string>? AllowedValues,
        string? Pattern,
        decimal? Min,
        decimal? Max,
        bool Sensitive = false);

    public sealed record TerraformTemplateOutput(
        string Name,
        TerraformParameterType Type,
        string? Description,
        bool Sensitive);

    public enum TerraformParameterType
    {
        String,
        Number,
        Bool,
        List,
        Map,
        Object,
    }
}
