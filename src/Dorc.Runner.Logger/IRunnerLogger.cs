using Dorc.PersistData.Dapper;
using Serilog;

namespace Dorc.Runner.Logger
{
    public interface IRunnerLogger
    {
        ILogger Logger { get; set; }
        IDapperContext DapperContext { get; set; }

        void UpdateLog(int deploymentResultId, string log);

        void AddLogFilePath(int deploymentRequestId, string logFilePath);

        void Information(string message);
        void Information(string message, params object?[]? values);
        void Verbose(string message);
        void Warning(string message);
        void Error(string message);
        void Error(string message, Exception exception);
        void Error(Exception exception, string message, params object?[]? values);
        void Debug(string message);
    }
}
