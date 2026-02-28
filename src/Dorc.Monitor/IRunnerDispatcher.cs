using Dorc.ApiModel;

namespace Dorc.Monitor
{
    public interface IRunnerDispatcher
    {
        int StartRunner(
            string pipeName,
            string runnerLogPath,
            ScriptGroup scriptGroup,
            CancellationToken cancellationToken);
    }
}
