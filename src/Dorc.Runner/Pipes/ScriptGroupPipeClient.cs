using Dorc.ApiModel;
using Dorc.Runner.Logger;
using Microsoft.Extensions.Logging;
using System.IO.Pipes;
using System.Text.Json;

namespace Dorc.Runner.Pipes
{
    internal class ScriptGroupPipeClient : IScriptGroupPipeClient
    {
        private readonly IRunnerLogger logger;

        internal ScriptGroupPipeClient(IRunnerLogger logger)
        {
            this.logger = logger;
        }

        public ScriptGroup GetScriptGroupProperties(string scriptGroupPipeName)
        {
            this.logger.FileLogger.LogInformation("Pipe name provided for client: '" + scriptGroupPipeName + "'.");

            try
            {
                using (NamedPipeClientStream pipeClient =
                    new NamedPipeClientStream(".",
                    scriptGroupPipeName,
                    PipeDirection.In))
                {
                    this.logger.FileLogger.LogInformation("Connecting client pipe to server. Pipe name: '" + scriptGroupPipeName + "'.");

                    try
                    {
                        pipeClient.Connect();

                        if (pipeClient.IsConnected)
                        {
                            this.logger.FileLogger.LogInformation("Client pipe is connected.");
                        }
                        else
                        {
                            this.logger.Warning("Client pipe is NOT connected.");
                        }
                    }
                    catch (Exception e)
                    {
                        this.logger.Error("Exception is thrown while trying to connect to named pipe sever: " + e);
                        throw;
                    }

                    this.logger.FileLogger.LogInformation("Deserializing received ScriptGroup.");

                    ScriptGroup scriptGroup = JsonSerializer.Deserialize<ScriptGroup>(pipeClient, JsonSerializerOptions.Default);

                    this.logger.FileLogger.LogInformation("Deserialization of ScriptGroup is completed.");

                    return scriptGroup;
                }
            }
            catch (Exception ex)
            {
                this.logger.Error("Client pipe has failed. Exception: " + ex);
                throw;
            }
        }
    }
}
