using Dorc.ApiModel;
using Dorc.ApiModel.Constants;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Dorc.ApiModel.MonitorRunnerApi;

namespace Dorc.Monitor.Pipes
{
    internal class ScriptGroupFileWriter : IScriptGroupPipeServer
    {
        private ILogger logger;

        public ScriptGroupFileWriter(ILogger<ScriptGroupFileWriter> logger)
        {
            this.logger = logger;
        }

        public Task Start(string pipeName, ScriptGroup scriptGroup, CancellationToken cancellationToken)
        {
            string filesPath = RunnerConstants.ScriptGroupFilesPath;
            string filename = $"{filesPath}{pipeName}.json";
            try
            {
                bool exists = Directory.Exists(filesPath);

                if (!exists)
                    Directory.CreateDirectory(filesPath);

                var serializeOptions = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Converters =
                                {
                                    new VariableValueJsonConverter(),
                                }
                };

                using FileStream createStream = File.Create(filename);

                JsonSerializer.Serialize(createStream, scriptGroup, serializeOptions);
                
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                logger.LogError($"File creation has failed. File name: '{filename}'. Exception: {ex}");
                throw;
            }
        }
    }
}
