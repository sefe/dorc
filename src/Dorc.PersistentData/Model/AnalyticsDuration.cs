namespace Dorc.PersistentData.Model
{
    public class AnalyticsDuration
    {
        public int Id { get; set; }
        public decimal AverageDurationMinutes { get; set; }
        public decimal LongestDurationMinutes { get; set; }
        public decimal ShortestDurationMinutes { get; set; }
    }
}
