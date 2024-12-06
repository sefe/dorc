using Dorc.ApiModel;

namespace Dorc.NetFramework.Runner.Pipes
{
    internal interface IScriptGroupPipeClient
    {
        ScriptGroup GetScriptGroupProperties(string pipeName);
    }
}
