using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using System.Text;

namespace Dorc.Monitor
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
            StringBuilder resultLogBuilder,
            CancellationToken cancellationToken);
    }
}