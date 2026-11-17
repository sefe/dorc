namespace Dorc.TerraformRunner
{
    public interface ITerraformProcessor
    {
        Task<bool> PreparePlanAsync(
            string pipeName,
            int requestId,
            string resultFilePath,
            string planContentFilePath,
            string? lockFilePath,
            CancellationToken cancellationToken,
            bool isDestroy = false);

        Task<bool> ExecuteConfirmedPlanAsync(
            string pipeName,
            int requestId,
            string planFile,
            string? lockFilePath,
            string? appliedResourcesFilePath,
            string? sourceArchiveFilePath,
            CancellationToken cancellationToken,
            bool isDestroy = false);
    }
}