namespace Dorc.TerraformRunner
{
    internal interface ITerraformProcessor
    {
        Task<bool> PreparePlanAsync(
            string pipeName,
            int requestId,
            string resultFilePath,
            string planContentFilePath,
            string? lockFilePath,
            CancellationToken cancellationToken);

        Task<bool> ExecuteConfirmedPlanAsync(
            string pipeName,
            int requestId,
            string planFile,
            string? lockFilePath,
            CancellationToken cancellationToken);
    }
}