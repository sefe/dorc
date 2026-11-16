using System;

namespace Dorc.ApiModel
{
    public class DeploymentResultAttemptApiModel
    {
        public int? ProjectId { get; set; }
        public bool? IsTerraform { get; set; }
        public string DefinitionRevision { get; set; }
        public string SourceRevisionLabel { get; set; }
        public int Id { get; set; }
        public int DeploymentRequestAttemptId { get; set; }
        public int ComponentId { get; set; }
        public string ComponentName { get; set; }
        public DateTimeOffset? StartedTime { get; set; }
        public DateTimeOffset? CompletedTime { get; set; }
        public string Status { get; set; }
        public int? DeploymentResultId { get; set; }
        public string Log { get; set; }
        public bool HasTerraformPlan { get; set; }
    }
}