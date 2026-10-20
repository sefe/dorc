using System;

namespace Dorc.ApiModel
{
    public class TerraformPlanApiModel
    {
        public int DeploymentResultId { get; set; }
        public string PlanContent { get; set; }
        public DateTime? CreatedAt { get; set; }
        public string Status { get; set; }
        public string PlanContentSha256 { get; set; } = string.Empty;
        public TerraformDeploymentReceiptApiModel Receipt { get; set; }
        public string ApprovedBy { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public bool IsHistorical { get; set; }
        public TerraformDeploymentOutcomeApiModel Outcome { get; set; }
    }
}