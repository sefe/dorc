namespace Dorc.PersistentData.Model
{
    public class AnalyticsEnvironmentWait
    {
        public int Id { get; set; }
        public string EnvironmentName { get; set; } = null!;
        public decimal AvgWaitMinutes { get; set; }
        public decimal MedianWaitMinutes { get; set; }
        public decimal P90WaitMinutes { get; set; }
        public int SampleCount { get; set; }
    }
}
