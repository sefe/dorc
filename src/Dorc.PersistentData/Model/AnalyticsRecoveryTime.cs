namespace Dorc.PersistentData.Model
{
    public class AnalyticsRecoveryTime
    {
        public int Id { get; set; }
        public string ProjectName { get; set; } = null!;
        public decimal MedianRecoveryHours { get; set; }
        public decimal AvgRecoveryHours { get; set; }
        public int SampleCount { get; set; }
    }
}
