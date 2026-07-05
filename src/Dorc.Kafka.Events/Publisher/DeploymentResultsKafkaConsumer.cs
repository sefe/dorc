using Confluent.Kafka;
using Dorc.Core.Events;
using Dorc.Kafka.Client;
using Dorc.Kafka.Client.Configuration;
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
/// API-side hosted service that subscribes to <c>dorc.results.status</c>,
/// deserialises Avro via factory, and rebroadcasts via the
/// injected <see cref="IDeploymentResultBroadcaster"/> (production: SignalR).
///
/// multi-replica fan-out: consumer group id is
/// <c>dorc-api-results-status.{HostInstanceId}</c> so every API replica
/// consumes every event and broadcasts to its locally-pinned SignalR
/// clients. AutoOffsetReset overridden to Latest because status events
/// are real-time signals — a UI doesn't need historical replay on
/// consumer restart.
///
/// Failure path: poison messages and broadcast exceptions write a
/// KafkaErrorLogEntry via IKafkaErrorLog; if the DAL itself
/// throws, fall back to a structured LogError; super-degraded (logger
/// throws too) is swallowed so the consumer loop never crashes.
/// Offsets are stored only after the log path completes.
/// </summary>
public sealed class DeploymentResultsKafkaConsumer : BackgroundService
{
    public const string ConsumerGroupPrefix = "dorc-api-results-status";

    private readonly IKafkaConnectionProvider _connectionProvider;
    private readonly IKafkaSerializerFactory _serializerFactory;
    private readonly IDeploymentResultBroadcaster _broadcaster;
    private readonly KafkaConsumeFailureRecorder _failureRecorder;
    private readonly IKafkaConsumerMetrics _metrics;
    private readonly ILogger<DeploymentResultsKafkaConsumer> _logger;

    public DeploymentResultsKafkaConsumer(
        IKafkaConnectionProvider connectionProvider,
        IKafkaSerializerFactory serializerFactory,
        IDeploymentResultBroadcaster broadcaster,
        IKafkaErrorLog errorLog,
        IOptions<KafkaTopicsOptions> topics,
        IKafkaConsumerMetrics metrics,
        ILogger<DeploymentResultsKafkaConsumer> logger,
        IOptions<Client.Configuration.KafkaClientOptions>? clientOptions = null,
        bool useSharedConsumerGroup = false)
    {
        _connectionProvider = connectionProvider;
        _serializerFactory = serializerFactory;
        _broadcaster = broadcaster;
        // Built from existing ctor deps (not injected) so the singleton DI
        // registration in the substrate extension stays untouched; the
        // collaborator itself is unit-tested directly via InternalsVisibleTo.
        _failureRecorder = new KafkaConsumeFailureRecorder(errorLog, logger);
        _metrics = metrics;
        _logger = logger;
        TopicName = topics.Value.ResultsStatus;
        // Group identity has two modes (assigned in the ctor so test
        // object-initializer overrides still win):
        // - Per-replica (default, in-process SignalR): every replica consumes
        //   every event and broadcasts to ITS locally-pinned hub clients —
        //   exactly-once per client. Kafka:ReplicaId (MSI-written, tier-
        //   distinct) disambiguates co-hosted services.
        // - Shared/competing (Azure SignalR Service): hub sends are delivered
        //   service-wide to ALL clients regardless of which replica sends, so
        //   per-replica fan-out would broadcast every event N times per
        //   client. Exactly ONE consumer PER TIER must project each event —
        //   the shared group is still suffixed with Kafka:ReplicaId
        //   ("prod"/"nonprod", NOT the machine name: the group must span a
        //   tier's machines) because Prod and NonProd share one broker and
        //   one results topic; a single cross-tier group would deliver each
        //   event to exactly one tier's Azure SignalR service and silently
        //   starve the other tier's UI clients.
        _configuredReplicaId = clientOptions?.Value.ReplicaId;
        ConsumerGroupId = useSharedConsumerGroup
            ? string.IsNullOrWhiteSpace(_configuredReplicaId)
                ? ConsumerGroupPrefix
                : $"{ConsumerGroupPrefix}.{_configuredReplicaId.Trim()}"
            : $"{ConsumerGroupPrefix}.{HostInstanceId.For(_configuredReplicaId)}";
    }

    private readonly string? _configuredReplicaId;

    public string ConsumerGroupId { get; init; }

    /// <summary>
    /// The topic the consumer subscribes to. Default resolves to
    /// <c>KafkaTopicsOptions.ResultsStatus</c> at construction; tests can
    /// override via object initializer to subscribe to a unique topic for
    /// isolation.
    /// </summary>
    public string TopicName { get; init; }

    public int InsertTimeoutMs { get; init; } = 5_000;

    /// <summary>
    /// Per-attempt cap on <see cref="IDeploymentResultBroadcaster.BroadcastAsync"/>.
    /// The broadcast runs synchronously on the consume/poll thread, so a stalled
    /// broadcaster (e.g. a wedged SignalR backplane) that exceeds
    /// <c>max.poll.interval.ms</c> fences the consumer and triggers an endless
    /// rebalance. Bounding each attempt keeps the worst-case total broadcast time
    /// (<see cref="MaxBroadcastAttempts"/> × this + retry delays) safely under
    /// <c>max.poll.interval.ms</c> (default 300s). Audit finding F2.
    /// <para>Enforced via <see cref="System.Threading.Tasks.Task.WaitAsync(TimeSpan, CancellationToken)"/>,
    /// so the poll thread is released on timeout even if the broadcaster ignores
    /// the cancellation token (the production strongly-typed SignalR send does not
    /// observe one). The abandoned send is left to complete in the background.</para>
    /// </summary>
    public int BroadcastTimeoutMs { get; init; } = 10_000;

    /// <summary>
    /// Number of <see cref="IDeploymentResultBroadcaster.BroadcastAsync"/>
    /// attempts before a record is routed to the error-log path and its offset
    /// stored. <c>results.status</c> has no DLQ, so a single transient
    /// broadcaster failure would otherwise drop a real-time UI event forever;
    /// bounded retry recovers the transient case without stalling the partition
    /// on a permanently-failing record. Audit finding F1.
    /// </summary>
    public int MaxBroadcastAttempts { get; init; } = 3;

    /// <summary>Delay between broadcast retry attempts.</summary>
    public int BroadcastRetryDelayMs { get; init; } = 200;

    /// <summary>Outcome of a (possibly retried) broadcast attempt sequence.</summary>
    internal enum BroadcastOutcome
    {
        /// <summary>Delivered successfully — offset may be stored.</summary>
        Delivered,
        /// <summary>Stopping token fired — shut down without storing the offset.</summary>
        ShuttingDown,
        /// <summary>All attempts failed — route to the error-log + offset-advance path.</summary>
        Failed
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => Task.Run(() => RunLoop(stoppingToken), stoppingToken);

    private void RunLoop(CancellationToken stoppingToken)
    {
        // Warn early if falling back to MachineName: co-hosted replicas without
        // a replica identity share a consumer group, breaking the fan-out invariant.
        HostInstanceId.WarnIfFallingBackToMachineName(_logger, _configuredReplicaId);

        WarmupSerializers();
        using var consumer = BuildConsumer();
        consumer.Subscribe(TopicName);
        _logger.LogInformation(
            "Kafka results-status consumer subscribed: topic={Topic} group={GroupId}",
            TopicName, ConsumerGroupId);

        while (!stoppingToken.IsCancellationRequested)
        {
            ConsumeResult<string, DeploymentResultEventData>? result = null;
            try
            {
                result = consumer.Consume(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ConsumeException ex)
            {
                // Distinguish transient schema-registry connectivity failures from
                // actual poison messages. A registry outage throws ConsumeException
                // wrapping HttpRequestException (network unreachable) — routing
                // these to the error log would permanently discard valid deployment
                // result events that could not be deserialized only because the
                // schema registry was temporarily down. The results-status topic
                // has no DLQ, so routing to HandleFailure would silently drop the
                // message. Back off and leave the offset un-advanced instead.
                if (IsRegistryConnectivityFailure(ex))
                {
                    _logger.LogWarning(ex,
                        "Schema registry connectivity failure on results Consume — backing off 5s before retry. " +
                        "Message will be redelivered; offset NOT advanced.");
                    try { Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).Wait(stoppingToken); }
                    catch (OperationCanceledException) { break; }
                    continue;
                }

                HandleFailure(consumer, ex.ConsumerRecord, ex, stoppingToken);
                // On pure transport-failure ConsumeException (no ConsumerRecord),
                // back off briefly so a dead broker doesn't busy-spin the loop
                // and flood the error-log DAL.
                if (ex.ConsumerRecord is null)
                {
                    try { Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).Wait(stoppingToken); }
                    catch (OperationCanceledException) { break; }
                }
                continue;
            }
            catch (KafkaException ex) when (!CriticalExceptions.IsCritical(ex))
            {
                // Non-consume KafkaExceptions must not escape the loop: an unhandled
                // exception from a BackgroundService faults the hosted service and
                // triggers StopHost, taking the entire API down and defeating the
                // DB-poll fallback design.
                _logger.LogError(ex, "Kafka results consumer unexpected non-consume error; backing off 5s. code={Code}", ex.Error.Code);
                try { Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).Wait(stoppingToken); }
                catch (OperationCanceledException) { break; }
                continue;
            }

            if (result is null) continue;

            var outcome = TryBroadcast(result.Message.Value, stoppingToken, out var broadcastFailure);
            if (outcome == BroadcastOutcome.ShuttingDown)
                // Shutdown in progress — break out before storing the offset:
                // the broadcast never reached clients, so the offset must not
                // advance. The record will be redelivered after restart.
                break;
            if (outcome == BroadcastOutcome.Failed)
            {
                // Bounded retries exhausted (or a process-fatal exception was
                // re-thrown out of TryBroadcast). Route to the error log so the
                // payload is preserved (results.status has no DLQ) and the
                // offset is stored internally via AdvancePastFailedRecord, so
                // the loop doesn't stall on a permanently-failing record.
                HandleFailureFromConsumeResult(consumer, result, broadcastFailure!, stoppingToken);
                continue;
            }

            _logger.LogDebug(
                "broadcast-ok topic={Topic} partition={Partition} offset={Offset} group={GroupId} requestId={RequestId}",
                result.Topic, result.Partition.Value, result.Offset.Value, ConsumerGroupId, result.Message.Value.RequestId);

            // Store the offset only AFTER broadcast success (auto-offset-store
            // is disabled in BuildConsumerConfig; the auto-commit timer then
            // flushes stored offsets in the background — same pattern as the
            // sibling requests consumer). A crash between broadcast and the
            // next auto-commit means redelivery, which the file's contract
            // accepts: the record is re-broadcast, idempotent for UI status
            // projections. StoreOffset failure (e.g. partition revoked
            // mid-rebalance) is NOT a message failure — a warning suffices,
            // the record is redelivered and re-broadcast.
            try
            {
                consumer.StoreOffset(result);
            }
            catch (KafkaException storeEx)
            {
                _logger.LogWarning(storeEx,
                    "offset-store-failed-after-broadcast topic={Topic} partition={Partition} offset={Offset} group={GroupId} — broadcast already delivered; record may be redelivered after rebalance.",
                    result.Topic, result.Partition.Value, result.Offset.Value, ConsumerGroupId);
            }
        }

        try { consumer.Close(); } catch (KafkaException) { /* best-effort */ }
    }

    /// <summary>
    /// Runs the broadcast on the consume thread with a per-attempt timeout
    /// (<see cref="BroadcastTimeoutMs"/>) and bounded retries
    /// (<see cref="MaxBroadcastAttempts"/>). Audit findings F1 (retry transient
    /// failures before dropping a UI event on the DLQ-less results.status topic)
    /// and F2 (cap each attempt so a stalled broadcaster can't fence the
    /// consumer). Returns:
    /// <list type="bullet">
    ///   <item><see cref="BroadcastOutcome.Delivered"/> on success;</item>
    ///   <item><see cref="BroadcastOutcome.ShuttingDown"/> if the stopping token
    ///   fired (caller must break without storing the offset);</item>
    ///   <item><see cref="BroadcastOutcome.Failed"/> when every attempt failed —
    ///   <paramref name="lastFailure"/> carries the final exception for the
    ///   error-log path.</item>
    /// </list>
    /// Process-fatal exceptions (see <see cref="CriticalExceptions.IsCritical"/>) are never caught.
    /// </summary>
    internal BroadcastOutcome TryBroadcast(
        DeploymentResultEventData eventData,
        CancellationToken stoppingToken,
        out Exception? lastFailure)
    {
        lastFailure = null;
        var attempts = Math.Max(1, MaxBroadcastAttempts);
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            if (stoppingToken.IsCancellationRequested) return BroadcastOutcome.ShuttingDown;

            // Bound each attempt with Task.WaitAsync rather than relying on the
            // broadcaster to honour a cancellation token. The production SignalR
            // broadcaster's strongly-typed hub send does NOT observe a token, so a
            // token-based timeout would be inert; WaitAsync releases the poll
            // thread after BroadcastTimeoutMs regardless, which is what F2 actually
            // needs to prevent a max.poll.interval.ms fence. stoppingToken is still
            // passed to the broadcaster so a cooperative implementation can abort
            // early on shutdown.
            // Assigned inside the try so a broadcaster that throws synchronously
            // is still routed through the failure path rather than escaping.
            Task? broadcastTask = null;
            try
            {
                broadcastTask = _broadcaster.BroadcastAsync(eventData, stoppingToken);
                broadcastTask
                    .WaitAsync(TimeSpan.FromMilliseconds(BroadcastTimeoutMs), stoppingToken)
                    .GetAwaiter().GetResult();
                // Clear any earlier-attempt failure: on Delivered the out-param
                // must be null so callers never mistake a recovered transient
                // error for a real failure.
                lastFailure = null;
                return BroadcastOutcome.Delivered;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Genuine shutdown — not an attempt failure.
                return BroadcastOutcome.ShuttingDown;
            }
            catch (Exception ex) when (!CriticalExceptions.IsCritical(ex))
            {
                // Covers transient broadcaster failures AND a per-attempt timeout
                // (WaitAsync throws TimeoutException). lastFailure carries the
                // accurate reason for the error-log path — a timeout reads as a
                // TimeoutException, not a generic cancellation.
                var timedOut = ex is TimeoutException;
                if (timedOut && broadcastTask is not null)
                    // The orphaned send may still be running after the timeout;
                    // observe its eventual outcome so a later fault can't surface
                    // as an unobserved-task exception.
                    ObserveOrphanedBroadcast(broadcastTask);
                lastFailure = ex;
                _logger.LogWarning(ex,
                    "broadcast-attempt-failed attempt={Attempt}/{Max} timedOut={TimedOut} requestId={RequestId}",
                    attempt, attempts, timedOut, eventData.RequestId);

                if (attempt < attempts)
                {
                    try { Task.Delay(BroadcastRetryDelayMs, stoppingToken).Wait(stoppingToken); }
                    catch (OperationCanceledException) { return BroadcastOutcome.ShuttingDown; }
                }
            }
        }
        return BroadcastOutcome.Failed;
    }

    /// <summary>
    /// Observes a broadcast task abandoned after a per-attempt timeout so its
    /// eventual failure (if any) doesn't become an unobserved-task exception.
    /// </summary>
    private static void ObserveOrphanedBroadcast(Task broadcastTask)
        => _ = broadcastTask.ContinueWith(
            static t => { _ = t.Exception; },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

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
        _serializerFactory.WarmupDeserializer<DeploymentResultEventData>(new[] { TopicName });
    }

    /// <summary>
    /// Shapes the consumer configuration. Exposed as <c>internal</c> so the
    /// tests can pin the offset-semantics invariants (see
    /// strong-2/3 finding: store-on-consume must not leak into this
    /// consumer or a crash mid-broadcast can silently drop a SignalR
    /// projection — offset storage is gated on broadcast success).
    /// </summary>
    internal ConsumerConfig BuildConsumerConfig()
    {
        // Use connection provider for SASL / bootstrap / timeouts,
        // but override AutoOffsetReset to Latest (status events are
        // real-time; no historical replay) and group.id to the per-replica
        // identity.
        var config = _connectionProvider.GetConsumerConfig(ConsumerGroupId);
        config.AutoOffsetReset = AutoOffsetReset.Latest;
        // Auto-commit timer stays enabled (low-overhead) but offset STORAGE
        // is moved off the consume call and onto explicit StoreOffset — after
        // broadcast success on the happy path, or after the error-log tiers
        // via AdvancePastFailedRecord on the failure path (same pattern as
        // the sibling requests consumer). Setting both explicitly defends
        // against a provider/librdkafka default silently re-enabling
        // store-on-consume, which would let a crash mid-broadcast advance
        // past an undelivered SignalR projection. The crash-redelivery window
        // (stored but not yet timer-committed) is accepted: redelivered
        // records are re-broadcast, idempotent for UI status projections.
        //
        // IMPORTANT: BroadcastAsync runs synchronously on the consumer poll
        // thread (.GetAwaiter().GetResult()). If downstream SignalR latency
        // exceeds max.poll.interval.ms (default 300s), the broker will fence
        // this consumer and trigger a rebalance. Ensure the configured value
        // accommodates worst-case broadcast latency.
        config.EnableAutoCommit = true;
        config.EnableAutoOffsetStore = false;
        return config;
    }

    private IConsumer<string, DeploymentResultEventData> BuildConsumer()
    {
        var config = BuildConsumerConfig();
        var handlers = new KafkaRebalanceHandlers<string, DeploymentResultEventData>(_logger, ConsumerGroupId, _metrics);
        var builder = new ConsumerBuilder<string, DeploymentResultEventData>(config)
            .SetErrorHandler(handlers.OnError)
            .SetStatisticsHandler(handlers.OnStatistics)
            .SetPartitionsAssignedHandler(handlers.OnPartitionsAssigned)
            .SetPartitionsRevokedHandler(handlers.OnPartitionsRevoked)
            .SetPartitionsLostHandler(handlers.OnPartitionsLost);

        var valueDeserializer = _serializerFactory.GetValueDeserializer<DeploymentResultEventData>();
        if (valueDeserializer is not null) builder.SetValueDeserializer(valueDeserializer);

        return builder.Build();
    }

    private void HandleFailure(
        IConsumer<string, DeploymentResultEventData> consumer,
        ConsumeResult<byte[], byte[]>? rawRecord,
        Exception failure,
        CancellationToken stoppingToken)
    {
        // Three-tier failure recording (DLQ → structured log → swallow) and
        // entry construction live in the shared collaborator so both
        // consumers stay in lock-step. Topic fallback is this consumer's
        // single subscribed topic — a record-less transport failure is still
        // attributable to it.
        _failureRecorder.RecordRawRecordFailure(
            rawRecord, TopicName, ConsumerGroupId, failure,
            TimeSpan.FromMilliseconds(InsertTimeoutMs), stoppingToken);

        // No record ⇒ nothing to advance past.
        if (rawRecord is null) return;
        AdvancePastFailedRecord(consumer, rawRecord.Topic, rawRecord.Partition.Value, rawRecord.Offset.Value);
    }

    private void HandleFailureFromConsumeResult(
        IConsumer<string, DeploymentResultEventData> consumer,
        ConsumeResult<string, DeploymentResultEventData> result,
        Exception failure,
        CancellationToken stoppingToken)
    {
        // Broadcast failure: the recorder re-serialises the typed message so
        // the structured-log fallback preserves the content (results.status
        // has no DLQ route; the entry otherwise loses its payload forever
        // once the offset advances below).
        _failureRecorder.RecordTypedRecordFailure(
            result, ConsumerGroupId, failure,
            TimeSpan.FromMilliseconds(InsertTimeoutMs), stoppingToken);

        AdvancePastFailedRecord(consumer, result.Topic, result.Partition.Value, result.Offset.Value);
    }

    private void AdvancePastFailedRecord(
        IConsumer<string, DeploymentResultEventData> consumer,
        string topic,
        int partition,
        long offset)
    {
        try
        {
            // Store the failed record's next offset after the error-log tiers
            // ran, so the poisoned record doesn't stall the partition. Same
            // StoreOffset semantics as the happy path (auto-commit timer
            // flushes it); inherently scoped to the failed message's
            // partition only.
            var nextOffset = new TopicPartitionOffset(
                topic,
                new Partition(partition),
                new Offset(offset + 1));
            consumer.StoreOffset(nextOffset);
        }
        catch (KafkaException storeEx)
        {
            _logger.LogError(storeEx,
                "Kafka offset store failed after error-log path for topic={Topic} partition={Partition} offset={Offset}",
                topic, partition, offset);
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> when the <see cref="ConsumeException"/> was caused by a
    /// transient schema registry failure rather than a malformed (poison) message.
    /// <para>
    /// <see cref="Confluent.SchemaRegistry.SchemaRegistryException"/> inherits from
    /// <see cref="System.Net.Http.HttpRequestException"/>, so sub-type order matters:
    /// </para>
    /// <list type="bullet">
    ///   <item>5xx <see cref="Confluent.SchemaRegistry.SchemaRegistryException"/> — server error, transient → back off.</item>
    ///   <item>4xx <see cref="Confluent.SchemaRegistry.SchemaRegistryException"/> (e.g. 40403 Schema Not Found) — deterministic failure → HandleFailure.</item>
    ///   <item>Plain <see cref="System.Net.Http.HttpRequestException"/> (no HTTP response) — network unreachable → back off.</item>
    /// </list>
    /// The results-status topic has no DLQ; routing 4xx errors to HandleFailure
    /// is still preferable to an infinite backoff loop that permanently stalls
    /// the partition.
    /// </summary>
    internal static bool IsRegistryConnectivityFailure(ConsumeException ex)
        => KafkaRegistryFailureClassifier.IsTransientRegistryFailure(ex);
}
