using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dorc.Kafka.ErrorLog;

/// <summary>
/// DLQ-tier producer per K-2 resolution. Routes <see cref="KafkaErrorLogEntry"/>
/// to the DLQ topic configured for the source topic; throws
/// <see cref="DlqNotConfiguredException"/> when the source topic has no DLQ
/// (the consumer's catch then falls through to the structured-log tier per
/// the SPEC-S-006 R-8 / SPEC-S-007 R-3 #4 three-tier model). Throws on produce
/// failure (broker down, oversized payload, etc.) — same fallback contract.
/// </summary>
public sealed class KafkaErrorLog : IKafkaErrorLog
{
    private readonly IProducer<string, KafkaErrorEnvelope> _producer;
    private readonly KafkaErrorLogOptions _options;
    private readonly IReadOnlyDictionary<string, string> _routes;
    private readonly ILogger<KafkaErrorLog> _logger;

    /// <param name="routes">
    /// Source-topic → DLQ-topic map. Missing key means "no DLQ for this source"
    /// — <see cref="InsertAsync"/> throws <see cref="DlqNotConfiguredException"/>
    /// for unmapped source topics. Wired by the DI extension from
    /// <c>KafkaTopicsOptions</c>.
    /// </param>
    public KafkaErrorLog(
        IProducer<string, KafkaErrorEnvelope> producer,
        IOptions<KafkaErrorLogOptions> options,
        IReadOnlyDictionary<string, string> routes,
        ILogger<KafkaErrorLog> logger)
    {
        _producer = producer;
        _options = options.Value;
        _routes = routes;
        _logger = logger;
    }

    public async Task InsertAsync(KafkaErrorLogEntry entry, CancellationToken cancellationToken)
    {
        if (!_routes.TryGetValue(entry.Topic, out var dlqTopic))
            throw new DlqNotConfiguredException(entry.Topic);

        var (payload, truncated) = TruncatePayload(entry.RawPayload, _options.MaxPayloadBytes);
        var envelope = new KafkaErrorEnvelope(
            SourceTopic: entry.Topic,
            SourcePartition: entry.Partition,
            SourceOffset: entry.Offset,
            ConsumerGroup: entry.ConsumerGroup,
            MessageKey: entry.MessageKey,
            RawPayload: payload,
            PayloadTruncated: truncated,
            ExceptionType: string.Empty,
            ExceptionMessage: entry.Error,
            ExceptionStack: entry.Stack,
            OccurredAt: entry.OccurredAt == default ? DateTimeOffset.UtcNow : entry.OccurredAt,
            LoggedAt: DateTimeOffset.UtcNow);

        var key = entry.MessageKey ?? $"{entry.Topic}:{entry.Partition}:{entry.Offset}";

        using var produceCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        produceCts.CancelAfter(TimeSpan.FromMilliseconds(_options.ProduceTimeoutMs));

        var result = await _producer.ProduceAsync(
            dlqTopic,
            new Message<string, KafkaErrorEnvelope> { Key = key, Value = envelope },
            produceCts.Token);

        _logger.LogDebug(
            "dlq-produce-ok dlqTopic={DlqTopic} sourceTopic={SourceTopic} sourcePartition={SourcePartition} sourceOffset={SourceOffset} truncated={Truncated}",
            result.Topic, entry.Topic, entry.Partition, entry.Offset, truncated);
    }

    internal static (byte[]? Payload, bool Truncated) TruncatePayload(byte[]? raw, int maxBytes)
    {
        if (raw is null) return (null, false);
        if (raw.Length <= maxBytes) return (raw, false);
        var truncated = new byte[maxBytes];
        Buffer.BlockCopy(raw, 0, truncated, 0, maxBytes);
        return (truncated, true);
    }
}

/// <summary>
/// Thrown by <see cref="KafkaErrorLog.InsertAsync"/> when the source topic of
/// the failed message has no DLQ configured. Caught by the consumer's
/// existing structured-log fallback path (SPEC-S-006 R-8 / SPEC-S-007 R-3 #4
/// three-tier model).
/// </summary>
public sealed class DlqNotConfiguredException : Exception
{
    public string SourceTopic { get; }

    public DlqNotConfiguredException(string sourceTopic)
        : base($"No DLQ configured for source topic '{sourceTopic}'. Falling through to structured-log tier.")
    {
        SourceTopic = sourceTopic;
    }
}
