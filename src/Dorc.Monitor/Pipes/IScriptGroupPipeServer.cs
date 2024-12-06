using Dorc.ApiModel;

namespace Dorc.Monitor.Pipes
{
    public interface IScriptGroupPipeServer
    {
        Task Start(string pipeName, ScriptGroup scriptGroup, CancellationToken cancellationToken);
    }
}
