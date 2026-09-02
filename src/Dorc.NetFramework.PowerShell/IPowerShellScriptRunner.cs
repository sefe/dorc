using System.Collections.Generic;
using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;

namespace Dorc.NetFramework.PowerShell
{
    public interface IPowerShellScriptRunner
    {
        void Run(string scriptsLocation,
            IEnumerable<(string, IDictionary<string, VariableValue>, string)> scripts,
            IDictionary<string, VariableValue> commonProperties,
            ScriptContentVerificationMode contentVerification
        );
    }
}