using Dorc.PersistData.Dapper;
using Serilog;

namespace Dorc.Runner.Logger
{
    public class RunnerLogger : IRunnerLogger
    {
        public ILogger Logger { get; set; }
        public IDapperContext DapperContext { get; set; }

        public RunnerLogger(ILogger logger, IDapperContext dapperContext)
        {
            Logger = logger;
            DapperContext = dapperContext;
        }

        public void AddLogFilePath(int deploymentRequestId, string logFilePath)
        {
            this.DapperContext.AddLogFilePath(this.Logger, deploymentRequestId, logFilePath);
        }

        public void UpdateLog(int deploymentResultId, string log)
        {
            this.DapperContext.UpdateLog(this.Logger, deploymentResultId, log);
        }

        public void Information(string message)
        {
            this.Logger.Information(message);
        }
        public void Information(string message, params object?[]? values)
        {
            this.Logger.Information(message, values);
        }

        public void Verbose(string message)
        {
            this.Logger.Verbose(message);
        }

        public void Warning(string message)
        {
            this.Logger.Warning(message);
        }

        public void Error(string message)
        {
            this.Logger.Error(message);
        }
        public void Error(string message, Exception exception)
        {
            this.Logger.Error(message, exception);
        }
        public void Error(Exception exception, string message, params object?[]? values)
        {
            this.Logger.Error(exception, message, values);
        }

        public void Debug(string message)
        {
            this.Logger.Debug(message);
        }
    }
}
