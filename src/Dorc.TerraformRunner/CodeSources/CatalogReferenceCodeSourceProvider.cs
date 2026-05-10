using Dorc.ApiModel;
using Dorc.Runner.Logger;

namespace Dorc.TerraformRunner.CodeSources
{
    // S-008 (scaffold): resolves a stock-template reference (catalog name +
    // version) into a Git source pin and delegates to GitCodeSourceProvider.
    // The catalog itself is resolved via Dorc.Terraform.Catalog -
    // GitTemplateCatalog reads YAML manifests from a configured local
    // directory.
    //
    // This commit ships the provider entry point plumbing; the catalog
    // configuration (manifests directory location, runtime DI of
    // ITemplateCatalog into the runner) is the follow-up under S-006d's
    // consolidated lifecycle owner. Until then ProvisionCodeAsync throws a
    // clear "not yet configured" error that points engineers at the
    // configuration knob.
    public class CatalogReferenceCodeSourceProvider : ITerraformCodeSourceProvider
    {
        private readonly IRunnerLogger _logger;

        public CatalogReferenceCodeSourceProvider(IRunnerLogger logger)
        {
            _logger = logger;
        }

        public Task<bool> ProvisionCodeAsync(
            ScriptGroup scriptGroup,
            string workingDir,
            CancellationToken cancellationToken)
        {
            _logger.Error(
                "Catalog source mode is set on this component but the catalog " +
                "runtime is not yet configured in this runner. Configure " +
                "Terraform:Catalog:ManifestsDirectory in appsettings.json and " +
                "supply the catalog DI binding (S-008 follow-up).");
            throw new NotSupportedException(
                "Terraform catalog source mode is configured on the component but " +
                "the runner cannot resolve catalog manifests yet. Set " +
                "Terraform:Catalog:ManifestsDirectory in the runner's " +
                "appsettings.json and re-deploy. Until then, switch the " +
                "component to Git/SharedFolder/AzureArtifact source.");
        }
    }
}
