using System;
using System.IO.Pipes;
using System.Linq;
using System.Text.Json;
using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.TerraformRunner.Logging;
using Microsoft.Extensions.Logging;

namespace Dorc.TerraformRunner.Pipes
{
    internal class ScriptGroupPipeClient : IScriptGroupPipeClient
    {
        private readonly ILogger logger;
        private readonly SensitivePropertyRedactor redactor;

        internal ScriptGroupPipeClient(ILogger logger)
            : this(logger, new SensitivePropertyRedactor(SensitivePropertyRedactionOptions.Default()))
        {
        }

        internal ScriptGroupPipeClient(ILogger logger, SensitivePropertyRedactor redactor)
        {
            this.logger = logger;
            this.redactor = redactor;
        }

        public ScriptGroup GetScriptGroupProperties(string scriptGroupPipeName)
        {
            logger.LogInformation("Pipe name provided for client: '" + scriptGroupPipeName + "'.");

            try
            {
                using (NamedPipeClientStream pipeClient =
                    new NamedPipeClientStream(".",
                    scriptGroupPipeName,
                    PipeDirection.In))
                {
                    logger.LogInformation("Connecting client pipe to server. Pipe name: '" + scriptGroupPipeName + "'.");

                    try
                    {
                        pipeClient.Connect();

                        if (pipeClient.IsConnected)
                        {
                            logger.LogInformation("Client pipe is connected.");
                        }
                        else
                        {
                            logger.LogWarning("Client pipe is NOT connected.");
                        }
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, "Exception is thrown while trying to connect to named pipe sever: {0} ");
                        throw;
                    }

                    logger.LogInformation("Deserializing received ScriptGroup.");
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

                    logger.LogInformation($"Received from pipe: {guid}");
                    foreach (var scriptGroupScriptProperty in list)
                    {
                        var props = redactor.RedactJson(JsonSerializer.Serialize(scriptGroupScriptProperty.Properties));

                        logger.LogInformation($"Asked to execute: {scriptGroupScriptProperty.ScriptPath} for env {env.Value} with properties {props}");
                    }

                    logger.LogInformation("Deserialization of ScriptGroup is completed.");

                    return scriptGroup;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Client pipe has failed. Exception: ");
                throw;
            }
        }
    }
}
