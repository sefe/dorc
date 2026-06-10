using System.Collections.Concurrent;
using Confluent.Kafka;
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
    private const int LivenessFloorMs = 30_000;
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
    // reports a transport-class error so that empty poll returns (which
    // librdkafka also produces while disconnected) stop counting as contact.
    private long _lastBrokerContactTimestamp;
    private volatile bool _livenessTripped;
    private volatile bool _connectivitySuspect;
    private TimeSpan? _livenessTimeout;

    // Set on fatal consumer errors; the consume loop disposes and rebuilds the
    // consumer (with bounded backoff) instead of spinning on a dead handle.
    private volatile bool _rebuildRequested;

    public KafkaLockCoordinator(
        IKafkaConnectionProvider connectionProvider,
        IOptions<KafkaLocksOptions> options,
        IOptions<KafkaTopicsOptions> topics,
        ILogger<KafkaLockCoordinator> logger,
        TimeProvider? timeProvider = null)
    {
        _connectionProvider = connectionProvider;
        _options = options.Value;
        _topics = topics.Value;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _lastBrokerContactTimestamp = _timeProvider.GetTimestamp();
    }

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
                        // A delivered record/event is proof of broker contact.
                        _connectivitySuspect = false;
                        RecordBrokerContact();
                    }
                    else if (!_connectivitySuspect)
                    {
                        // An empty poll return counts as contact unless the error
                        // handler has reported a transport-class failure —
                        // librdkafka returns empty polls while disconnected too.
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

                    _logger.LogWarning(ex, "KafkaLockCoordinator consume warning: {Reason}", ex.Error.Reason);
                    // Back off briefly so a dead broker doesn't busy-spin.
                    if (!SleepUnlessStopping(1_000, stoppingToken)) break;
                }
                catch (Exception ex) when (!IsCritical(ex))
                {
                    // Safety net for the long-running consume loop: log and
                    // continue rather than tear the host down on a transient
                    // unknown failure. Process-fatal exceptions still escape
                    // via IsCritical so the runtime can restart us cleanly.
                    _logger.LogError(ex, "KafkaLockCoordinator unexpected consume-loop error");
                    if (!SleepUnlessStopping(1_000, stoppingToken)) break;
                }

                EvaluateLiveness();
            }
        }
        finally
        {
            try { _consumer?.Close(); }
            catch (Exception ex) when (!IsCritical(ex))
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
            catch (Exception ex) when (!IsCritical(ex)) { _logger.LogWarning(ex, "KafkaLockCoordinator close of dead consumer failed"); }
            try { dead.Dispose(); }
            catch (Exception ex) when (!IsCritical(ex)) { _logger.LogWarning(ex, "KafkaLockCoordinator dispose of dead consumer failed"); }
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
        catch (Exception ex) when (!IsCritical(ex))
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

    private static bool IsCritical(Exception ex) =>
        ex is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException
            or System.Threading.ThreadAbortException;

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
    /// otherwise max(session.timeout.ms, 30s) — once the session times out the
    /// broker has reassigned our partitions, so any longer silence means a
    /// peer may already hold them.
    /// </summary>
    internal TimeSpan ResolveLivenessTimeout()
    {
        if (_options.LivenessTimeoutMs is { } configuredMs)
            return TimeSpan.FromMilliseconds(configuredMs);

        var sessionTimeoutMs = _connectionProvider
            .GetConsumerConfig(_options.ConsumerGroupId)
            .SessionTimeoutMs ?? LibrdkafkaDefaultSessionTimeoutMs;
        return TimeSpan.FromMilliseconds(Math.Max(sessionTimeoutMs, LivenessFloorMs));
    }

    private TimeSpan LivenessTimeout => _livenessTimeout ??= ResolveLivenessTimeout();

    /// <summary>Records a successful broker contact and re-arms the watchdog.</summary>
    internal void RecordBrokerContact()
    {
        Interlocked.Exchange(ref _lastBrokerContactTimestamp, _timeProvider.GetTimestamp());
        _livenessTripped = false;
    }

    /// <summary>
    /// Connectivity watchdog, evaluated every consume-loop iteration. If no
    /// broker contact has been recorded within the liveness timeout, every
    /// slot is cancelled (locks report lost) exactly once per outage; the loop
    /// keeps running so slots re-establish when partitions are reassigned on
    /// reconnect. Returns true when the watchdog trips on this call.
    /// </summary>
    internal bool EvaluateLiveness()
    {
        if (_livenessTripped) return false;

        var last = Interlocked.Read(ref _lastBrokerContactTimestamp);
        if (_timeProvider.GetElapsedTime(last) < LivenessTimeout) return false;

        _livenessTripped = true;
        _logger.LogError(
            "KafkaLockCoordinator has had no broker contact for over {LivenessTimeout}; the broker may have reassigned our partitions to a peer (split-brain guard). Cancelling all lock slots; they re-establish on reconnect.",
            LivenessTimeout);
        CancelAllSlots("liveness-timeout");
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
        // treated as contact.
        if (!lost) RecordBrokerContact();

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
            // Fire LockLostToken off-thread: lock-holder callbacks must never run
            // inline on the rebalance/consume thread (max.poll eviction risk) or
            // under _slotLock (deadlock risk).
            _ = TearDownSlotsAsync(new[] { replaced });
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
    /// Cancels and then disposes replaced slots on the thread pool, so
    /// lock-holder callbacks never run under <see cref="_slotLock"/> or on the
    /// consume/rebalance thread, and the replaced CTSs no longer leak.
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
                // Dispose after cancel so the replaced CTS doesn't leak per cycle;
                // callbacks have run by this point (Cancel is synchronous here).
                try { slot.Cts.Dispose(); }
                catch (ObjectDisposedException) { /* already disposed */ }
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
