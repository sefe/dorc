using Confluent.Kafka;
using Dorc.Kafka.Client;
using Dorc.Kafka.ErrorLog;
using Microsoft.Extensions.Logging;

namespace Dorc.Kafka.Events.Publisher;

/// <summary>
/// Shared failure-recording collaborator for the Kafka consumers
/// (<see cref="DeploymentRequestsKafkaConsumer"/> and
/// <see cref="DeploymentResultsKafkaConsumer"/>). Owns both the construction
/// of <see cref="KafkaErrorLogEntry"/> records (see
/// <see cref="RecordRawRecordFailure"/> /
/// <see cref="RecordTypedRecordFailure{TValue}"/>) and the three-tier
/// failure-handling contract, in ONE place so the consumers cannot drift:
/// <list type="number">
/// <item><description>DLQ tier — <see cref="IKafkaErrorLog.InsertAsync"/>
/// under a timeout linked to the host's stopping token (so shutdown is never
/// blocked waiting on an unreachable DLQ broker). The DLQ tier applies its
/// own <c>KafkaErrorLogOptions.MaxPayloadBytes</c> truncation and stamps the
/// envelope's <c>PayloadTruncated</c> flag.</description></item>
/// <item><description>Structured-log tier — if the DLQ insert throws, write
/// a structured <c>LogError</c> that includes a bounded base64 rendering of
/// the payload. Without it, the message content is unrecoverable once the
/// consumer's offset advances past the poison record.</description></item>
/// <item><description>Super-degraded tier — if the logger itself throws,
/// swallow so a single bad record cannot crash the consume loop.</description></item>
/// </list>
/// Instantiated by the consumers from their existing constructor dependencies
/// (no DI registration needed); <c>internal</c> + InternalsVisibleTo so unit
/// tests drive the REAL production path rather than a copy.
/// </summary>
internal sealed class KafkaConsumeFailureRecorder
{
    /// <summary>
    /// Cap on the raw bytes rendered into the structured-log fallback. Kept
    /// small (a few KB) because this lands in the app log, not the DLQ — big
    /// enough to reconstruct/replay typical event payloads, small enough not
    /// to blow up log sinks. Truncation is flagged via
    /// <see cref="TruncationMarker"/> appended to the base64 text.
    /// </summary>
    internal const int FallbackLogPayloadCapBytes = 4_096;

    internal const string TruncationMarker = "...[TRUNCATED]";

    private readonly IKafkaErrorLog _errorLog;
    private readonly ILogger _logger;

    public KafkaConsumeFailureRecorder(IKafkaErrorLog errorLog, ILogger logger)
    {
        _errorLog = errorLog;
        _logger = logger;
    }

    /// <summary>
    /// Builds and records the entry for a raw-record failure — a consume /
    /// deserialization error, where <c>ConsumeException.ConsumerRecord</c>
    /// may be null (pure transport failure with no record attached). The raw
    /// bytes are preserved so the DLQ envelope (where one exists) stays
    /// replayable.
    /// </summary>
    /// <param name="rawRecord">The failed raw record, if any.</param>
    /// <param name="topicFallback">Fills <see cref="KafkaErrorLogEntry.Topic"/>
    /// when no record is available. The results consumer passes its single
    /// subscribed topic name; the requests consumer subscribes to multiple
    /// topics (no singular fallback exists) so it passes <c>""</c> — the
    /// difference is a deliberate, visible argument instead of drift between
    /// hand-rolled entry copies.</param>
    /// <param name="consumerGroup">The reporting consumer's group id.</param>
    /// <param name="failure">The consume/deserialize exception.</param>
    /// <param name="insertTimeout">Upper bound on the DLQ insert.</param>
    /// <param name="stoppingToken">Host stopping token; linked into the
    /// insert so shutdown isn't blocked for up to the timeout per record.</param>
    public void RecordRawRecordFailure(
        ConsumeResult<byte[], byte[]>? rawRecord,
        string topicFallback,
        string consumerGroup,
        Exception failure,
        TimeSpan insertTimeout,
        CancellationToken stoppingToken)
    {
        var entry = new KafkaErrorLogEntry
        {
            Topic = rawRecord?.Topic ?? topicFallback,
            Partition = rawRecord?.Partition.Value ?? -1,
            Offset = rawRecord?.Offset.Value ?? -1,
            ConsumerGroup = consumerGroup,
            MessageKey = rawRecord?.Message?.Key is byte[] kb ? System.Text.Encoding.UTF8.GetString(kb) : null,
            // Deserialization failure: ConsumeException.ConsumerRecord carries
            // the raw bytes — preserve them for triage/replay.
            RawPayload = rawRecord?.Message?.Value,
            Error = failure.Message,
            ExceptionType = failure.GetType().FullName,
            Stack = failure.StackTrace,
            OccurredAt = DateTimeOffset.UtcNow
        };
        Record(entry, insertTimeout, stoppingToken);
    }

    /// <summary>
    /// Builds and records the entry for a typed-record failure — a handler /
    /// broadcast exception thrown after successful deserialization. The raw
    /// Kafka bytes are gone at that point, so the typed value is re-serialised
    /// as JSON via <see cref="SerializeTypedPayload{T}"/>: without a payload
    /// the entry is untriageable once the consumer's offset advances.
    /// </summary>
    /// <param name="result">The successfully-deserialised record whose
    /// handling failed.</param>
    /// <param name="consumerGroup">The reporting consumer's group id.</param>
    /// <param name="failure">The handler/broadcast exception.</param>
    /// <param name="insertTimeout">Upper bound on the DLQ insert.</param>
    /// <param name="stoppingToken">Host stopping token; linked into the
    /// insert so shutdown isn't blocked for up to the timeout per record.</param>
    public void RecordTypedRecordFailure<TValue>(
        ConsumeResult<string, TValue> result,
        string consumerGroup,
        Exception failure,
        TimeSpan insertTimeout,
        CancellationToken stoppingToken)
    {
        var entry = new KafkaErrorLogEntry
        {
            Topic = result.Topic,
            Partition = result.Partition.Value,
            Offset = result.Offset.Value,
            ConsumerGroup = consumerGroup,
            MessageKey = result.Message.Key,
            RawPayload = SerializeTypedPayload(result.Message.Value),
            Error = failure.Message,
            ExceptionType = failure.GetType().FullName,
            Stack = failure.StackTrace,
            OccurredAt = DateTimeOffset.UtcNow
        };
        Record(entry, insertTimeout, stoppingToken);
    }

    /// <summary>
    /// Records a consume/handler failure through the three tiers. Never
    /// throws (other than process-fatal exceptions, which always escape).
    /// </summary>
    /// <param name="entry">The failure record; the
    /// <c>Record*RecordFailure</c> factory methods populate
    /// <see cref="KafkaErrorLogEntry.RawPayload"/> (raw bytes for
    /// deserialization failures, JSON of the typed value for handler
    /// failures) and <see cref="KafkaErrorLogEntry.ExceptionType"/>.</param>
    /// <param name="insertTimeout">Upper bound on the DLQ insert.</param>
    /// <param name="stoppingToken">Host stopping token; linked into the
    /// insert so shutdown isn't blocked for up to the timeout per record.</param>
    internal void Record(KafkaErrorLogEntry entry, TimeSpan insertTimeout, CancellationToken stoppingToken)
    {
        try
        {
            using var insertCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            insertCts.CancelAfter(insertTimeout);
            _errorLog.InsertAsync(entry, insertCts.Token).GetAwaiter().GetResult();
            _logger.LogError(
                "error-logged topic={Topic} partition={Partition} offset={Offset} group={GroupId} error={Error}",
                entry.Topic, entry.Partition, entry.Offset, entry.ConsumerGroup, entry.Error);
        }
        catch (Exception dalEx) when (!CriticalExceptions.IsCritical(dalEx))
        {
            try
            {
                // Last chance to preserve the message content: once the
                // consumer's offset advances, the payload is unrecoverable
                // from Kafka. Bounded base64 so a poison megabyte can't take
                // out the log sink.
                _logger.LogError(dalEx,
                    "error-fallback-structured-log topic={Topic} partition={Partition} offset={Offset} group={GroupId} key={Key} error={Error} exceptionType={ExceptionType} payloadBase64={PayloadBase64}",
                    entry.Topic, entry.Partition, entry.Offset, entry.ConsumerGroup, entry.MessageKey,
                    entry.Error, entry.ExceptionType, EncodePayloadForLog(entry.RawPayload));
            }
            catch (Exception logEx) when (!CriticalExceptions.IsCritical(logEx))
            {
                // Super-degraded: logger itself threw. Swallow so the
                // consumer loop survives a single bad record instead of
                // crashing the whole acceleration layer.
            }
        }
    }

    /// <summary>
    /// Bounded base64 rendering of the payload for the structured-log tier.
    /// Truncates the RAW bytes at <see cref="FallbackLogPayloadCapBytes"/>
    /// and appends an explicit marker so operators know the rendering is
    /// partial.
    /// </summary>
    internal static string EncodePayloadForLog(byte[]? raw)
    {
        if (raw is null || raw.Length == 0) return "(none)";
        if (raw.Length <= FallbackLogPayloadCapBytes) return Convert.ToBase64String(raw);
        return Convert.ToBase64String(raw.AsSpan(0, FallbackLogPayloadCapBytes)) + TruncationMarker;
    }

    /// <summary>
    /// JSON-serialises the already-deserialised typed message for the
    /// handler-failure path, where the raw Kafka bytes are no longer
    /// available but the typed value is. A DLQ envelope without a payload
    /// cannot be replayed, so losing this would make requests.new poison
    /// records untriageable. Returns null (never throws) if serialisation
    /// itself fails — the error-log entry still carries the metadata.
    /// Size is NOT capped here: the DLQ tier truncates to
    /// <c>KafkaErrorLogOptions.MaxPayloadBytes</c> (flagging the envelope)
    /// and the structured-log tier applies its own bound.
    /// </summary>
    internal static byte[]? SerializeTypedPayload<T>(T value)
    {
        try
        {
            return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value);
        }
        catch (Exception ex) when (!CriticalExceptions.IsCritical(ex))
        {
            return null;
        }
    }
}
