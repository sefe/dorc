using System.ComponentModel.DataAnnotations.Schema;

namespace Dorc.PersistentData.Model
{
    public class TerraformPlanApproval
    {
        public const string EphemeralAutoApproveActor = "system:ephemeral-auto-approve";

        public int Id { get; set; }
        public int DeploymentResultId { get; set; }
        public int DeploymentRequestId { get; set; }
        public string ApprovedBy { get; set; } = null!;

        [NotMapped]
        public string ConfirmedBy
        {
            get => ApprovedBy;
            set => ApprovedBy = value;
        }

        public DateTimeOffset ApprovedAt { get; set; }
        public string PlanSha256 { get; set; } = null!;
        public string SourceSha256 { get; set; } = null!;
        public string PlanContentSha256 { get; set; } = null!;
    }
}
