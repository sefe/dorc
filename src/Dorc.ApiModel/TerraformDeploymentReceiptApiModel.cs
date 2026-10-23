using System;

namespace Dorc.ApiModel
{
    public class TerraformDeploymentReceiptApiModel
    {
        public int DeploymentRequestId { get; set; }
        public int? ProjectId { get; set; }
        public string DefinitionRevision { get; set; }
        public string SourceType { get; set; }
        public string RequestedSource { get; set; }
        public string RequestedRef { get; set; }
        public string SourceSubPath { get; set; }
        public string BuildIdentity { get; set; }
        public int ComponentId { get; set; }
        public int? EnvironmentId { get; set; }
        public string WorkloadTarget { get; set; }
        public string ProjectName { get; set; }
        public string EnvironmentName { get; set; }
        public string ComponentName { get; set; }
        public string TemplateName { get; set; }
        public string TemplateVersion { get; set; }
        public string GitCommitSha { get; set; }
        public string StateStorageAccount { get; set; }
        public string StateContainerName { get; set; }
        public string StateKey { get; set; }
        public DateTimeOffset PlannedAt { get; set; }
    }
}
