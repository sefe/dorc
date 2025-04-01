using Dorc.ApiModel;
using Dorc.PersistData.Dapper;
using Dorc.PowerShell;
using Dorc.Runner.Pipes;
using Serilog;
using Serilog.Context;

namespace Dorc.Runner
{
    internal class ScriptGroupProcessor : IScriptGroupProcessor
    {
        private ILogger logger;
        private readonly IScriptGroupPipeClient scriptGroupPipeClient;
        private readonly IDapperContext dbContext;

        internal ScriptGroupProcessor(
        ILogger logger,
        IScriptGroupPipeClient scriptGroupPipeClient,
        IDapperContext dbContext)
        {
            this.dbContext = dbContext;
            this.logger = logger;
            this.scriptGroupPipeClient = scriptGroupPipeClient;
        }

        public int Process(string pipeName, int requestId)
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

            using (LogContext.PushProperty("RequestId", requestId))
            using (LogContext.PushProperty("DeploymentResultId", deploymentResultId))
            using (var outputProc = new OutputProcessor(this.logger, this.dbContext, deploymentResultId))
            {
                var scriptRunner = new PowerShellScriptRunner(this.logger, outputProc);
                int sumResult = 0;
                foreach (var scriptProps in scriptGroupProperties.ScriptProperties)
                {
                    sumResult += scriptRunner.Run(
                        scriptGroupProperties.ScriptsLocation,
                        scriptProps.ScriptPath,
                        scriptProps.Properties,
                        scriptGroupProperties.CommonProperties);
                }

                return sumResult;
            }            
        }
    }
}
