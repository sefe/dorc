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
/// to the lock topic. Partition ownership = distributed lock ownership (ADR-S-005).
///
/// Cooperative-sticky rebalance provides per-partition revoke/assign events;
/// each partition has a <see cref="PartitionSlot"/> carrying a live
/// <see cref="CancellationTokenSource"/> (fired on revoke/lost → LockLostToken)
/// and a <see cref="TaskCompletionSource{T}"/> signalling ownership acquisition.
/// Slot replacement on revoke→reassign is atomic under a per-coordinator lock
/// so concurrent <see cref="WaitForPartitionOwnershipAsync"/> callers never
/// observe a stale (cancelled) token as "still live".
///
/// No user records are produced or consumed for lock semantics — the consume
/// loop exists only to keep the librdkafka heartbeat thread scheduled; any
/// record that happens to arrive is silently discarded.
/// </summary>
public sealed class KafkaLockCoordinator : IHostedService, IAsyncDisposable
{
    private readonly IKafkaConnectionProvider _connectionProvider;
    private readonly KafkaLocksOptions _options;
    private readonly KafkaTopicsOptions _topics;
    private readonly ILogger<KafkaLockCoordinator> _logger;

    private readonly object _slotLock = new();
    private readonly ConcurrentDictionary<int, PartitionSlot> _slots = new();

    private IConsumer<byte[], byte[]>? _consumer;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private volatile bool _disposed;

    public KafkaLockCoordinator(
        IKafkaConnectionProvider connectionProvider,
        IOptions<KafkaLocksOptions> options,
        IOptions<KafkaTopicsOptions> topics,
        ILogger<KafkaLockCoordinator> logger)
    {
        _connectionProvider = connectionProvider;
        _options = options.Value;
        _topics = topics.Value;
        _logger = logger;
    }

    public KafkaLocksOptions Options => _options;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("KafkaLockCoordinator disabled via options; lock service will return null on acquire.");
            return Task.CompletedTask;
        }

        _loopCts = new CancellationTokenSource();
        _consumer = BuildConsumer();
        _consumer.Subscribe(_topics.Locks);
        _logger.LogInformation(
            "KafkaLockCoordinator subscribed: topic={Topic} group={GroupId} partitions={PartitionCount}",
            _topics.Locks, _options.ConsumerGroupId, _options.PartitionCount);

        _loopTask = Task.Run(() => RunLoop(_loopCts.Token));
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await DisposeAsync().ConfigureAwait(false);
    }

    private IConsumer<byte[], byte[]> BuildConsumer()
    {
        var config = _connectionProvider.GetConsumerConfig(_options.ConsumerGroupId);
        // Lock semantics don't use committed offsets — auto-commit is harmless
        // (the topic has no meaningful records) and cheaper than manual commit.
        config.EnableAutoCommit = true;
        config.AutoOffsetReset = AutoOffsetReset.Latest;

        var handlers = new KafkaRebalanceHandlers<byte[], byte[]>(_logger, "dorc-lock-coordinator");

        return new ConsumerBuilder<byte[], byte[]>(config)
            .SetErrorHandler(handlers.OnError)
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
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Return of null ConsumeResult on timeout is fine; we don't act on records.
                    _consumer?.Consume(TimeSpan.FromMilliseconds(500));
                }
                catch (OperationCanceledException) { break; }
                catch (ConsumeException ex)
                {
                    _logger.LogWarning(ex, "KafkaLockCoordinator consume warning: {Reason}", ex.Error.Reason);
                    // Back off briefly so a dead broker doesn't busy-spin.
                    try { Task.Delay(1000, stoppingToken).Wait(stoppingToken); }
                    catch (OperationCanceledException) { break; }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "KafkaLockCoordinator unexpected consume-loop error");
                    try { Task.Delay(1000, stoppingToken).Wait(stoppingToken); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }
        finally
        {
            try { _consumer?.Close(); } catch { /* best-effort */ }
        }
    }

    /// <summary>
    /// Compute the partition for a resource key using Kafka's <c>MurmurHash2</c>
    /// partitioner (the default librdkafka/Java producer partitioner) against
    /// the configured partition count.
    /// </summary>
    public int GetPartitionFor(string resourceKey)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(resourceKey);
        var hash = MurmurHash2.Hash(bytes);
        // librdkafka applies `(hash & 0x7fffffff) % partitions`
        return (int)((hash & 0x7FFFFFFFu) % (uint)_options.PartitionCount);
    }

    /// <summary>
    /// Await ownership of <paramref name="partition"/>. Returns the live
    /// LockLostToken for that ownership cycle. Unbounded by design; the caller
    /// caps wait duration via the linked <paramref name="cancellationToken"/>.
    /// Throws <see cref="OperationCanceledException"/> on cancellation.
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
        lock (_slotLock)
        {
            var slot = _slots.GetOrAdd(partition, _ => new PartitionSlot());
            if (slot.AcquiredTcs.Task.IsCompleted)
            {
                // No-op rebalance: already owned. Leave the existing CTS intact
                // per ADR-S-005 and SPEC-S-005b R-1 ("recycling is scoped to the
                // revoke→assign cycle, not fired on every assignment event").
                return;
            }
            slot.AcquiredTcs.TrySetResult(slot.Cts.Token);
            _logger.LogInformation("KafkaLockCoordinator owns partition={Partition}", partition);
        }
    }

    private void OnRevokedOrLost(int partition, bool lost)
    {
        lock (_slotLock)
        {
            if (_slots.TryGetValue(partition, out var slot))
            {
                // Atomic swap: signal the old cycle's LockLostToken, then publish
                // a fresh slot for the next ownership cycle in the same critical
                // section. Any concurrent WaitForPartitionOwnershipAsync caller
                // that resolves to this partition sees exactly one of:
                //   - the old slot with a cancelled CTS (and they re-wait), or
                //   - the new slot with a live uncancelled CTS.
                slot.Cts.Cancel();
                // Wake any waiter still blocked on the old slot's TCS (orphan-waiter edge:
                // a caller who captured AcquiredTcs.Task while ownership was pending and
                // then saw a revoke-without-assign). They observe OperationCanceledException
                // from WaitAsync and re-enter via a new call against the fresh slot below.
                slot.AcquiredTcs.TrySetCanceled();
                _slots[partition] = new PartitionSlot();
                _logger.LogInformation(
                    "KafkaLockCoordinator released partition={Partition} reason={Reason}",
                    partition, lost ? "lost" : "revoked");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try { _loopCts?.Cancel(); } catch { /* best-effort */ }
        if (_loopTask is not null)
        {
            try { await _loopTask.ConfigureAwait(false); } catch { /* best-effort */ }
        }

        lock (_slotLock)
        {
            foreach (var kv in _slots)
            {
                try { kv.Value.Cts.Cancel(); } catch { /* best-effort */ }
                kv.Value.AcquiredTcs.TrySetCanceled();
            }
            _slots.Clear();
        }

        try { _consumer?.Dispose(); } catch { /* best-effort */ }
        try { _loopCts?.Dispose(); } catch { /* best-effort */ }
    }

    internal sealed class PartitionSlot
    {
        public CancellationTokenSource Cts { get; } = new();
        public TaskCompletionSource<CancellationToken> AcquiredTcs { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
