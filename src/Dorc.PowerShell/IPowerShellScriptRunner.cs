using System.Collections.Generic;
using Dorc.ApiModel.MonitorRunnerApi;

namespace Dorc.PowerShell
{
    public interface IPowerShellScriptRunner
    {
        void Run(string scriptsLocation,
            IEnumerable<(string, IDictionary<string, VariableValue>)> scripts,
            IDictionary<string, VariableValue> commonProperties
        );
    }
}