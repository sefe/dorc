namespace Dorc.PersistentData.Model
{
    /// <summary>
    /// Persistent record of a Kafka consumer poison-message failure.
    /// Per HLPS C-8: no DLQ; failures are written here and (on DB unavailability)
    /// fall back to a structured-log entry that carries the same fields.
    /// </summary>
    public class KafkaErrorLogEntry
    {
        public long Id { get; set; }

        public string Topic { get; set; } = string.Empty;

        public int Partition { get; set; }

        public long Offset { get; set; }

        public string ConsumerGroup { get; set; } = string.Empty;

        public string? MessageKey { get; set; }

        public byte[]? RawPayload { get; set; }

        public bool PayloadTruncated { get; set; }

        public string Error { get; set; } = string.Empty;

        public string? Stack { get; set; }

        public DateTimeOffset OccurredAt { get; set; }

        public DateTimeOffset LoggedAt { get; set; }
    }
}
