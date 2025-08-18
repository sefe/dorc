using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using System.Text;

namespace Dorc.Terraformmunner
{
    internal interface ITerraformDispatcher
    {
        Task<bool> DispatchAsync(
            ComponentApiModel component,
            DeploymentResultApiModel deploymentResult,
            IDictionary<string, VariableValue> properties,
            int requestId,
            bool isProduction,
            string environmentName,
            string terraformWorkingDir,
            StringBuilder resultLogBuilder,
            CancellationToken cancellationToken);

        Task<bool> ExecuteConfirmedPlanAsync(
            int deploymentResultId,
            CancellationToken cancellationToken);
    }
}