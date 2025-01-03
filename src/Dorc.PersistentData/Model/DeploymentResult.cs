namespace Dorc.PersistentData.Model
{
    public class DeploymentResult
    {
        public int Id { get; set; }

        public DeploymentRequest DeploymentRequest { get; set; } = null!;

        public Component Component { get; set; } = null!;

        public string? Log { get; set; }

        public string? Status { get; set; }

        public DateTimeOffset? StartedTime { get; set; }

        public DateTimeOffset? CompletedTime { get; set; }
    }
}