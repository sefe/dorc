namespace Dorc.Kafka.ErrorLog;

public interface IKafkaErrorLog
{
    /// <summary>
    /// Produces a poison-message <see cref="KafkaErrorEnvelope"/> to the DLQ
    /// topic configured for <paramref name="entry"/><c>.Topic</c>. Applies the
    /// configured payload-size truncation; honours <paramref name="cancellationToken"/>.
    ///
    /// Throws if the source topic has no DLQ configured (the consumer's
    /// existing catch will then fall through to the structured-log tier per
    /// SPEC-S-006 R-8 / SPEC-S-007 R-3 #4 three-tier model). Throws if the
    /// produce itself fails (broker down, oversized payload, etc.) — same
    /// fallback contract.
    /// </summary>
    Task InsertAsync(KafkaErrorLogEntry entry, CancellationToken cancellationToken);
}
