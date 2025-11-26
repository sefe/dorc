namespace Dorc.Core.TerraformSources
{
    /// <summary>
    /// Interface for retrieving Terraform code from various sources
    /// </summary>
    public interface ITerraformSourceProvider
    {
        /// <summary>
        /// Retrieves Terraform code to a working directory
        /// </summary>
        /// <param name="workingDirectory">The directory where Terraform code should be placed</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> RetrieveSourceAsync(string workingDirectory, CancellationToken cancellationToken);
    }
}
