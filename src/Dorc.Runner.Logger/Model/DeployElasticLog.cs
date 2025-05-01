using OpenSearch.Client;
using System;
using System.Text.Json.Serialization;

namespace Dorc.Runner.Logger.Model
{
    internal class DeployElasticLog
    {
        [JsonPropertyName("request_id")]
        public int RequestId {  get; set; }
        [JsonPropertyName("deployment_result_id")]
        public int DeploymentResultId { get; set; }
        [JsonPropertyName("message")]
        public string Message {  get; set; }
        [JsonPropertyName("exception")]
        public Exception Exception { get; set; }
        [JsonPropertyName("level")]
        public LogLevel Level { get; set; }
        [JsonPropertyName("@timestamp")]
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
