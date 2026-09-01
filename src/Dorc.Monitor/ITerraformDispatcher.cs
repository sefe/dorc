using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Monitor.RunnerProcess;
using System.Text;

namespace Dorc.Monitor
{
    internal interface ITerraformDispatcher
    {
        bool Dispatch(
            ComponentApiModel component,
            DeploymentResultApiModel deploymentResult,
            IDictionary<string, VariableValue> properties,
            int requestId,
            bool isProduction,
            string environmentName,
            string? executionIdentityReference,
            RequestExecutionIdentityAdoption identityAdoption,
            StringBuilder resultLogBuilder,
            TerraformRunnerOperations terreformOperation,
            CancellationToken cancellationToken);
    }
}