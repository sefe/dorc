using Newtonsoft.Json;
using OpenSearch.Client;

namespace Dorc.OpenSearchData.Model
{
    internal class DeployElasticLog
    {
        [JsonProperty("request_id")]
        public int request_id { get; set; }
        [JsonProperty("deployment_result_id")]
        public int deployment_result_id { get; set; }
        [JsonProperty("message")]
        public string message { get; set; }
        [JsonProperty("exception")]
        public Exception exception { get; set; }
        [JsonProperty("level")]
        public LogLevel level { get; set; }
        [JsonProperty("@timestamp")]
        public DateTime @timestamp { get; set; }

        public DeployElasticLog(int requestId, int deploymentResultId, string message, LogLevel level = LogLevel.Info, Exception exception = null)
        {
            request_id = requestId;
            deployment_result_id = deploymentResultId;
            this.message = message;
            this.exception = exception;
            this.level = level;
            this.@timestamp = DateTime.UtcNow;
        }
    }
}
