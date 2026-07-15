namespace Dorc.PersistentData.Model
{
    public class AnalyticsProjectDuration
    {
        public int Id { get; set; }
        public string ProjectName { get; set; } = null!;
        public decimal MedianDurationMinutes { get; set; }
        public decimal P90DurationMinutes { get; set; }
        public int SampleCount { get; set; }
    }
}
