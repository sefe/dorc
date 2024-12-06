using Dorc.ApiModel;

namespace Dorc.Runner.Pipes
{
    internal interface IScriptGroupPipeClient
    {
        ScriptGroup GetScriptGroupProperties(string pipeName);
    }
}
