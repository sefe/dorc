using System.Collections.Generic;

namespace Dorc.ApiModel
{
    public class AnalyticsDeploymentSummaryApiModel
    {
        public int TotalDeployments { get; set; }
        public int TotalDeploymentsThisYear { get; set; }
        public int TotalFailedDeploymentsThisYear { get; set; }
        public int PercentFailedThisYear { get; set; }
        public int AverageDeploymentsPerDay { get; set; }
        public int BusiestDeploymentCount { get; set; }
        public int PercentTop3Projects { get; set; }
        public List<AnalyticsProjectDeploymentApiModel> TopProjectsThisYear { get; set; }
            = new List<AnalyticsProjectDeploymentApiModel>();
    }
}
