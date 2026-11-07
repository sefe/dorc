namespace Dorc.PersistentData.Model;

public class DeploymentRequestDefinition
{
    public int DeploymentRequestId { get; set; }
    public int Ordinal { get; set; }
    public int ProjectId { get; set; }
    public int ComponentId { get; set; }
    public string ComponentName { get; set; } = "";
    public Guid DefinitionRevisionId { get; set; }
    public string DefinitionRevision { get; set; } = "";
    public string DefinitionJson { get; set; } = "";
    public string ProjectJson { get; set; } = "";
    public string? ScriptJson { get; set; }
    public bool IsSkipped { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
    public string SubmittedBy { get; set; } = "";
}
