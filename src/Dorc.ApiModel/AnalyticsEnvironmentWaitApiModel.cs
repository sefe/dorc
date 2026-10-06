namespace Dorc.ApiModel
{
    public class AnalyticsEnvironmentWaitApiModel
    {
        public string EnvironmentName { get; set; }
        public double AvgWaitMinutes { get; set; }
        public double MedianWaitMinutes { get; set; }
        public double P90WaitMinutes { get; set; }
        public int SampleCount { get; set; }
    }
}
