namespace Dorc.TerraformRunner
{
    internal interface ITerraformProcessor
    {
        Task<bool> PreparePlanAsync(
            string pipeName,
            int requestId,
            string resultFilePath,
            string planContentFilePath,
            CancellationToken cancellationToken);

        Task<bool> ExecuteConfirmedPlanAsync(
            string pipeName,
            int requestId,
            string planFile,
            CancellationToken cancellationToken);
    }
}