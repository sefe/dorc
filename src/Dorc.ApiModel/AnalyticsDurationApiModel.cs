namespace Dorc.ApiModel
{
    public class AnalyticsDurationApiModel
    {
        public double AverageDurationMinutes { get; set; }
        public double MaxDurationMinutes { get; set; }
        public double MinDurationMinutes { get; set; }
        public int TotalDeployments { get; set; }
    }
}
