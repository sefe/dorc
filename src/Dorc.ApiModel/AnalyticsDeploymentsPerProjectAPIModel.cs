namespace Dorc.ApiModel
{
    public class AnalyticsDeploymentsPerProjectApiModel
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public int Day { get; set; }
        public string ProjectName { get; set; }
        public int CountOfDeployments { get; set; }
        public int Failed { get; set; }
    }
}