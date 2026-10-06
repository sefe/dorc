using System.ComponentModel.DataAnnotations;

namespace Dorc.PersistentData.Model
{
    public class DeploymentRequestAttempt
    {
        public int Id { get; set; }

        public DeploymentRequest DeploymentRequest { get; set; } = null!;

        public int AttemptNumber { get; set; }

        public DateTimeOffset? StartedTime { get; set; }

        public DateTimeOffset? CompletedTime { get; set; }

        [StringLength(32)]
        public string Status { get; set; } = null!;

        public string? Log { get; set; }

        [StringLength(128)]
        public string UserName { get; set; } = null!;

        public ICollection<DeploymentResultAttempt> DeploymentResultAttempts { get; set; } = new List<DeploymentResultAttempt>();
    }
}
