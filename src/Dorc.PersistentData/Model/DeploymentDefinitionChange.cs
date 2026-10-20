namespace Dorc.PersistentData.Model;

public class DeploymentDefinitionChange
{
    public Guid Id { get; set; }
    public int ProjectId { get; set; }
    public string ProjectName { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; }
    public string Actor { get; set; } = "";
    public string Operation { get; set; } = "";
    public string BeforeJson { get; set; } = "";
    public string AfterJson { get; set; } = "";
    public string Revision { get; set; } = "";
    public string? SourceRecordId { get; set; }
    public string? AffectedComponentsJson { get; set; }
}
