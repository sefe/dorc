using Dorc.ApiModel;

namespace Dorc.Runner.Pipes
{
    public interface IScriptGroupPipeClient
    {
        ScriptGroup GetScriptGroupProperties(string pipeName);
    }
}
