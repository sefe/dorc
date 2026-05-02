namespace Dorc.ApiModel
{
    public class AnalyticsEnvironmentUsageApiModel
    {
        public string EnvironmentName { get; set; }
        public int CountOfDeployments { get; set; }
        public int Failed { get; set; }
    }
}
