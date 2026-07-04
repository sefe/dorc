using Confluent.Kafka;
using Dorc.Core.Events;
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
/// Offset commits only after the log path completes.
/// </summary>
public sealed class DeploymentResultsKafkaConsumer : BackgroundService
{
    public const string ConsumerGroupPrefix = "dorc-api-results-status";

    private readonly IKafkaConnectionProvider _connectionProvider;
    private readonly IKafkaSerializerFactory _serializerFactory;
    private readonly IDeploymentResultBroadcaster _broadcaster;
    private readonly KafkaConsumeFailureRecorder _failureRecorder;
    private readonly KafkaErrorLogOptions _errorLogOptions;
    private readonly IKafkaConsumerMetrics _metrics;
    private readonly ILogger<DeploymentResultsKafkaConsumer> _logger;

    public DeploymentResultsKafkaConsumer(
        IKafkaConnectionProvider connectionProvider,
        IKafkaSerializerFactory serializerFactory,
        IDeploymentResultBroadcaster broadcaster,
        IKafkaErrorLog errorLog,
        IOptions<KafkaErrorLogOptions> errorLogOptions,
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
        _errorLogOptions = errorLogOptions.Value;
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
        //   client. Exactly ONE consumer service-wide must project each event.
        _configuredReplicaId = clientOptions?.Value.ReplicaId;
        ConsumerGroupId = useSharedConsumerGroup
            ? ConsumerGroupPrefix
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
    /// committed. <c>results.status</c> has no DLQ, so a single transient
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
        /// <summary>Delivered successfully — offset may be committed.</summary>
        Delivered,
        /// <summary>Stopping token fired — shut down without committing.</summary>
        ShuttingDown,
        /// <summary>All attempts failed — route to the error-log + commit path.</summary>
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
            catch (KafkaException ex) when (!IsCritical(ex))
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
                // Shutdown in progress — break out before committing: the
                // broadcast never reached clients, so the offset must not
                // advance. The record will be redelivered after restart.
                break;
            if (outcome == BroadcastOutcome.Failed)
            {
                // Bounded retries exhausted (or a process-fatal exception was
                // re-thrown out of TryBroadcast). Route to the error log so the
                // payload is preserved (results.status has no DLQ) and the
                // offset is committed internally via WriteErrorLogAndCommit, so
                // the loop doesn't stall on a permanently-failing record.
                HandleFailureFromConsumeResult(consumer, result, broadcastFailure!, stoppingToken);
                continue;
            }

            _logger.LogInformation(
                "broadcast-ok topic={Topic} partition={Partition} offset={Offset} group={GroupId} requestId={RequestId}",
                result.Topic, result.Partition.Value, result.Offset.Value, ConsumerGroupId, result.Message.Value.RequestId);

            // Commit OUTSIDE the broadcast try: the broadcast already
            // succeeded, so a commit failure (e.g. mid-rebalance) is NOT a
            // message failure — routing it into the error-log path would
            // produce a spurious DLQ/DlqNotConfiguredException entry and a
            // second commit attempt. A warning suffices: on rebalance the
            // record is redelivered and re-broadcast (idempotent for UI
            // status projections).
            try
            {
                consumer.Commit(result);
            }
            catch (KafkaException commitEx)
            {
                _logger.LogWarning(commitEx,
                    "offset-commit-failed-after-broadcast topic={Topic} partition={Partition} offset={Offset} group={GroupId} — broadcast already delivered; record may be redelivered after rebalance.",
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
    ///   fired (caller must break without committing);</item>
    ///   <item><see cref="BroadcastOutcome.Failed"/> when every attempt failed —
    ///   <paramref name="lastFailure"/> carries the final exception for the
    ///   error-log path.</item>
    /// </list>
    /// Process-fatal exceptions (see <see cref="IsCritical"/>) are never caught.
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
            catch (Exception ex) when (!IsCritical(ex))
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
    /// tests can pin the commit-semantics invariants (see
    /// strong-2/3 finding: a global <c>EnableAutoCommit=true</c> override
    /// must not leak into this consumer or a crash mid-broadcast can
    /// silently drop a SignalR projection).
    /// </summary>
    internal ConsumerConfig BuildConsumerConfig()
    {
        // Use connection provider for SASL / bootstrap / timeouts,
        // but override AutoOffsetReset to Latest (status events are
        // real-time; no historical replay) and group.id to the per-replica
        // identity.
        var config = _connectionProvider.GetConsumerConfig(ConsumerGroupId);
        config.AutoOffsetReset = AutoOffsetReset.Latest;
        // Manual commit-only: every offset advances via consumer.Commit(result)
        // after broadcast success or via WriteErrorLogAndCommit's typed
        // TopicPartitionOffset commit on the error path. Setting this
        // explicitly defends against an operator setting Kafka:EnableAutoCommit
        // = true globally and silently dropping a SignalR broadcast on crash
        // between the timer-fired auto-commit and BroadcastAsync completion.
        //
        // IMPORTANT: BroadcastAsync runs synchronously on the consumer poll
        // thread (.GetAwaiter().GetResult()). If downstream SignalR latency
        // exceeds max.poll.interval.ms (default 300s), the broker will fence
        // this consumer and trigger a rebalance. Ensure the configured value
        // accommodates worst-case broadcast latency.
        config.EnableAutoCommit = false;
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
        var entry = new KafkaErrorLogEntry
        {
            Topic = rawRecord?.Topic ?? TopicName,
            Partition = rawRecord?.Partition.Value ?? -1,
            Offset = rawRecord?.Offset.Value ?? -1,
            ConsumerGroup = ConsumerGroupId,
            MessageKey = rawRecord?.Message?.Key is byte[] kb ? System.Text.Encoding.UTF8.GetString(kb) : null,
            // Deserialization failure: ConsumeException.ConsumerRecord carries
            // the raw bytes — preserve them for triage/replay.
            RawPayload = rawRecord?.Message?.Value,
            Error = failure.Message,
            ExceptionType = failure.GetType().FullName,
            Stack = failure.StackTrace,
            OccurredAt = DateTimeOffset.UtcNow
        };
        WriteErrorLogAndCommit(consumer, entry, advanceOffset: rawRecord is not null, stoppingToken);
    }

    private void HandleFailureFromConsumeResult(
        IConsumer<string, DeploymentResultEventData> consumer,
        ConsumeResult<string, DeploymentResultEventData> result,
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
            // Broadcast failure: the raw bytes are gone (already deserialised)
            // but the typed message is available — serialise it so the
            // structured-log fallback preserves the content (results.status
            // has no DLQ route; the entry otherwise loses its payload forever
            // once the offset is committed below).
            RawPayload = KafkaConsumeFailureRecorder.SerializeTypedPayload(result.Message.Value),
            Error = failure.Message,
            ExceptionType = failure.GetType().FullName,
            Stack = failure.StackTrace,
            OccurredAt = DateTimeOffset.UtcNow
        };
        WriteErrorLogAndCommit(consumer, entry, advanceOffset: true, stoppingToken);
    }

    private void WriteErrorLogAndCommit(
        IConsumer<string, DeploymentResultEventData> consumer,
        KafkaErrorLogEntry entry,
        bool advanceOffset,
        CancellationToken stoppingToken)
    {
        // Three-tier failure recording (DLQ → structured log → swallow) lives
        // in the shared collaborator so both consumers stay in lock-step.
        _failureRecorder.Record(entry, TimeSpan.FromMilliseconds(InsertTimeoutMs), stoppingToken);

        if (!advanceOffset) return;

        try
        {
            // Scope commit to the failed message's partition only. The no-arg
            // overload would commit every assigned partition's last consumed
            // offset, which under CooperativeSticky rebalancing can advance
            // partitions whose in-flight messages haven't been processed yet.
            var nextOffset = new TopicPartitionOffset(
                entry.Topic,
                new Partition(entry.Partition),
                new Offset(entry.Offset + 1));
            consumer.Commit(new[] { nextOffset });
        }
        catch (KafkaException commitEx)
        {
            _logger.LogError(commitEx,
                "Kafka offset commit failed after error-log path for topic={Topic} partition={Partition} offset={Offset}",
                entry.Topic, entry.Partition, entry.Offset);
        }
    }

    private static bool IsCritical(Exception ex) =>
        ex is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException
            or System.Threading.ThreadAbortException;

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
