namespace Dorc.ApiModel
{
    public class AnalyticsDurationApiModel
    {
        public double AverageDurationMinutes { get; set; }
        public double MaxDurationMinutes { get; set; }
        public double MinDurationMinutes { get; set; }
        public double? P50DurationMinutes { get; set; }
        public double? P90DurationMinutes { get; set; }
        public double? P95DurationMinutes { get; set; }
    }
}
