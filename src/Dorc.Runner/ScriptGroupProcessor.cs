using Dorc.ApiModel;
using Dorc.PowerShell;
using Dorc.Runner.Logger;
using Dorc.Runner.Pipes;

namespace Dorc.Runner
{
    public class ScriptGroupProcessor : IScriptGroupProcessor
    {
        private IRunnerLogger logger;
        private readonly IScriptGroupPipeClient scriptGroupPipeClient;

        public ScriptGroupProcessor(
        IRunnerLogger logger,
        IScriptGroupPipeClient scriptGroupPipeClient)
        {
            this.logger = logger;
            this.scriptGroupPipeClient = scriptGroupPipeClient;
        }

        public int Process(string pipeName, int requestId)
        {
            this.logger.SetRequestId(requestId);
            ScriptGroup scriptGroupProperties = this.scriptGroupPipeClient.GetScriptGroupProperties(pipeName);

            if (scriptGroupProperties == null
                || scriptGroupProperties.CommonProperties == null
                || !scriptGroupProperties.CommonProperties.Any()
                || string.IsNullOrEmpty(scriptGroupProperties.ScriptsLocation))
            {
                throw new Exception("ScriptGroup is not initialized.");
            }

            this.logger.SetDeploymentResultId(scriptGroupProperties.DeployResultId);

            var scriptRunner = new PowerShellScriptRunner(this.logger, scriptGroupProperties);
            foreach (var scriptProps in scriptGroupProperties.ScriptProperties)
            {
                int result = scriptRunner.Run(
                    scriptGroupProperties.ScriptsLocation,
                    scriptProps.ScriptPath,
                    scriptProps.Properties,
                    scriptGroupProperties.CommonProperties);
                if (result != 0)
                {
                    return result;
                }
            }

            return 0;
        }
    }
}
