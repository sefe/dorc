using Dorc.ApiModel.MonitorRunnerApi;

namespace Dorc.PowerShell
{
    public interface IPowerShellScriptRunner
    {
        int Run(string scriptsLocation,
            string scriptName,
            IDictionary<string, VariableValue> scriptProperties,
            IDictionary<string, VariableValue> commonProperties
        );
    }
}