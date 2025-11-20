using System;

namespace Dorc.ApiModel
{
    public class TerraformPlanApiModel
    {
        public int DeploymentResultId { get; set; }
        public string PlanContent { get; set; }
        public string BlobUrl { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Status { get; set; }
    }
}