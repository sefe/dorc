using Dorc.ApiModel;

namespace Dorc.PersistentData.Model;

public class TerraformDeploymentOutcome
{
    public const string ApplyNotStarted = "NotStarted";
    public const string ApplyRunningOrUnknown = "RunningOrUnknown";
    public const string ApplySucceeded = "Succeeded";
    public const string ApplyFailedOrUnknown = "FailedOrUnknown";

    public int DeploymentResultId { get; set; }
    public long Revision { get; set; }
    public string ApplyOutcome { get; set; } = ApplyRunningOrUnknown;
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

public enum TerraformApplyExecutionState
{
    NeverRan = 0,
    Confirmed = 1,
    UnconfirmedPartial = 2
}

public static class TerraformApplyReceiptSelection
{
    public static TerraformApplyExecutionState Classify(string? receiptOperation, string? applyOutcome)
    {
        if (!string.IsNullOrWhiteSpace(applyOutcome))
        {
            if (string.Equals(applyOutcome, TerraformDeploymentOutcome.ApplySucceeded, StringComparison.OrdinalIgnoreCase))
                return TerraformApplyExecutionState.Confirmed;
            if (string.Equals(applyOutcome, TerraformDeploymentOutcome.ApplyNotStarted, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(applyOutcome, "NotAttempted", StringComparison.OrdinalIgnoreCase))
                return TerraformApplyExecutionState.NeverRan;
            return TerraformApplyExecutionState.UnconfirmedPartial;
        }

        return string.Equals(receiptOperation, "Apply", StringComparison.OrdinalIgnoreCase)
            ? TerraformApplyExecutionState.Confirmed
            : TerraformApplyExecutionState.NeverRan;
    }

    public static TerraformDeploymentReceipt? SelectStateMutatingReceipt(
        IEnumerable<(TerraformDeploymentReceipt Receipt, string? ApplyOutcome)> candidates,
        bool allowNeverRanFallback = false)
    {
        var ordered = candidates
            .Select(c => (c.Receipt, c.ApplyOutcome, State: Classify(c.Receipt.Operation, c.ApplyOutcome)))
            .OrderByDescending(c => c.Receipt.DeploymentResultId)
            .ToList();

        var stateMutating = ordered
            .Where(c => c.State != TerraformApplyExecutionState.NeverRan)
            .ToList();

        if (stateMutating.Count == 0)
        {
            return allowNeverRanFallback ? ordered.FirstOrDefault().Receipt : null;
        }

        var latest = stateMutating[0];
        if (latest.State == TerraformApplyExecutionState.Confirmed)
        {
            return latest.Receipt;
        }

        if (string.Equals(latest.ApplyOutcome, TerraformDeploymentOutcome.ApplyRunningOrUnknown, StringComparison.OrdinalIgnoreCase) ||
            stateMutating.Count > 1)
        {
            throw new InvalidOperationException(
                $"Terraform component {latest.Receipt.ComponentId} in environment '{latest.Receipt.EnvironmentName}' has an ambiguous partial or interrupted apply (result {latest.Receipt.DeploymentResultId}, outcome '{latest.ApplyOutcome}'). Reconcile Terraform state with a successful apply before running destroy.");
        }

        return latest.Receipt;
    }
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
