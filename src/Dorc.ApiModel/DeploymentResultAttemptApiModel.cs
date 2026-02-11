using System;

namespace Dorc.ApiModel
{
    public class DeploymentResultAttemptApiModel
    {
        public int Id { get; set; }
        public int DeploymentRequestAttemptId { get; set; }
        public int ComponentId { get; set; }
        public string ComponentName { get; set; }
        public DateTimeOffset? StartedTime { get; set; }
        public DateTimeOffset? CompletedTime { get; set; }
        public string Status { get; set; }
        public string Log { get; set; }
    }
}