﻿using Dorc.ApiModel;
using Dorc.PowerShell;
using Dorc.Runner.Pipes;
using Serilog;

namespace Dorc.Runner
{
    internal class ScriptGroupProcessor : IScriptGroupProcessor
    {
        private readonly ILogger logger;
        private readonly IScriptGroupPipeClient scriptGroupPipeClient;

        internal ScriptGroupProcessor(
        ILogger logger,
        IScriptGroupPipeClient scriptGroupPipeClient)
        {
            this.logger = logger;
            this.scriptGroupPipeClient = scriptGroupPipeClient;
        }

        public void Process(string pipeName)
        {
            ScriptGroup scriptGroupProperties = this.scriptGroupPipeClient.GetScriptGroupProperties(pipeName); 
            var deploymentResultId = scriptGroupProperties.DeployResultId;

            if (scriptGroupProperties == null
                || scriptGroupProperties.CommonProperties == null
                || !scriptGroupProperties.CommonProperties.Any()
                || string.IsNullOrEmpty(scriptGroupProperties.ScriptsLocation))
            {
                throw new Exception("ScriptGroup is not initialized.");
            }

            try
            {

                this.logger.Information("ScriptGroup is received.");

                var scriptRunner = new PowerShellScriptRunner(this.logger, deploymentResultId);

                scriptRunner.Run(
                    scriptGroupProperties.ScriptsLocation,
                    scriptGroupProperties.ScriptProperties.Select(p => (p.ScriptPath, p.Properties)),
                    scriptGroupProperties.CommonProperties);

            }
            catch (Exception e)
            {
                logger.Error("An Exception has Occured: {0}", e.Message);
                throw;
            }
        }
    }
}
