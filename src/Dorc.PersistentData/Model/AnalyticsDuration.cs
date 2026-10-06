namespace Dorc.PersistentData.Model
{
    public class AnalyticsDuration
    {
        public int Id { get; set; }
        public decimal AverageDurationMinutes { get; set; }
        public decimal LongestDurationMinutes { get; set; }
        public decimal ShortestDurationMinutes { get; set; }
        public decimal? P50DurationMinutes { get; set; }
        public decimal? P90DurationMinutes { get; set; }
        public decimal? P95DurationMinutes { get; set; }
    }
}
