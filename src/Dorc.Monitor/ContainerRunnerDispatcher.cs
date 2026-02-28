using Dorc.ApiModel;
using Dorc.ApiModel.Constants;
using Dorc.Monitor.Pipes;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Dorc.Monitor
{
    /// <summary>
    /// Dispatches runner work in container mode by writing the script group to a shared
    /// volume (Azure Files) and invoking the runner as an ACA Job via az CLI or HTTP trigger.
    /// The runner reads the script group JSON from the shared volume using file-based IPC.
    /// </summary>
    public class ContainerRunnerDispatcher : IRunnerDispatcher
    {
        private readonly ILogger<ContainerRunnerDispatcher> _logger;
        private readonly IScriptGroupPipeServer _scriptGroupWriter;

        public ContainerRunnerDispatcher(
            ILogger<ContainerRunnerDispatcher> logger,
            IScriptGroupPipeServer scriptGroupWriter)
        {
            _logger = logger;
            _scriptGroupWriter = scriptGroupWriter;
        }

        public int StartRunner(
            string pipeName,
            string runnerLogPath,
            ScriptGroup scriptGroup,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Write script group JSON to shared volume using the file writer
            _logger.LogInformation("Container mode: writing script group to shared volume for pipe name '{PipeName}'.", pipeName);
            _scriptGroupWriter.Start(pipeName, scriptGroup, cancellationToken).GetAwaiter().GetResult();

            _logger.LogInformation("Container mode: script group written successfully. Runner will pick up work from shared volume.");

            // In container mode, the runner is started as an ACA Job externally.
            // Return 0 to indicate the dispatch was successful.
            // The actual runner process is managed by the container orchestrator.
            return 0;
        }
    }
}
