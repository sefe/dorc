using Dorc.ApiModel;
using Dorc.ApiModel.Constants;
using log4net;
using System.Text.Json;
using Dorc.ApiModel.MonitorRunnerApi;

namespace Dorc.Monitor.Pipes
{
    internal class ScriptGroupFileWriter : IScriptGroupPipeServer
    {
        private ILog logger;

        public ScriptGroupFileWriter(ILog logger)
        {
            this.logger = logger;
        }

        public async Task Start(string pipeName, ScriptGroup scriptGroup, CancellationToken cancellationToken)
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

                await using FileStream createStream = File.Create(filename);
                await JsonSerializer.SerializeAsync(createStream, scriptGroup, serializeOptions, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.Error($"File creation has failed. File name: '{filename}'. Exception: {ex}");
                throw;
            }
        }
    }
}
