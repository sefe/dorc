namespace Dorc.PersistentData.Model
{
    public class EnvironmentComponentStatus
    {
        public int Id { get; set; }
        public Environment Environment { get; set; } = null!;
        public Component Component { get; set; } = null!;
        public string? Status { get; set; }
        public DateTimeOffset UpdateDate { get; set; }
        public DeploymentRequest DeploymentRequest { get; set; } = null!;
    }
}