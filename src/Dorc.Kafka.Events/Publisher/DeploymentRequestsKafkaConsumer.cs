using Confluent.Kafka;
using Dorc.Core.Events;
using Dorc.Kafka.Client.Connection;
using Dorc.Kafka.Client.Consumers;
using Dorc.Kafka.Client.Observability;
using Dorc.Kafka.Client.Serialization;
using Dorc.Kafka.ErrorLog;
using Dorc.Kafka.Events.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Events.Publisher;

/// <summary>
/// Monitor-side hosted service. Subscribes to BOTH
/// <c>dorc.requests.new</c> and <c>dorc.requests.status</c>, deserialises Avro
/// via factory, and invokes <see cref="IRequestEventHandler"/> per
/// record. The handler's only effect is to raise the poll-loop wake-up
/// signal — this consumer NEVER executes requests; that remains the
/// existing DB-poll path's responsibility (env-lock continues to
/// guard mutual exclusion).
///
/// Consumer group id: per-replica <c>dorc-monitor-requests.{HostInstanceId}</c>
/// so every Monitor replica sees every event (fan-out, mirroring ).
/// AutoOffsetReset overridden to Earliest to narrow the visibility gap on
/// consumer restart; rebalance-replay is harmless because handler is
/// idempotent + DB-state guards downstream paths.
///
/// Failure path mirrors results consumer: deserialise failure or
/// handler exception →  IKafkaErrorLog DAL → fall back to structured
/// LogError → super-degraded swallow. The consumer loop never crashes on a
/// single record; AutoCommit advances the offset past the poison record.
/// </summary>
public sealed class DeploymentRequestsKafkaConsumer : BackgroundService
{
    public const string ConsumerGroupPrefix = "dorc-monitor-requests";

    private readonly IKafkaConnectionProvider _connectionProvider;
    private readonly IKafkaSerializerFactory _serializerFactory;
    private readonly IRequestEventHandler _handler;
    private readonly KafkaConsumeFailureRecorder _failureRecorder;
    private readonly IKafkaConsumerMetrics _metrics;
    private readonly ILogger<DeploymentRequestsKafkaConsumer> _logger;

    public DeploymentRequestsKafkaConsumer(
        IKafkaConnectionProvider connectionProvider,
        IKafkaSerializerFactory serializerFactory,
        IRequestEventHandler handler,
        IKafkaErrorLog errorLog,
        IOptions<KafkaTopicsOptions> topics,
        IKafkaConsumerMetrics metrics,
        ILogger<DeploymentRequestsKafkaConsumer> logger)
    {
        _connectionProvider = connectionProvider;
        _serializerFactory = serializerFactory;
        _handler = handler;
        // Built from existing ctor deps (not injected) so the singleton DI
        // registration in the substrate extension stays untouched; the
        // collaborator itself is unit-tested directly via InternalsVisibleTo.
        _failureRecorder = new KafkaConsumeFailureRecorder(errorLog, logger);
        _metrics = metrics;
        _logger = logger;
        Topics = new[] { topics.Value.RequestsNew, topics.Value.RequestsStatus };
    }

    public string ConsumerGroupId { get; init; } = $"{ConsumerGroupPrefix}.{HostInstanceId.Value}";

    /// <summary>
    /// Subscribed topic set. Default is <c>{ KafkaTopicsOptions.RequestsNew,
    /// KafkaTopicsOptions.RequestsStatus }</c> as resolved at construction;
    /// tests may override via object initializer to subscribe to fixture-
    /// specific topics for isolation.
    /// </summary>
    public string[] Topics { get; init; }

    public int InsertTimeoutMs { get; init; } = 5_000;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => Task.Run(() => RunLoop(stoppingToken), stoppingToken);

    private void RunLoop(CancellationToken stoppingToken)
    {
        WarmupSerializers();
        using var consumer = BuildConsumer();
        consumer.Subscribe(Topics);
        _logger.LogInformation(
            "Kafka requests consumer subscribed: topics=[{Topics}] group={GroupId}",
            string.Join(",", Topics), ConsumerGroupId);

        while (!stoppingToken.IsCancellationRequested)
        {
            ConsumeResult<string, DeploymentRequestEventData>? result = null;
            try
            {
                result = consumer.Consume(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (ConsumeException ex)
            {
                HandleFailure(ex.ConsumerRecord, ex, stoppingToken);
                // Offset-store is disabled globally; advance past the poison
                // message so the consume loop doesn't busy-spin on it once the
                // error log is written. Best-effort — StoreOffset only stages,
                // commit happens on the auto-commit timer.
                if (ex.ConsumerRecord is not null)
                {
                    try
                    {
                        consumer.StoreOffset(new TopicPartitionOffset(
                            ex.ConsumerRecord.Topic,
                            ex.ConsumerRecord.Partition,
                            new Offset(ex.ConsumerRecord.Offset.Value + 1)));
                    }
                    catch (KafkaException) { /* best-effort */ }
                }
                else
                {
                    try { Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).Wait(stoppingToken); }
                    catch (OperationCanceledException) { break; }
                }
                continue;
            }

            if (result is null) continue;

            try
            {
                _handler.HandleAsync(result.Topic, result.Message.Value, stoppingToken)
                    .GetAwaiter().GetResult();
                consumer.StoreOffset(result);
                // Debug, not Information: per-record logging at Information
                // makes a full-topic replay (Earliest reset + fresh group)
                // operationally noisy — thousands of lines per restart.
                _logger.LogDebug(
                    "request-event-consumed topic={Topic} partition={Partition} offset={Offset} group={GroupId} requestId={RequestId} status={Status}",
                    result.Topic, result.Partition.Value, result.Offset.Value, ConsumerGroupId,
                    result.Message.Value.RequestId, result.Message.Value.Status);
            }
            catch (Exception ex) when (!IsCritical(ex))
            {
                // Broad by design: any handler-side failure (deserialize,
                // DAL, cancellation) routes to the error log so the consume
                // loop survives. Process-fatal exceptions still escape.
                HandleFailureFromConsumeResult(result, ex, stoppingToken);
                try { consumer.StoreOffset(result); }
                catch (KafkaException) { /* best-effort */ }
            }
        }

        try { consumer.Close(); } catch (KafkaException) { /* best-effort */ }
    }

    private void WarmupSerializers()
    {
        // Eagerly resolve the (topic, type) → schema-registry deserializer
        // before Subscribe so the first Consume call doesn't pay the
        // registry round-trip on the consume thread. A blocking call inside
        // Consume that exceeds max.poll.interval.ms fences the consumer
        // and triggers a group rebalance — preventable failure mode. The
        // call goes through IKafkaSerializerFactory so a wrapped/replaced
        // factory still gets the warmup hook (or no-ops via the interface's
        // default implementation if it doesn't talk to a registry).
        _serializerFactory.WarmupDeserializer<DeploymentRequestEventData>(Topics);
    }

    /// <summary>
    /// Shapes the consumer configuration. Exposed as <c>internal</c> so the
    /// tests can pin the commit-semantics invariants (the auto-commit timer
    /// is left running but offset *storage* is moved off Consume onto
    /// explicit StoreOffset only after the handler runs to completion
    /// otherwise a crash mid-handler silently advances past an unprocessed
    /// record).
    /// </summary>
    internal ConsumerConfig BuildConsumerConfig()
    {
        var config = _connectionProvider.GetConsumerConfig(ConsumerGroupId);
        config.AutoOffsetReset = AutoOffsetReset.Earliest;
        // Auto-commit timer is left enabled (low-overhead) but offset storage
        // is moved off the consume call and onto explicit StoreOffset after
        // handler success. This prevents a crash between consume return and
        // handler completion from silently advancing past an unprocessed
        // record — important because the handler is a no-op signal today but
        // future stateful additions (metrics, dedup state) would silently
        // lose messages otherwise.
        //
        // IMPORTANT: HandleAsync runs synchronously on the consumer poll
        // thread (.GetAwaiter().GetResult()). If handler latency exceeds
        // max.poll.interval.ms (default 300s), the broker will fence this
        // consumer and trigger a rebalance. Ensure the configured value
        // accommodates worst-case handler latency.
        config.EnableAutoCommit = true;
        config.EnableAutoOffsetStore = false;
        return config;
    }

    private IConsumer<string, DeploymentRequestEventData> BuildConsumer()
    {
        var config = BuildConsumerConfig();
        var handlers = new KafkaRebalanceHandlers<string, DeploymentRequestEventData>(_logger, ConsumerGroupId, _metrics);
        var builder = new ConsumerBuilder<string, DeploymentRequestEventData>(config)
            .SetErrorHandler(handlers.OnError)
            .SetStatisticsHandler(handlers.OnStatistics)
            .SetPartitionsAssignedHandler(handlers.OnPartitionsAssigned)
            .SetPartitionsRevokedHandler(handlers.OnPartitionsRevoked)
            .SetPartitionsLostHandler(handlers.OnPartitionsLost);

        var valueDeserializer = _serializerFactory.GetValueDeserializer<DeploymentRequestEventData>();
        if (valueDeserializer is not null) builder.SetValueDeserializer(valueDeserializer);

        return builder.Build();
    }

    private void HandleFailure(ConsumeResult<byte[], byte[]>? rawRecord, Exception failure, CancellationToken stoppingToken)
    {
        var entry = new KafkaErrorLogEntry
        {
            Topic = rawRecord?.Topic ?? "",
            Partition = rawRecord?.Partition.Value ?? -1,
            Offset = rawRecord?.Offset.Value ?? -1,
            ConsumerGroup = ConsumerGroupId,
            MessageKey = rawRecord?.Message?.Key is byte[] kb ? System.Text.Encoding.UTF8.GetString(kb) : null,
            // Deserialization failure: ConsumeException.ConsumerRecord carries
            // the raw bytes — preserve them so the DLQ envelope is replayable.
            RawPayload = rawRecord?.Message?.Value,
            Error = failure.Message,
            ExceptionType = failure.GetType().FullName,
            Stack = failure.StackTrace,
            OccurredAt = DateTimeOffset.UtcNow
        };
        _failureRecorder.Record(entry, TimeSpan.FromMilliseconds(InsertTimeoutMs), stoppingToken);
    }

    private void HandleFailureFromConsumeResult(
        ConsumeResult<string, DeploymentRequestEventData> result,
        Exception failure,
        CancellationToken stoppingToken)
    {
        var entry = new KafkaErrorLogEntry
        {
            Topic = result.Topic,
            Partition = result.Partition.Value,
            Offset = result.Offset.Value,
            ConsumerGroup = ConsumerGroupId,
            MessageKey = result.Message.Key,
            // Handler failure: the raw bytes are gone (already deserialised)
            // but the typed message is available — serialise it so the DLQ
            // envelope (requests.new has a DLQ) stays replayable and the
            // structured-log fallback can preserve the content. The DLQ tier
            // enforces KafkaErrorLogOptions.MaxPayloadBytes (with truncation
            // flag) on this value.
            RawPayload = KafkaConsumeFailureRecorder.SerializeTypedPayload(result.Message.Value),
            Error = failure.Message,
            ExceptionType = failure.GetType().FullName,
            Stack = failure.StackTrace,
            OccurredAt = DateTimeOffset.UtcNow
        };
        _failureRecorder.Record(entry, TimeSpan.FromMilliseconds(InsertTimeoutMs), stoppingToken);
    }

    private static bool IsCritical(Exception ex) =>
        ex is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException
            or System.Threading.ThreadAbortException;
}
