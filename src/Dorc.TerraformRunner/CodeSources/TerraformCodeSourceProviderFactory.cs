using Dorc.ApiModel;
using Dorc.Runner.Logger;
using Dorc.Terraform.Catalog;

namespace Dorc.TerraformRunner.CodeSources
{
    /// <summary>
    /// Factory for creating Terraform code source providers
    /// </summary>
    public class TerraformCodeSourceProviderFactory
    {
        private readonly IRunnerLogger _logger;
        private readonly Dictionary<TerraformSourceType, ITerraformCodeSourceProvider> _providers;

        public TerraformCodeSourceProviderFactory(IRunnerLogger logger, ITemplateCatalog catalog)
        {
            _logger = logger;
            _providers = new Dictionary<TerraformSourceType, ITerraformCodeSourceProvider>
            {
                { TerraformSourceType.SharedFolder, new SharedFolderCodeSourceProvider(logger) },
                { TerraformSourceType.Git, new GitCodeSourceProvider(logger) },
                { TerraformSourceType.AzureArtifact, new AzureArtifactCodeSourceProvider(logger) },
                { TerraformSourceType.GitHubArtifact, new GitHubArtifactCodeSourceProvider(logger) },
                { TerraformSourceType.Catalog, new CatalogReferenceCodeSourceProvider(logger, catalog) }
            };
        }

        public ITerraformCodeSourceProvider GetProvider(TerraformSourceType sourceType)
        {
            if (_providers.TryGetValue(sourceType, out var provider))
            {
                return provider;
            }

            throw new NotSupportedException($"Terraform source type '{sourceType}' is not supported.");
        }
    }
}
