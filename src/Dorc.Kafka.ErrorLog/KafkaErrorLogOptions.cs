namespace Dorc.Kafka.ErrorLog;

/// <summary>
/// Producer-side configuration for the Kafka DLQ tier (post-K-2 substrate
/// change: error log moved from a SQL table to a Kafka topic per source-
/// topic). Topic naming lives on <see cref="Configuration.KafkaTopicsOptions"/>;
/// this options class only carries the producer-side knobs.
/// </summary>
public sealed class KafkaErrorLogOptions
{
    public const string SectionName = "Kafka:ErrorLog";

    /// <summary>
    /// Per-message payload truncation cap applied before producing. Sized
    /// well under the 1 MiB Kafka <c>message.max.bytes</c> default to leave
    /// headroom for the envelope and exception fields.
    /// </summary>
    public int MaxPayloadBytes { get; set; } = 900_000;

    /// <summary>
    /// Bounded wait for <c>ProduceAsync</c>. Caller cancellation also yields.
    /// </summary>
    public int ProduceTimeoutMs { get; set; } = 5_000;

    /// <summary>
    /// Topic-creation partition count. DLQ topics are low-volume; defaults
    /// to 3 (vs. 12 for the live request/results topics).
    /// </summary>
    public int PartitionCount { get; set; } = 3;

    /// <summary>
    /// Topic-creation replication factor. Override to 1 for single-broker dev.
    /// </summary>
    public short ReplicationFactor { get; set; } = 3;

    /// <summary>
    /// Kafka topic retention (ms). Default 30 days. Replaces the SQL
    /// <c>RetentionDays</c> + purge job from the pre-K-2 implementation.
    /// </summary>
    public long RetentionMs { get; set; } = 2_592_000_000L; // 30 days
}
