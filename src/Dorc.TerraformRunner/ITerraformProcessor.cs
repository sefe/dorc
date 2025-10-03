namespace Dorc.TerraformmRunner
{
    internal interface ITerraformProcessor
    {
        Task<bool> PreparePlanAsync(
            string pipeName,
            int requestId,
            string scriptPath,
            string resultFilePath,
            CancellationToken cancellationToken);

        Task<bool> ExecuteConfirmedPlanAsync(
            string pipeName,
            int requestId,
            string scriptPath,
            string planFile,
            CancellationToken cancellationToken);
    }
}