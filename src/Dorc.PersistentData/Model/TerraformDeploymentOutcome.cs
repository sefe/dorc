using Dorc.ApiModel;

namespace Dorc.PersistentData.Model;

public class TerraformDeploymentOutcome
{
    public int DeploymentResultId { get; set; }
    public long Revision { get; set; }
    public string ApplyOutcome { get; set; } = "RunningOrUnknown";
    public string InventoryOutcome { get; set; } = "NotAttempted";
    public string ArchiveOutcome { get; set; } = "NotAttempted";
    public int RegisteredCount { get; set; }
    public int FailedCount { get; set; }
    public DateTimeOffset? AppliedAt { get; set; }
    public string? InventoryArtifactSha256 { get; set; }
    public string InventoryWarningsJson { get; set; } = "[]";

    public TerraformDeploymentOutcomeApiModel ToApiModel() => new()
    {
        DeploymentResultId = DeploymentResultId, Revision = Revision,
        ApplyOutcome = ApplyOutcome, InventoryOutcome = InventoryOutcome, ArchiveOutcome = ArchiveOutcome,
        RegisteredCount = RegisteredCount, FailedCount = FailedCount, AppliedAt = AppliedAt,
        InventoryRecoveryAvailable = InventoryArtifactSha256 != null,
        InventoryWarnings = System.Text.Json.JsonSerializer.Deserialize<List<string>>(InventoryWarningsJson) ?? new()
    };
}

public class TerraformInventoryProvenance
{
    public int ComponentId { get; set; }
    public int EnvironmentId { get; set; }
    public int CloudResourceId { get; set; }
    public int? DatabaseId { get; set; }
    public int FirstSeenResultId { get; set; }
    public int LastSeenResultId { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public string ResourceIdentifier { get; set; } = string.Empty;
    public string ReconciliationState { get; set; } = "Observed";
    public long Revision { get; set; }
}
