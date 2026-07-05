using System.Collections.Concurrent;
using Confluent.Kafka;
using Dorc.Kafka.Client;
using Dorc.Kafka.Client.Connection;
using Dorc.Kafka.Client.Consumers;
using Dorc.Kafka.Events.Configuration;
using Dorc.Kafka.Lock.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Lock;

/// <summary>
/// Singleton consumer-group wrapper that owns the single Kafka consumer joined
/// to the lock topic. Partition ownership = distributed lock ownership.
///
/// Cooperative-sticky rebalance provides per-partition revoke/assign events;
/// each partition has a <see cref="PartitionSlot"/> carrying a live
/// <see cref="CancellationTokenSource"/> (fired on revoke/lost → LockLostToken)
/// and a <see cref="TaskCompletionSource{T}"/> signalling ownership acquisition.
/// Slot replacement on revoke→reassign is atomic under a per-coordinator lock
/// so concurrent <see cref="WaitForPartitionOwnershipAsync"/> callers never
/// observe a stale (cancelled) token as "still live". Lock-holder callbacks
/// are dispatched on the thread pool — never inline on the consume/rebalance
/// thread, and never under the slot lock.
///
/// Split-brain guard: librdkafka does not fire revoked/lost callbacks when the
/// node merely loses broker connectivity, but the broker reassigns our
/// partitions to a peer after <c>session.timeout.ms</c>. A connectivity
/// watchdog tracks the last successful broker contact and cancels every slot
/// once <see cref="KafkaLocksOptions.LivenessTimeoutMs"/> elapses without
/// contact; the loop keeps running so slots re-establish on reconnect via the
/// rebalance callbacks. Fatal consumer errors likewise cancel all slots, then
/// the consumer is disposed and rebuilt with bounded backoff.
///
/// No user records are produced or consumed for lock semantics — the consume
/// loop exists only to keep the librdkafka heartbeat thread scheduled; any
/// record that happens to arrive is silently discarded.
/// </summary>
public sealed class KafkaLockCoordinator : IHostedService, IAsyncDisposable
{
    private const int RebuildBackoffInitialMs = 500;
    private const int RebuildBackoffMaxMs = 30_000;
    // Absolute lower bound on the liveness timeout so very short
    // session.timeout.ms values don't produce a sub-second watchdog.
    private const int LivenessFloorMs = 10_000;
    private const int LibrdkafkaDefaultSessionTimeoutMs = 45_000;

    private readonly IKafkaConnectionProvider _connectionProvider;
    private readonly KafkaLocksOptions _options;
    private readonly KafkaTopicsOptions _topics;
    private readonly ILogger<KafkaLockCoordinator> _logger;
    private readonly TimeProvider _timeProvider;

    private readonly object _slotLock = new();
    private readonly ConcurrentDictionary<int, PartitionSlot> _slots = new();

    private IConsumer<byte[], byte[]>? _consumer;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private volatile bool _disposed;

    // Connectivity watchdog state. _lastBrokerContactTimestamp is a
    // TimeProvider timestamp; _livenessTripped makes the trip one-shot until
    // contact is restored; _connectivitySuspect is set when the error handler
    // reports a transport-class error, suppressing empty-poll contact counting
    // (librdkafka also returns empty polls during half-open TCP connections).
    // While suspect, an active probe (group-coordinator round-trip via
    // Committed) runs every ProbeIntervalMs — a healthy cluster clears the
    // suspicion within one probe, so a benign single-connection disconnect
    // (idle reap, one broker of three restarting) can never trip the watchdog.
    private long _lastBrokerContactTimestamp;
    private long _lastProbeTimestamp;
    private volatile bool _livenessTripped;
    private volatile bool _connectivitySuspect;
    private TimeSpan? _livenessTimeout;
    private const int ProbeIntervalMs = 5_000;
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

    // Set on fatal consumer errors; the consume loop disposes and rebuilds the
    // consumer (with bounded backoff) instead of spinning on a dead handle.
    private volatile bool _rebuildRequested;

    public KafkaLockCoordinator(
        IKafkaConnectionProvider connectionProvider,
        IOptions<KafkaLocksOptions> options,
        IOptions<KafkaTopicsOptions> topics,
        ILogger<KafkaLockCoordinator> logger,
        TimeProvider? timeProvider = null,
        IOptions<Client.Configuration.KafkaClientOptions>? clientOptions = null)
    {
        _connectionProvider = connectionProvider;
        _options = options.Value;
        _topics = topics.Value;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _lastBrokerContactTimestamp = _timeProvider.GetTimestamp();
        _configuredReplicaId = clientOptions?.Value.ReplicaId;
    }

    // Same replica-identity channel as the event consumers (Kafka:ReplicaId
    // config, DORC_REPLICA_ID env var outranking it) so the static
    // group.instance.id below can never diverge from the identity the rest
    // of the substrate uses.
    private readonly string? _configuredReplicaId;

    public KafkaLocksOptions Options => _options;

    /// <summary>
    /// Test seam: replaces <see cref="BuildConsumer"/> so the consume loop can
    /// be driven against a scripted consumer without a broker.
    /// </summary>
    internal Func<IConsumer<byte[], byte[]>>? ConsumerFactoryOverride { get; set; }

    internal bool IsConsumerRebuildRequested => _rebuildRequested;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("KafkaLockCoordinator disabled via options; lock service will return null on acquire.");
            return Task.CompletedTask;
        }

        _loopCts = new CancellationTokenSource();
        _consumer = CreateConsumer();
        _consumer.Subscribe(_topics.Locks);
        _logger.LogInformation(
            "KafkaLockCoordinator subscribed: topic={Topic} group={GroupId} partitions={PartitionCount} livenessTimeout={LivenessTimeout}",
            _topics.Locks, _options.ConsumerGroupId, _options.PartitionCount, LivenessTimeout);

        RecordBrokerContact();
        // Long-lived loop: don't pin a thread-pool worker for the process
        // lifetime — ask the scheduler for a dedicated thread.
        _loopTask = Task.Factory.StartNew(
            () => RunLoop(_loopCts.Token),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await DisposeAsync().ConfigureAwait(false);
    }

    private IConsumer<byte[], byte[]> CreateConsumer() =>
        ConsumerFactoryOverride?.Invoke() ?? BuildConsumer();

    private IConsumer<byte[], byte[]> BuildConsumer()
    {
        var config = _connectionProvider.GetConsumerConfig(_options.ConsumerGroupId);
        // Lock semantics don't use committed offsets — auto-commit is harmless
        // (the topic has no meaningful records) and cheaper than manual commit.
        config.EnableAutoCommit = true;
        config.AutoOffsetReset = AutoOffsetReset.Latest;
        // Lock-specific session timeout: the outage-grace budget for held
        // locks (see KafkaLocksOptions.SessionTimeoutMs). The liveness
        // watchdog derives from the same effective value below.
        if (_options.SessionTimeoutMs is { } sessionMs)
            config.SessionTimeoutMs = sessionMs;
        // Static membership: a restart that rejoins within session.timeout.ms
        // reclaims the same partitions with no rebalance, so peer Monitors'
        // held locks survive routine service restarts / rolling upgrades.
        // The instance id must be unique per group member: group id scopes it
        // per tier, host identity scopes it per machine/replica. Two members
        // presenting the SAME instance id fence each other (fatal error →
        // slot cancel → rebuild → re-fence, a locking-outage ping-pong), so
        // same-tier co-hosted replicas MUST set DORC_REPLICA_ID — the
        // fan-out consumer warning covers the same topology.
        if (_options.UseStaticGroupMembership)
            config.GroupInstanceId = $"{_options.ConsumerGroupId}.{Events.Publisher.HostInstanceId.For(_configuredReplicaId)}";

        var handlers = new KafkaRebalanceHandlers<byte[], byte[]>(_logger, "dorc-lock-coordinator");

        return new ConsumerBuilder<byte[], byte[]>(config)
            .SetErrorHandler((c, e) =>
            {
                handlers.OnError(c, e);
                OnConsumerError(e);
            })
            .SetStatisticsHandler(handlers.OnStatistics)
            .SetPartitionsAssignedHandler((c, parts) =>
            {
                handlers.OnPartitionsAssigned(c, parts);
                foreach (var tp in parts) OnAssigned(tp.Partition.Value);
            })
            .SetPartitionsRevokedHandler((c, parts) =>
            {
                handlers.OnPartitionsRevoked(c, parts);
                foreach (var tp in parts) OnRevokedOrLost(tp.Partition.Value, lost: false);
            })
            .SetPartitionsLostHandler((c, parts) =>
            {
                handlers.OnPartitionsLost(c, parts);
                foreach (var tp in parts) OnRevokedOrLost(tp.Partition.Value, lost: true);
            })
            .Build();
    }

    private void RunLoop(CancellationToken stoppingToken)
    {
        var rebuildBackoffMs = RebuildBackoffInitialMs;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (_rebuildRequested)
                {
                    RebuildConsumer(stoppingToken, ref rebuildBackoffMs);
                    continue;
                }

                try
                {
                    // Return of null ConsumeResult on timeout is fine; we don't act on records.
                    var result = _consumer?.Consume(TimeSpan.FromMilliseconds(500));
                    if (result is not null)
                    {
                        // A delivered record is unambiguous proof of broker contact.
                        _connectivitySuspect = false;
                        RecordBrokerContact();
                    }
                    else if (!_connectivitySuspect)
                    {
                        // The lock topic carries no user records by design, so on an
                        // idle topic with a healthy broker, empty polls are the only
                        // available contact signal. We count them ONLY when no
                        // transport-class error has been flagged: OnConsumerError sets
                        // _connectivitySuspect = true for Local_Transport /
                        // Local_AllBrokersDown errors, and librdkafka can return empty
                        // polls during a half-open TCP connection — counting those
                        // would allow the watchdog to keep resetting during a silent
                        // disconnect (the split-brain scenario it exists to prevent).
                        RecordBrokerContact();
                    }
                    rebuildBackoffMs = RebuildBackoffInitialMs;
                }
                catch (OperationCanceledException) { break; }
                catch (ConsumeException ex)
                {
                    if (ex.Error.IsFatal)
                    {
                        // A fatal librdkafka error permanently kills the consumer:
                        // spinning on it would hold locks forever on a dead handle.
                        _logger.LogError(ex,
                            "KafkaLockCoordinator fatal consume error: code={Code} reason={Reason}. Cancelling all lock slots and rebuilding the consumer.",
                            ex.Error.Code, ex.Error.Reason);
                        OnFatalConsumerError(ex.Error);
                        continue;
                    }

                    // A broker-originated error response still proves connectivity;
                    // a local/transport error does not — the broker may already have
                    // reassigned our partitions to a peer (split-brain risk).
                    if (!ex.Error.IsLocalError)
                    {
                        _connectivitySuspect = false;
                        RecordBrokerContact();
                    }
                    else if (ex.Error.Code is ErrorCode.Local_Transport or ErrorCode.Local_AllBrokersDown)
                    {
                        // Mirror the OnConsumerError logic: transport-class ConsumeExceptions
                        // must also suppress empty-poll contact counting so the watchdog
                        // fires correctly when the error arrives via the consume path rather
                        // than the error callback.
                        _connectivitySuspect = true;
                    }

                    _logger.LogWarning(ex, "KafkaLockCoordinator consume warning: {Reason}", ex.Error.Reason);
                    // Back off briefly so a dead broker doesn't busy-spin.
                    if (!SleepUnlessStopping(1_000, stoppingToken)) break;
                }
                catch (Exception ex) when (!CriticalExceptions.IsCritical(ex))
                {
                    // Safety net for the long-running consume loop: log and
                    // continue rather than tear the host down on a transient
                    // unknown failure. Process-fatal exceptions still escape
                    // via CriticalExceptions.IsCritical so the runtime can restart us cleanly.
                    _logger.LogError(ex, "KafkaLockCoordinator unexpected consume-loop error");
                    if (!SleepUnlessStopping(1_000, stoppingToken)) break;
                }

                MaybeProbeConnectivity();
                EvaluateLiveness();
            }
        }
        finally
        {
            try { _consumer?.Close(); }
            catch (Exception ex) when (!CriticalExceptions.IsCritical(ex))
            {
                _logger.LogWarning(ex, "KafkaLockCoordinator consumer-close failed");
            }
        }
    }

    /// <summary>
    /// Disposes the dead consumer and builds + resubscribes a fresh one, with
    /// bounded exponential backoff between attempts. On failure the rebuild
    /// flag stays set so the loop retries.
    /// </summary>
    private void RebuildConsumer(CancellationToken stoppingToken, ref int backoffMs)
    {
        var dead = _consumer;
        _consumer = null;
        if (dead is not null)
        {
            try { dead.Close(); }
            catch (Exception ex) when (!CriticalExceptions.IsCritical(ex)) { _logger.LogWarning(ex, "KafkaLockCoordinator close of dead consumer failed"); }
            try { dead.Dispose(); }
            catch (Exception ex) when (!CriticalExceptions.IsCritical(ex)) { _logger.LogWarning(ex, "KafkaLockCoordinator dispose of dead consumer failed"); }
        }

        if (!SleepUnlessStopping(backoffMs, stoppingToken)) return;
        backoffMs = Math.Min(backoffMs * 2, RebuildBackoffMaxMs);

        try
        {
            var fresh = CreateConsumer();
            fresh.Subscribe(_topics.Locks);
            _consumer = fresh;
            _rebuildRequested = false;
            _logger.LogInformation(
                "KafkaLockCoordinator consumer rebuilt and resubscribed after fatal error: topic={Topic}", _topics.Locks);
        }
        catch (Exception ex) when (!CriticalExceptions.IsCritical(ex))
        {
            _logger.LogError(ex, "KafkaLockCoordinator consumer rebuild failed; retrying with backoff");
        }
    }

    private static bool SleepUnlessStopping(int delayMs, CancellationToken stoppingToken)
    {
        try
        {
            Task.Delay(delayMs, stoppingToken).Wait(stoppingToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// Consumer error-handler hook. Fatal errors cancel every slot and request
    /// a consumer rebuild; transport-class errors mark connectivity as suspect
    /// so the liveness watchdog stops treating empty polls as broker contact.
    /// </summary>
    internal void OnConsumerError(Error error)
    {
        if (error.IsFatal)
        {
            OnFatalConsumerError(error);
            return;
        }

        if (error.Code is ErrorCode.Local_Transport or ErrorCode.Local_AllBrokersDown)
            _connectivitySuspect = true;
    }

    /// <summary>
    /// Fatal consumer error: every held lock reports lost immediately and the
    /// consume loop rebuilds the consumer instead of spinning on a dead handle.
    /// </summary>
    internal void OnFatalConsumerError(Error error)
    {
        _logger.LogError(
            "KafkaLockCoordinator fatal consumer error: code={Code} reason={Reason}. All lock slots cancelled; consumer will be rebuilt.",
            error.Code, error.Reason);
        CancelAllSlots("fatal-consumer-error");
        _rebuildRequested = true;
    }

    /// <summary>
    /// Effective connectivity-watchdog timeout:
    /// <see cref="KafkaLocksOptions.LivenessTimeoutMs"/> when configured,
    /// otherwise <c>max(session.timeout.ms × 0.5, 10s)</c> clamped below
    /// <c>session.timeout.ms - 2s</c> — this ensures the watchdog fires
    /// BEFORE the broker reassigns our partitions to a peer, preventing a
    /// split-brain window. With the prior formula of
    /// <c>max(session.timeout.ms, 30s)</c>, the watchdog fired at the same
    /// instant the broker could reassign (or later, for short timeouts).
    /// </summary>
    internal TimeSpan ResolveLivenessTimeout()
    {
        if (_options.LivenessTimeoutMs is { } configuredMs)
            return TimeSpan.FromMilliseconds(configuredMs);

        // Must mirror BuildConsumer's effective session timeout: the lock-
        // specific override wins over the shared client value.
        var sessionTimeoutMs = _options.SessionTimeoutMs
            ?? _connectionProvider
                .GetConsumerConfig(_options.ConsumerGroupId)
                .SessionTimeoutMs
            ?? LibrdkafkaDefaultSessionTimeoutMs;

        // Target: 50% of session timeout, at least LivenessFloorMs, and
        // strictly less than session timeout (by at least 2s safety margin).
        var halfSession = sessionTimeoutMs / 2;
        var floored = Math.Max(halfSession, LivenessFloorMs);
        var clamped = Math.Min(floored, sessionTimeoutMs - 2_000);
        // Absolute safety: never below 1s regardless of configuration.
        return TimeSpan.FromMilliseconds(Math.Max(clamped, 1_000));
    }

    private TimeSpan LivenessTimeout => _livenessTimeout ??= ResolveLivenessTimeout();

    /// <summary>Records a successful broker contact and re-arms the watchdog.</summary>
    internal void RecordBrokerContact()
    {
        Interlocked.Exchange(ref _lastBrokerContactTimestamp, _timeProvider.GetTimestamp());
        _livenessTripped = false;
    }

    /// <summary>
    /// Active connectivity probe, run from the consume loop while
    /// <c>_connectivitySuspect</c> is set (at most once per
    /// <see cref="ProbeIntervalMs"/>). The locks topic is idle by design, so
    /// after a transport-class error there is no passive signal that could
    /// ever clear the suspicion — records never arrive, and librdkafka handles
    /// heartbeats internally without surfacing them. Without this probe a
    /// single benign disconnect (broker idle-connection reap, one broker of a
    /// healthy cluster restarting) pinned the suspicion until the watchdog
    /// cancelled every held lock.
    ///
    /// <c>Committed</c> is a group-coordinator round-trip: success proves the
    /// exact connection that our session/partition-assignment lifecycle
    /// depends on is healthy, so suspicion is cleared and contact recorded.
    /// Failure (any exception) leaves the suspicion in place — a genuine
    /// partition still trips the watchdog before the broker can reassign our
    /// partitions to a peer. The call blocks the consume thread for at most
    /// <see cref="ProbeTimeout"/>, well inside max.poll.interval.
    /// </summary>
    internal void MaybeProbeConnectivity()
    {
        if (!_connectivitySuspect) return;
        var lastProbe = Interlocked.Read(ref _lastProbeTimestamp);
        if (lastProbe != 0 && _timeProvider.GetElapsedTime(lastProbe) < TimeSpan.FromMilliseconds(ProbeIntervalMs))
            return;
        Interlocked.Exchange(ref _lastProbeTimestamp, _timeProvider.GetTimestamp());

        try
        {
            _consumer?.Committed(new[] { new TopicPartition(_topics.Locks, 0) }, ProbeTimeout);
            _connectivitySuspect = false;
            RecordBrokerContact();
            _logger.LogInformation(
                "KafkaLockCoordinator connectivity probe succeeded after transport-class error; suspicion cleared.");
        }
        catch (Exception ex) when (!CriticalExceptions.IsCritical(ex))
        {
            _logger.LogWarning(
                "KafkaLockCoordinator connectivity probe failed ({Message}); broker contact still unconfirmed.",
                ex.Message);
        }
    }

    /// <summary>
    /// Connectivity watchdog, evaluated every consume-loop iteration. If no
    /// broker contact has been recorded within the liveness timeout, every
    /// slot is cancelled (locks report lost) exactly once per outage; the
    /// consumer is also rebuilt to force a fresh group-join and a new
    /// <c>OnAssigned</c> callback — required when the network recovers before
    /// <c>session.timeout.ms</c> elapses (no broker-side eviction, no
    /// automatic rebalance) so that <c>_connectivitySuspect</c> is cleared
    /// and slot re-establishment is guaranteed. Returns true when the watchdog
    /// trips on this call.
    /// </summary>
    internal bool EvaluateLiveness()
    {
        if (_livenessTripped) return false;

        var last = Interlocked.Read(ref _lastBrokerContactTimestamp);
        if (_timeProvider.GetElapsedTime(last) < LivenessTimeout) return false;

        _livenessTripped = true;
        _logger.LogError(
            "KafkaLockCoordinator has had no broker contact for over {LivenessTimeout}; " +
            "the broker may have reassigned our partitions to a peer (split-brain guard). " +
            "Cancelling all lock slots and rebuilding the consumer to guarantee re-assignment.",
            LivenessTimeout);
        CancelAllSlots("liveness-timeout");
        // Force consumer rebuild so the group re-joins and OnAssigned fires.
        // Without this, a transient disconnect that recovers before session.timeout.ms
        // leaves _connectivitySuspect=true with no rebalance to clear it — new slots
        // would wait for OnAssigned indefinitely and the coordinator would be permanently
        // wedged until process restart.
        //
        // Trade-off (audit CR#5): on a flapping broker this can cause repeated
        // teardown/rebuild cycles. That churn is the deliberate lesser evil — a
        // sustained loss of broker contact means the broker may already have
        // reassigned our partitions to a peer, so cancelling our slots and
        // re-joining is the *correct* response, and the rebuild's bounded
        // exponential backoff (RebuildBackoff*Ms in RebuildConsumer) caps the
        // cost. Riding the outage out instead would risk the permanent wedge
        // above. _livenessTripped makes this one-shot per outage.
        _rebuildRequested = true;
        return true;
    }

    /// <summary>
    /// Compute the partition for a resource key using Kafka's <c>MurmurHash2</c>
    /// partitioner (the Java-client default; librdkafka's <c>murmur2_random</c>
    /// — see <see cref="MurmurHash2"/>) against the configured partition count.
    /// </summary>
    public int GetPartitionFor(string resourceKey)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(resourceKey);
        var hash = MurmurHash2.Hash(bytes);
        // Kafka applies `toPositive(hash) % partitions` where toPositive = hash & 0x7fffffff.
        return (int)((hash & 0x7FFFFFFFu) % (uint)_options.PartitionCount);
    }

    /// <summary>
    /// Await ownership of <paramref name="partition"/>. Returns the live
    /// LockLostToken for that ownership cycle. Unbounded by design; the caller
    /// caps wait duration via the linked <paramref name="cancellationToken"/>.
    /// Throws <see cref="OperationCanceledException"/> on cancellation — which
    /// also occurs when the awaited slot is revoked before assignment
    /// completes; the slot is atomically replaced before the waiter wakes, so
    /// callers re-enter by calling again (see KafkaDistributedLockService).
    /// </summary>
    public async Task<CancellationToken> WaitForPartitionOwnershipAsync(
        int partition, CancellationToken cancellationToken)
    {
        Task<CancellationToken> task;
        lock (_slotLock)
        {
            var slot = _slots.GetOrAdd(partition, _ => new PartitionSlot());
            task = slot.AcquiredTcs.Task;
        }
        return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private void OnAssigned(int partition)
    {
        // A rebalance assignment is a group-coordinator round-trip — proof of
        // broker contact; this is also what re-arms the watchdog on reconnect.
        _connectivitySuspect = false;
        RecordBrokerContact();

        lock (_slotLock)
        {
            var slot = _slots.GetOrAdd(partition, _ => new PartitionSlot());
            if (slot.AcquiredTcs.Task.IsCompleted)
            {
                // No-op rebalance: already owned. Leave the existing CTS intact
                // ("recycling is scoped to the
                // revoke→assign cycle, not fired on every assignment event").
                return;
            }
            slot.AcquiredTcs.TrySetResult(slot.Cts.Token);
            _logger.LogInformation("KafkaLockCoordinator owns partition={Partition}", partition);
        }
    }

    private void OnRevokedOrLost(int partition, bool lost)
    {
        // Cooperative revoke is coordinator-driven (broker contact); "lost" can
        // be a purely local decision (e.g. max.poll exceeded), so it is not
        // treated as contact. Clear _connectivitySuspect on cooperative revoke:
        // a coordinator-driven rebalance round-trip proves the broker is reachable.
        if (!lost)
        {
            _connectivitySuspect = false;
            RecordBrokerContact();
        }

        PartitionSlot? replaced = null;
        lock (_slotLock)
        {
            if (_slots.TryGetValue(partition, out var slot))
            {
                // Atomic swap: publish a fresh slot for the next ownership cycle
                // inside the critical section, then signal the old cycle OUTSIDE
                // it. Any concurrent WaitForPartitionOwnershipAsync caller that
                // resolves to this partition sees exactly one of:
                // - the old slot (whose CTS/TCS are about to be cancelled — they
                //   observe OperationCanceledException and re-enter), or
                // - the new slot with a live uncancelled CTS.
                replaced = slot;
                _slots[partition] = new PartitionSlot();
                _logger.LogInformation(
                    "KafkaLockCoordinator released partition={Partition} reason={Reason}",
                    partition, lost ? "lost" : "revoked");
            }
        }

        if (replaced is not null)
        {
            // Synchronously cancel the lock-lost CTS BEFORE returning from the
            // rebalance callback. This is the critical split-brain guard: once
            // cancelled, any peer that receives OnAssigned for this partition
            // will see the old holder's LockLostToken already fired. Without
            // this synchronous cancel, the Task.Run below races the peer's
            // acquisition — the peer can observe "still valid" while the old
            // holder hasn't yet been notified of the loss.
            //
            // CTS.Cancel() runs all registered token callbacks synchronously on
            // the calling (rebalance) thread before returning. IMPORTANT: any code
            // that calls LockLostToken.Register(...) MUST register only fast,
            // non-blocking callbacks (flag flips, linked-CTS flips, etc.). If a
            // callback blocks or does I/O, it will directly block the rebalance
            // thread, potentially exceeding max.poll.interval.ms and triggering
            // a broker-side consumer eviction. In this codebase the only
            // production registrations are linked CancellationTokenSource objects
            // created via CreateLinkedTokenSource — these are safe (in-memory
            // flag flip). Document this contract clearly if adding new registrations.
            try { replaced.Cts.Cancel(); }
            catch (ObjectDisposedException) { /* torn down concurrently */ }
            catch (AggregateException ex)
            {
                _logger.LogWarning(ex, "KafkaLockCoordinator lock-lost callback threw during synchronous cancel");
            }

            // Wake orphaned TCS waiters off-thread (no callbacks to invoke
            // inline on the rebalance thread; max.poll risk + deadlock avoided).
            _ = Task.Run(() => replaced.AcquiredTcs.TrySetCanceled());
        }
    }

    /// <summary>
    /// Cancels every current slot (locks report lost) and atomically publishes
    /// fresh slots so waiters/holders re-establish on the next assignment.
    /// Used by the connectivity watchdog and the fatal-error path. Returns the
    /// teardown task (callbacks + dispose run on the thread pool).
    /// </summary>
    internal Task CancelAllSlots(string reason)
    {
        var replaced = new List<PartitionSlot>();
        lock (_slotLock)
        {
            foreach (var partition in _slots.Keys.ToList())
            {
                replaced.Add(_slots[partition]);
                _slots[partition] = new PartitionSlot();
            }
        }

        if (replaced.Count == 0) return Task.CompletedTask;

        _logger.LogError(
            "KafkaLockCoordinator cancelling {SlotCount} lock slot(s): reason={Reason}",
            replaced.Count, reason);
        return TearDownSlotsAsync(replaced);
    }

    /// <summary>
    /// Cancels replaced slots on the thread pool, so lock-holder callbacks
    /// never run under <see cref="_slotLock"/> or on the consume/rebalance
    /// thread. The CTSs are intentionally left undisposed — see the comment
    /// in the loop body.
    /// </summary>
    private Task TearDownSlotsAsync(IReadOnlyList<PartitionSlot> slots)
    {
        return Task.Run(() =>
        {
            foreach (var slot in slots)
            {
                try { slot.Cts.Cancel(); }
                catch (ObjectDisposedException) { /* torn down concurrently */ }
                catch (AggregateException ex)
                {
                    _logger.LogWarning(ex, "KafkaLockCoordinator lock-lost callback threw");
                }
                // Wake any waiter still blocked on the old slot's TCS (orphan-waiter
                // edge: a caller who captured AcquiredTcs.Task while ownership was
                // pending and then saw a revoke-without-assign). They observe
                // OperationCanceledException from WaitAsync and re-enter via a new
                // call against the fresh slot already published.
                slot.AcquiredTcs.TrySetCanceled();
                // Deliberately NOT disposed: LockLostToken escapes to lock
                // holders, and a disposed CTS throws ObjectDisposedException
                // from token.WaitHandle / token.Register on the holder's side
                // (holders legitimately block on the wait handle to observe
                // loss). A cancelled CTS with no timer holds only managed
                // memory, so letting the GC collect it is safe; disposing a
                // CTS whose token has escaped is not.
            }
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try { _loopCts?.Cancel(); }
        catch (ObjectDisposedException) { /* CTS already disposed */ }
        catch (OperationCanceledException) { /* cancellation already observed */ }
        if (_loopTask is not null)
        {
            try { await _loopTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { /* expected during shutdown */ }
            catch (ObjectDisposedException) { /* loop torn down concurrently */ }
        }

        List<PartitionSlot> remaining;
        lock (_slotLock)
        {
            remaining = _slots.Values.ToList();
            _slots.Clear();
        }
        // Awaited so StopAsync semantics hold: locks have reported lost before
        // shutdown proceeds. Callbacks still run off the caller's stack.
        await TearDownSlotsAsync(remaining).ConfigureAwait(false);

        try { _consumer?.Dispose(); }
        catch (KafkaException) { /* best-effort: consumer torn down */ }
        catch (ObjectDisposedException) { /* already disposed */ }
        try { _loopCts?.Dispose(); }
        catch (ObjectDisposedException) { /* already disposed */ }
    }

    internal sealed class PartitionSlot
    {
        public CancellationTokenSource Cts { get; } = new();
        public TaskCompletionSource<CancellationToken> AcquiredTcs { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
