using Dorc.ApiModel;

namespace Dorc.TerraformmRunner.Pipes
{
    internal interface IScriptGroupPipeClient
    {
        ScriptGroup GetScriptGroupProperties(string pipeName);
    }
}
