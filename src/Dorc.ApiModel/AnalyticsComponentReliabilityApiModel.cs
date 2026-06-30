namespace Dorc.ApiModel
{
    public class AnalyticsComponentReliabilityApiModel
    {
        public string ComponentName { get; set; }
        public int AttemptCount { get; set; }
        public int FailedCount { get; set; }
        public int RetryAttemptCount { get; set; }
    }
}
