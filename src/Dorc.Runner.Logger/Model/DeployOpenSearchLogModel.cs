using OpenSearch.Client;
using System;
using Newtonsoft.Json;
using System.Globalization;
using System.Reflection;

namespace Dorc.Runner.Logger.Model
{
    internal class DeployOpenSearchLogModel
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
        public OpenSearch.Client.LogLevel level { get; set; }
        [JsonProperty("@timestamp")]
        public DateTime @timestamp { get; set; }
        [JsonProperty("name")]
        public string name { get; set; }
        [JsonProperty("version")]
        public string version { get; set; }
        [JsonProperty("core_version")]
        public string core_version { get; set; }
        [JsonProperty("environment")]
        public string environment { get; set; }
        [JsonProperty("environment_tier")]
        public string environment_tier { get; set; }
        [JsonProperty("component_name")]
        public string component_name { get; set; }
        [JsonProperty("machine_name")]
        public string machine_name { get; set; }
        [JsonProperty("process_user_identity")]
        public string process_user_identity { get; set; }

        public DeployOpenSearchLogModel(int requestId, int deploymentResultId, string message, OpenSearch.Client.LogLevel level = OpenSearch.Client.LogLevel.Info, Exception exception = null, string environment = "", string environmentTier = "")
        {
            request_id = requestId;
            deployment_result_id = deploymentResultId;
            this.message = message;
            this.exception = exception;
            this.level = level;
            this.@timestamp = DateTime.Now;
            var appAssembly = Assembly.GetEntryAssembly() ?? Assembly.GetCallingAssembly();
            this.version = appAssembly.GetName().Version.ToString();
            this.core_version = Assembly.GetExecutingAssembly().GetName().Version.ToString();
            this.environment = environment;
            this.environment_tier = environmentTier;
            this.component_name = "Runner";
            this.machine_name = Environment.MachineName;
            this.process_user_identity = Environment.UserName;
            
        }
    }
}
