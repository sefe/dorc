namespace Dorc.PersistentData.Model
{
    public class TerraformPlanApproval
    {
        public int Id { get; set; }
        public int DeploymentResultId { get; set; }
        public int DeploymentRequestId { get; set; }
        public string ApprovedBy { get; set; } = null!;
        public DateTimeOffset ApprovedAt { get; set; }
        public string PlanSha256 { get; set; } = null!;
        public string SourceSha256 { get; set; } = null!;
        public string PlanContentSha256 { get; set; } = null!;
    }
}
