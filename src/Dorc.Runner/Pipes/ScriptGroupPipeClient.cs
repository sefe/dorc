using Dorc.ApiModel;
using Dorc.Runner.Logger;
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
            this.logger.Information("Pipe name provided for client: '" + scriptGroupPipeName + "'.");

            try
            {
                using (NamedPipeClientStream pipeClient =
                    new NamedPipeClientStream(".",
                    scriptGroupPipeName,
                    PipeDirection.In))
                {
                    this.logger.Information("Connecting client pipe to server. Pipe name: '" + scriptGroupPipeName + "'.");

                    try
                    {
                        pipeClient.Connect();

                        if (pipeClient.IsConnected)
                        {
                            this.logger.Information("Client pipe is connected.");
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

                    this.logger.Information("Deserializing received ScriptGroup.");

                    ScriptGroup scriptGroup = JsonSerializer.Deserialize<ScriptGroup>(pipeClient, JsonSerializerOptions.Default);

                    this.logger.Information("Deserialization of ScriptGroup is completed.");

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
