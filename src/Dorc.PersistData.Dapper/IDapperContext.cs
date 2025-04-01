using Serilog;

namespace Dorc.PersistData.Dapper
{
    public interface IDapperContext
    {
        void UpdateLog(ILogger contextLogger,int deploymentResultId, string log);
        void AddLogFilePath(ILogger contextLogger,int deploymentRequestId, string logFilePath);
    }
}