using Dorc.ApiModel;

namespace Dorc.TerraformRunner.Pipes
{
    internal interface IScriptGroupPipeClient
    {
        ScriptGroup GetScriptGroupProperties(string pipeName);
    }
}
