using System.ComponentModel.DataAnnotations;

namespace Dorc.Terraform.Catalog
{
    // Manifest for one published version of a stock template.
    // Resolved by ITemplateCatalog implementations (e.g. GitTemplateCatalog).
    // Schema validation lives in GitTemplateCatalog and the CI release gate.
    // additive fields: TerraformTemplateSource.SubPath defaults to null 
    // the resolver falls back to stock-modules/<Name>. TerraformTemplateParameter.Sensitive
    // defaults to false.
    public sealed record TerraformTemplateManifest(
        [property: Required] string Name,
        [property: Required] string Version,
        [property: Required] TerraformTemplateSource Source,
        [property: Required] IReadOnlyList<TerraformTemplateParameter> Parameters,
        [property: Required] IReadOnlyList<TerraformTemplateOutput> Outputs,
        string? Description,
        [property: Required] IReadOnlyList<string> Tags,
        string? Category,
        [property: Required] IReadOnlyDictionary<string, string> RequiredProviders,
        [property: Required] string RequiredTerraformVersion,
        string? Owner,
        [property: Required] bool Deprecated,
        string? DeprecationReason,
        IReadOnlyList<TerraformParameterConflict>? ParameterConflicts = null);

    public sealed record TerraformParameterConflict(
        [property: Required] string Parameter,
        [property: Required] string Value,
        [property: Required] string WhenParameter,
        [property: Required] string WhenValue,
        [property: Required] string Message);

    public sealed record TerraformTemplateSource(
        [property: Required] string Kind,
        [property: Required] string Locator,
        [property: Required] string Ref,
        string? SubPath = null,
        string? CommitSha = null);

    public sealed record TerraformTemplateParameter(
        [property: Required] string Name,
        [property: Required] TerraformParameterType Type,
        [property: Required] bool Required,
        string? Description,
        string? Default,
        IReadOnlyList<string>? AllowedValues,
        string? Pattern,
        decimal? Min,
        decimal? Max,
        [property: Required] bool Sensitive = false,
        bool Integer = false,
        // Optional context-suggestion hint (see CatalogInputSuggestions); when
        // absent the parameter name selects the rule. Never applied to Sensitive inputs.
        string? Suggest = null);

    public sealed record TerraformTemplateOutput(
        [property: Required] string Name,
        [property: Required] TerraformParameterType Type,
        string? Description,
        [property: Required] bool Sensitive);

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
