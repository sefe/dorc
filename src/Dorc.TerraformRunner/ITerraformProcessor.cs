using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using System.Text;

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
            int deploymentResultId,
            CancellationToken cancellationToken);
    }
}