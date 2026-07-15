namespace Dorc.ApiModel
{
    public class AnalyticsProjectDurationApiModel
    {
        public string ProjectName { get; set; }
        public double MedianDurationMinutes { get; set; }
        public double P90DurationMinutes { get; set; }
        public int SampleCount { get; set; }
    }
}
