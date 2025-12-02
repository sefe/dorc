using Dorc.ApiModel;

namespace Dorc.TerraformmRunner.CodeSources
{
    /// <summary>
    /// Interface for Terraform code source providers
    /// </summary>
    public interface ITerraformCodeSourceProvider
    {
        /// <summary>
        /// Gets the source type supported by this provider
        /// </summary>
        TerraformSourceType SourceType { get; }

        /// <summary>
        /// Downloads or copies Terraform code to the specified working directory
        /// </summary>
        /// <param name="scriptGroup">Script group containing source configuration</param>
        /// <param name="scriptPath">Script path (used for SharedFolder type)</param>
        /// <param name="workingDir">Target directory for Terraform code</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task ProvisionCodeAsync(ScriptGroup scriptGroup, string scriptPath, string workingDir, CancellationToken cancellationToken);
    }
}
