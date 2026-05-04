namespace Dorc.Kafka.ErrorLog;

/// <summary>
/// In-memory shape callers (consumers) hand to <see cref="IKafkaErrorLog.InsertAsync"/>.
/// The producer impl converts this into a <see cref="KafkaErrorEnvelope"/> for
/// over-the-wire Avro emission to the configured per-source DLQ topic.
///
/// Post-K-2 the persistence has moved from a SQL table to a Kafka topic, so
/// EF-specific fields (Id, LoggedAt, server-populated PayloadTruncated) are
/// gone — truncation is computed by the producer at emit time and stamped on
/// the envelope directly.
/// </summary>
public class KafkaErrorLogEntry
{
    public string Topic { get; set; } = string.Empty;

    public int Partition { get; set; }

    public long Offset { get; set; }

    public string ConsumerGroup { get; set; } = string.Empty;

    public string? MessageKey { get; set; }

    public byte[]? RawPayload { get; set; }

    public string Error { get; set; } = string.Empty;

    public string? Stack { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}
