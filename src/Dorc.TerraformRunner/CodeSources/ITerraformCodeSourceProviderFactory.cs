using Dorc.ApiModel;

namespace Dorc.TerraformRunner.CodeSources;

public interface ITerraformCodeSourceProviderFactory
{
    ITerraformCodeSourceProvider GetProvider(TerraformSourceType sourceType);
}
