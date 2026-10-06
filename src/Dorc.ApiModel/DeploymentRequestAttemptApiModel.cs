using System;
using System.Collections.Generic;

namespace Dorc.ApiModel
{
    public class DeploymentRequestAttemptApiModel
    {
        public int Id { get; set; }
        public int DeploymentRequestId { get; set; }
        public int AttemptNumber { get; set; }
        public DateTimeOffset? StartedTime { get; set; }
        public DateTimeOffset? CompletedTime { get; set; }
        public string Status { get; set; }
        public string Log { get; set; }
        public string UserName { get; set; }
        public List<DeploymentResultAttemptApiModel> ComponentResults { get; set; } = new List<DeploymentResultAttemptApiModel>();
    }
}