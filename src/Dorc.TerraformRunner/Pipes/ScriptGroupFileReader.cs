using Dorc.ApiModel;
using Dorc.ApiModel.Constants;
using Dorc.ApiModel.MonitorRunnerApi;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Dorc.TerraformmRunner.Pipes
{
    internal class ScriptGroupFileReader: IScriptGroupPipeClient
    {
        private ILogger logger;

        internal ScriptGroupFileReader(ILogger logger)
        {
            this.logger = logger;
        }

        public ScriptGroup GetScriptGroupProperties(string pipeName)
        {
            string filename = $"{RunnerConstants.ScriptGroupFilesPath}{pipeName}.json";
            try
            {
                logger.LogInformation("Deserializing received ScriptGroup.");
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Converters =
                        {
                            new VariableValueJsonConverter(),
                        }
                };
                ScriptGroup? scriptGroup;
                using (FileStream readStream = File.OpenRead(filename))
                {
                    scriptGroup = JsonSerializer.Deserialize<ScriptGroup>(readStream, options);
                }

                if (scriptGroup == null)
                    throw new InvalidOperationException($"Failed to deserialize ScriptGroup from file: {filename}");

                var guid = scriptGroup.ID.ToString();
                var list = scriptGroup.ScriptProperties.ToList();
                var env = scriptGroup.CommonProperties["EnvironmentName"];

                logger.LogInformation($"Received from file: {guid}");
                foreach (var scriptGroupScriptProperty in list)
                {
                    var props = JsonSerializer.Serialize(scriptGroupScriptProperty.Properties);

                    logger.LogInformation($"Asked to execute: {scriptGroupScriptProperty.ScriptPath} for env {env.Value} with properties {props}");
                }

                logger.LogInformation("Deserialization of ScriptGroup is completed.");

                return scriptGroup;
            }
            catch (Exception exc)
            {
                logger.LogError(exc, exc.Message);
                throw;
            }
        }        
    }
}
