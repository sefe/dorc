namespace Dorc.PersistentData.Model
{
    public class AnalyticsComponentReliability
    {
        public int Id { get; set; }
        public string ComponentName { get; set; } = null!;
        public int AttemptCount { get; set; }
        public int FailedCount { get; set; }
        public int RetryAttemptCount { get; set; }
    }
}
