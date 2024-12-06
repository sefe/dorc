namespace Dorc.PersistentData.Model
{
    public class DeploymentRequestProcess
    {
        public int ProcessId { get; set; }

        public DeploymentRequest DeploymentRequest { get; set; } = default!;
    }
}
