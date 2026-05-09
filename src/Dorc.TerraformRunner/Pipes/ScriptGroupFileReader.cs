using Dorc.ApiModel;
using Dorc.ApiModel.Constants;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.TerraformRunner.Logging;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Dorc.TerraformRunner.Pipes
{
    internal class ScriptGroupFileReader: IScriptGroupPipeClient
    {
        private readonly ILogger logger;
        private readonly SensitivePropertyRedactor redactor;

        internal ScriptGroupFileReader(ILogger logger)
            : this(logger, new SensitivePropertyRedactor(SensitivePropertyRedactionOptions.Default()))
        {
        }

        internal ScriptGroupFileReader(ILogger logger, SensitivePropertyRedactor redactor)
        {
            this.logger = logger;
            this.redactor = redactor;
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
                ScriptGroup scriptGroup;
                using (FileStream readStream = File.OpenRead(filename))
                {
                    scriptGroup = JsonSerializer.Deserialize<ScriptGroup>(readStream, options);
                }

                var guid = scriptGroup.ID.ToString();
                var list = scriptGroup.ScriptProperties.ToList();
                var env = scriptGroup.CommonProperties["EnvironmentName"];

                logger.LogInformation($"Received from file: {guid}");
                foreach (var scriptGroupScriptProperty in list)
                {
                    var props = redactor.RedactJson(JsonSerializer.Serialize(scriptGroupScriptProperty.Properties));

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
