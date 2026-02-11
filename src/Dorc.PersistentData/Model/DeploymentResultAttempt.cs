using System.ComponentModel.DataAnnotations;

namespace Dorc.PersistentData.Model
{
    public class DeploymentResultAttempt
    {
        public int Id { get; set; }

        public DeploymentRequestAttempt DeploymentRequestAttempt { get; set; } = null!;

        public int ComponentId { get; set; }

        [StringLength(256)]
        public string ComponentName { get; set; } = null!;

        public DateTimeOffset? StartedTime { get; set; }

        public DateTimeOffset? CompletedTime { get; set; }

        [StringLength(32)]
        public string Status { get; set; } = null!;

        public string? Log { get; set; }
    }
}