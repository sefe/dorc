using Dorc.ApiModel;
using Dorc.ApiModel.Constants;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Runner.Logger;
using System.Text.Json;

namespace Dorc.Runner.Pipes
{
    internal class ScriptGroupFileReader : IScriptGroupPipeClient
    {
        private IRunnerLogger logger;

        internal ScriptGroupFileReader(IRunnerLogger logger)
        {
            this.logger = logger;
        }

        public ScriptGroup GetScriptGroupProperties(string pipeName)
        {
            string filename = $"{RunnerConstants.ScriptGroupFilesPath}{pipeName}.json";
            try
            {
                logger.Information("Deserializing received ScriptGroup.");
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Converters =
                        {
                            new VariableValueJsonConverter(),
                        }
                };
                ScriptGroup scriptGroup;
                using (FileStream readStream = File.OpenRead(filename))
                {
                    scriptGroup = JsonSerializer.Deserialize<ScriptGroup>(readStream, options);
                }

                var guid = scriptGroup.ID.ToString();
                var list = scriptGroup.ScriptProperties.ToList();
                var env = scriptGroup.CommonProperties["EnvironmentName"];

                logger.Information($"Received from file: {guid}");
                foreach (var scriptGroupScriptProperty in list)
                {
                    var props = JsonSerializer.Serialize(scriptGroupScriptProperty.Properties);

                    logger.Information($"Asked to execute: {scriptGroupScriptProperty.ScriptPath} for env {env.Value} with properties {props}");
                }

                logger.Information("Deserialization of ScriptGroup is completed.");

                return scriptGroup;
            }
            catch (Exception exc)
            {
                logger.Error(exc.Message);
                throw;
            }
        }
    }
}
