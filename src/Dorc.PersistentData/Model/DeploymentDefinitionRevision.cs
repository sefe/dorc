namespace Dorc.PersistentData.Model;

public class DeploymentDefinitionRevision
{
    public Guid Id { get; set; }
    public Guid? ChangeId { get; set; }
    public int ProjectId { get; set; }
    public int ComponentId { get; set; }
    public string ComponentName { get; set; } = "";
    public string DefinitionJson { get; set; } = "";
    public string Revision { get; set; } = "";
    public DateTimeOffset ObservedAt { get; set; }
    public bool IsBaseline { get; set; }
}
