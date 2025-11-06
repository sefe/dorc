using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;

namespace Dorc.Monitor
{
    internal interface IComponentProcessor
    {
        bool DeployComponent(ComponentApiModel component,
            DeploymentResultApiModel deploymentResult,
            int requestId,
            bool isProductionRequest,
            int environmentId,
            bool isProductionEnvironment,
            string environmentName,
            string scriptRoot,
            IDictionary<string, VariableValue> commonProperties,
            CancellationToken cancellationToken);
    }
}
