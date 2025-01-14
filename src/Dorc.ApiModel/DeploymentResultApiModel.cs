using System;

namespace Dorc.ApiModel
{
    public class DeploymentResultApiModel
    {
        public int Id { get; set; }
        public string ComponentName { get; set; }
        public string Status { get; set; }
        public string Log { get; set; }
        public int ComponentId { get; set; }
        public int RequestId { get; set; }
        public DateTimeOffset? StartedTime { get; set; }
        public DateTimeOffset? CompletedTime { get; set; }
    }
}