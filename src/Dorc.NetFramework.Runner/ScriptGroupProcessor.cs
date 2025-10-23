﻿using System;
using System.Linq;
using Dorc.ApiModel;
using Dorc.NetFramework.PowerShell;
using Dorc.NetFramework.Runner.Pipes;
using Dorc.Runner.Logger;
using Microsoft.Extensions.Logging.Context;

namespace Dorc.NetFramework.Runner
{
    internal class ScriptGroupProcessor : IScriptGroupProcessor
    {
        private readonly IRunnerLogger logger;
        private readonly IScriptGroupPipeClient scriptGroupPipeClient;

        internal ScriptGroupProcessor(
            IRunnerLogger logger,
            IScriptGroupPipeClient scriptGroupPipeClient)
        {
            this.logger = logger;
            this.scriptGroupPipeClient = scriptGroupPipeClient;
        }

        public void Process(string pipeName,int requestId)
        {
            ScriptGroup scriptGroupProperties = this.scriptGroupPipeClient.GetScriptGroupProperties(pipeName);
            var deploymentResultId = scriptGroupProperties.DeployResultId; 
            this.logger.SetRequestId(requestId);
            this.logger.SetDeploymentResultId(deploymentResultId);

            using (LogContext.PushProperty("RequestId", requestId)) 
            using (LogContext.PushProperty("DeploymentResultId", deploymentResultId))
            {
                logger.FileLogger.Information($"Request Id :{requestId}");
                logger.FileLogger.Information($"Deployment Result Id :{deploymentResultId}");
                try
                {
                    if (scriptGroupProperties == null
                        || scriptGroupProperties.CommonProperties == null
                        || !scriptGroupProperties.CommonProperties.Any()
                        || string.IsNullOrEmpty(scriptGroupProperties.ScriptsLocation))
                    {
                        throw new Exception("ScriptGroup is not initialized.");
                    }

                    logger.FileLogger.Information("ScriptGroup is received.");

                    var scriptRunner = new PowerShellScriptRunner(logger, deploymentResultId);

                    scriptRunner.Run(
                        scriptGroupProperties.ScriptsLocation,
                        scriptGroupProperties.ScriptProperties.Select(p => (p.ScriptPath, p.Properties)),
                       scriptGroupProperties.CommonProperties);

                }
                catch (Exception e)
                {
                    logger.Error($"An Exception Occured running the deployment: {e.Message}");
                    throw;
                }
            }

        }
    }
}
