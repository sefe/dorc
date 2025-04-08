using System;

namespace Dorc.Runner.Logger.Model
{
    internal class DeployElasticLog
    {
        public int RequestId {  get; set; }
        public int DeploymentResultId { get; set; }
        public string Message {  get; set; }
        public Exception Exception { get; set; }
        public LogType Type { get; set; }
        public DateTime TimeStamp { get; set; }

        public DeployElasticLog(int requestId, int deploymentResultId, string message, LogType type = LogType.Info, Exception exception = null)
        {
            RequestId = requestId;
            DeploymentResultId = deploymentResultId;
            Message = message;
            Exception = exception;
            Type = type;
            TimeStamp = DateTime.UtcNow;
        }
    }
}
