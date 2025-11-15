namespace Dorc.Core.Notifications
{
    /// <summary>
    /// Data model for job completion notifications
    /// </summary>
    public class JobCompletionNotification
    {
        public string UserIdentifier { get; set; } = string.Empty;
        public int RequestId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? Environment { get; set; }
        public string? Project { get; set; }
        public string? BuildNumber { get; set; }
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    }
}
