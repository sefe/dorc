using System;
using System.IO.Pipes;
using System.Linq;
using System.Text.Json;
using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Serilog;

namespace Dorc.NetFramework.Runner.Pipes
{
    internal class ScriptGroupPipeClient : IScriptGroupPipeClient
    {
        private readonly ILogger logger;

        internal ScriptGroupPipeClient(ILogger logger)
        {
            this.logger = logger;
        }

        public ScriptGroup GetScriptGroupProperties(string scriptGroupPipeName)
        {
            logger.Information("Pipe name provided for client: '" + scriptGroupPipeName + "'.");

            try
            {
                using (NamedPipeClientStream pipeClient =
                    new NamedPipeClientStream(".",
                    scriptGroupPipeName,
                    PipeDirection.In))
                {
                    logger.Information("Connecting client pipe to server. Pipe name: '" + scriptGroupPipeName + "'.");

                    try
                    {
                        pipeClient.Connect();

                        if (pipeClient.IsConnected)
                        {
                            logger.Information("Client pipe is connected.");
                        }
                        else
                        {
                            logger.Warning("Client pipe is NOT connected.");
                        }
                    }
                    catch (Exception e)
                    {
                        logger.Error("Exception is thrown while trying to connect to named pipe sever: {0} " , e);
                        throw;
                    }

                    logger.Information("Deserializing received ScriptGroup.");
                    var options = new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Converters =
                        {
                            new VariableValueJsonConverter(),
                        }
                    };

                    ScriptGroup scriptGroup = JsonSerializer.Deserialize<ScriptGroup>(pipeClient, options);

                    var guid = scriptGroup.ID.ToString();
                    var list = scriptGroup.ScriptProperties.ToList();
                    var env = scriptGroup.CommonProperties["EnvironmentName"];

                    logger.Information($"Received from pipe: {guid}");
                    foreach (var scriptGroupScriptProperty in list)
                    {
                        var props =JsonSerializer.Serialize(scriptGroupScriptProperty.Properties);

                        logger.Information($"Asked to execute: {scriptGroupScriptProperty.ScriptPath} for env {env.Value} with properties {props}");
                    }

                    logger.Information("Deserialization of ScriptGroup is completed.");

                    return scriptGroup;
                }
            }
            catch (Exception ex)
            {
                logger.Error("Client pipe has failed. Exception: " + ex);
                throw;
            }
        }
    }
}
