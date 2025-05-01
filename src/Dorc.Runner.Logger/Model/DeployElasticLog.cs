using OpenSearch.Client;
using System;
using Newtonsoft.Json;

namespace Dorc.Runner.Logger.Model
{
    internal class DeployElasticLog
    {
        [JsonProperty("request_id")]
        public int RequestId {  get; set; }
        [JsonProperty("deployment_result_id")]
        public int DeploymentResultId { get; set; }
        [JsonProperty("message")]
        public string Message {  get; set; }
        [JsonProperty("exception")]
        public Exception Exception { get; set; }
        [JsonProperty("level")]
        public LogLevel Level { get; set; }
        [JsonProperty("@timestamp")]
        public DateTime TimeStamp { get; set; }

        public DeployElasticLog(int requestId, int deploymentResultId, string message, LogLevel level = LogLevel.Info, Exception exception = null)
        {
            RequestId = requestId;
            DeploymentResultId = deploymentResultId;
            Message = message;
            Exception = exception;
            Level = level;
            TimeStamp = DateTime.UtcNow;
        }
    }
}
