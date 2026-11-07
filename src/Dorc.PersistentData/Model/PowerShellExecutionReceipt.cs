namespace Dorc.PersistentData.Model;

public class PowerShellExecutionReceipt
{
    public int DeploymentResultId { get; set; }
    public int ScriptOrdinal { get; set; }
    public int DeploymentRequestId { get; set; }
    public int ProjectId { get; set; }
    public int ComponentId { get; set; }
    public string DefinitionRevision { get; set; } = "";
    public string TextSha256 { get; set; } = "";
    public string RuntimeVersion { get; set; } = "";
    public string RequestedRuntime { get; set; } = "";
    public string? SourcePath { get; set; }
    public string? BuildIdentity { get; set; }
    public string Phase { get; set; } = "";
    public DateTimeOffset? PreparedAt { get; set; }
    public DateTimeOffset ObservedAt { get; set; }
    public DateTimeOffset? InvocationObservedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
