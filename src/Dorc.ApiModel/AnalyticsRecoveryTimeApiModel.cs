namespace Dorc.ApiModel
{
    public class AnalyticsRecoveryTimeApiModel
    {
        public string ProjectName { get; set; }
        public double MedianRecoveryHours { get; set; }
        public double AvgRecoveryHours { get; set; }
        public int SampleCount { get; set; }
    }
}
