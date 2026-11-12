namespace Dorc.PersistentData.Model
{
    // No foreign keys to mutable result rows: restart archives and deletes them.
    public class TerraformDeploymentReceipt
    {
        public int DeploymentResultId { get; set; }
        public int DeploymentRequestId { get; set; }
        public int? ProjectId { get; set; }
        public string? DefinitionRevision { get; set; }
        public string? SourceType { get; set; }
        public string? RequestedSource { get; set; }
        public string? RequestedRef { get; set; }
        public string? SourceSubPath { get; set; }
        public string? BuildIdentity { get; set; }
        public int ComponentId { get; set; }
        public int? EnvironmentId { get; set; }
        public string? WorkloadTarget { get; set; }
        public string ProjectName { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = string.Empty;
        public string ComponentName { get; set; } = string.Empty;
        public DateTimeOffset PlannedAt { get; set; }
        public string? TemplateName { get; set; }
        public string? TemplateVersion { get; set; }
        public string? GitCommitSha { get; set; }
        public string? StateStorageAccount { get; set; }
        public string? StateContainerName { get; set; }
        public string? StateKey { get; set; }
        public string PlanContentSha256 { get; set; } = string.Empty;
        public string SourceSha256 { get; set; } = string.Empty;
        public string PlanSha256 { get; set; } = string.Empty;
    }
}
