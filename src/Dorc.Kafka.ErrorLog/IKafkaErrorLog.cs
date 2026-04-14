using Dorc.PersistentData.Model;

namespace Dorc.Kafka.ErrorLog;

public interface IKafkaErrorLog
{
    /// <summary>
    /// Persists a poison-message failure. Applies the configured payload-size
    /// truncation, server-populates <see cref="KafkaErrorLogEntry.LoggedAt"/>,
    /// <see cref="KafkaErrorLogEntry.Id"/>, and <see cref="KafkaErrorLogEntry.PayloadTruncated"/>.
    /// Honours <paramref name="cancellationToken"/>; bounding the wait under
    /// SQL-slow-but-not-down conditions is the caller's responsibility.
    /// Does not enlist in any ambient transaction.
    /// </summary>
    Task InsertAsync(KafkaErrorLogEntry entry, CancellationToken cancellationToken);

    /// <summary>
    /// Returns up to <paramref name="maxRows"/> rows ordered by OccurredAt DESC,
    /// Id DESC. Filter parameters left null mean "no filter on that dimension".
    /// </summary>
    Task<IReadOnlyList<KafkaErrorLogEntry>> QueryAsync(
        string? topic,
        string? consumerGroup,
        DateTimeOffset? sinceUtc,
        int maxRows,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deletes rows with <c>OccurredAt &lt; UtcNow - RetentionDays</c> in batches of
    /// <c>PurgeBatchSize</c>. Returns the total deleted across all batches.
    /// Idempotent; a re-run on the same state deletes 0.
    /// </summary>
    Task<int> PurgeAsync(CancellationToken cancellationToken);
}
