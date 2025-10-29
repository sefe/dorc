using System.IO.Pipes;
using System.Text.Json;
using Dorc.ApiModel;
using Microsoft.Extensions.Logging;

namespace Dorc.Runner.Pipes
{
    internal class ScriptGroupPipeClient : IScriptGroupPipeClient
    {
        private readonly ILogger logger;

        internal ScriptGroupPipeClient(ILogger<Program> logger)
        {
            this.logger = logger;
        }

        public ScriptGroup GetScriptGroupProperties(string scriptGroupPipeName)
        {
            this.logger.LogInformation("Pipe name provided for client: '" + scriptGroupPipeName + "'.");

            try
            {
                using (NamedPipeClientStream pipeClient =
                    new NamedPipeClientStream(".",
                    scriptGroupPipeName,
                    PipeDirection.In))
                {
                    this.logger.LogInformation("Connecting client pipe to server. Pipe name: '" + scriptGroupPipeName + "'.");

                    try
                    {
                        pipeClient.Connect();

                        if (pipeClient.IsConnected)
                        {
                            this.logger.LogInformation("Client pipe is connected.");
                        }
                        else
                        {
                            this.logger.LogWarning("Client pipe is NOT connected.");
                        }
                    }
                    catch (Exception e)
                    {
                        this.logger.LogError("Exception is thrown while trying to connect to named pipe sever: " + e);
                        throw;
                    }

                    this.logger.LogInformation("Deserializing received ScriptGroup.");

                    ScriptGroup scriptGroup = JsonSerializer.Deserialize<ScriptGroup>(pipeClient, JsonSerializerOptions.Default);

                    this.logger.LogInformation("Deserialization of ScriptGroup is completed.");

                    return scriptGroup;
                }
            }
            catch (Exception ex)
            {
                this.logger.LogError("Client pipe has failed. Exception: " + ex);
                throw;
            }
        }
    }
}
