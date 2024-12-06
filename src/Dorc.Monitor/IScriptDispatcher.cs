using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using System.Text;

namespace Dorc.Monitor
{
    internal interface IScriptDispatcher
    {
        bool Dispatch(string scriptsLocation,
            ScriptApiModel script,
            IDictionary<string, VariableValue> properties,
            int requestId,
            int deploymentRequestId,
            bool isProduction,
            string environmentName,
            StringBuilder resultLogBuilder,
            CancellationToken cancellationToken);
    }
}
