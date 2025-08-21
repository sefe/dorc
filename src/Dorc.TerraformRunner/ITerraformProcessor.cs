using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using System.Text;

namespace Dorc.TerraformmRunner
{
    internal interface ITerraformProcessor
    {
        Task<bool> DispatchAsync(
            ComponentApiModel component,
            DeploymentResultApiModel deploymentResult,
            IDictionary<string, VariableValue> properties,
            int requestId,
            bool isProduction,
            string environmentName,
            string scriptRoot,
            StringBuilder resultLogBuilder,
            CancellationToken cancellationToken);

        Task<bool> ExecuteConfirmedPlanAsync(
            int deploymentResultId,
            CancellationToken cancellationToken);
    }
}