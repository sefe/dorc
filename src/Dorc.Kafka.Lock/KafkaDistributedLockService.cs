using Dorc.Core.HighAvailability;
using Dorc.Kafka.Lock.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Lock;

/// <summary>
/// Kafka consumer-group-based implementation of <see cref="IDistributedLockService"/>.
///
/// Resource-key → partition via <see cref="MurmurHash2"/>; ownership acquired
/// by awaiting the coordinator's partition assignment. If the awaited slot is
/// revoked mid-wait (rebalance), the wait re-enters against the fresh slot
/// until the overall wait cap (<see cref="KafkaLocksOptions.AcquireWaitMs"/>)
/// expires.
///
/// <para>Intra-process gating: Kafka partition ownership is per-process, not
/// per-call. Two concurrent <see cref="TryAcquireLockAsync"/> calls for the
/// same resource key within the same process would both succeed. A per-key
/// <see cref="SemaphoreSlim"/> enforces exclusive access within the process,
/// preserving the same guarantee the RabbitMQ implementation provided.</para>
/// </summary>
public sealed class KafkaDistributedLockService : IDistributedLockService
{
    private readonly KafkaLockCoordinator _coordinator;
    private readonly KafkaLocksOptions _options;
    private readonly ILogger<KafkaDistributedLockService> _logger;

    // Per-resource-key semaphores for intra-process mutual exclusion.
    // Kafka partition ownership is at process granularity; without this gate
    // two concurrent TryAcquireLockAsync calls for the same key in the same
    // process would both receive the same partition-owned token and proceed
    // concurrently — defeating the lock abstraction.
    //
    // Reference-counted so the map stays bounded by concurrently-held keys
    // rather than every key ever seen (audit CR#7): a gate is created on first
    // interest and removed + disposed when the last holder/waiter releases it.
    // All map mutations and ref-count changes happen under _gatesLock, so an
    // increment can never race a removal.
    private readonly object _gatesLock = new();
    private readonly Dictionary<string, GateRef> _inProcessGates = new();

    private sealed class GateRef
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int RefCount;
    }

    /// <summary>
    /// Registers interest in the gate for <paramref name="key"/> (creating it if
    /// absent) and returns it with its reference count incremented. Must be
    /// paired with exactly one <see cref="ReleaseGateRef"/>.
    /// </summary>
    private GateRef AcquireGateRef(string key)
    {
        lock (_gatesLock)
        {
            if (!_inProcessGates.TryGetValue(key, out var gate))
            {
                gate = new GateRef();
                _inProcessGates[key] = gate;
            }
            gate.RefCount++;
            return gate;
        }
    }

    /// <summary>
    /// Drops one reference to the gate. Releases the semaphore first (when this
    /// caller held it) so the gate can never be removed/disposed while still
    /// held; removes and disposes the gate when the last reference goes away.
    /// </summary>
    private void ReleaseGateRef(string key, GateRef gate, bool releaseSemaphore)
    {
        if (releaseSemaphore)
            gate.Semaphore.Release();

        lock (_gatesLock)
        {
            if (--gate.RefCount == 0)
            {
                // Ref-count hits 0 only under this lock, and increments also take
                // it, so no caller is mid-WaitAsync on this semaphore here.
                _inProcessGates.Remove(key);
                gate.Semaphore.Dispose();
            }
        }
    }

    public KafkaDistributedLockService(
        KafkaLockCoordinator coordinator,
        IOptions<KafkaLocksOptions> options,
        ILogger<KafkaDistributedLockService> logger)
    {
        _coordinator = coordinator;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsEnabled => _options.Enabled;

    /// <summary>
    /// Attempts to acquire the lock for <paramref name="resourceKey"/> by
    /// awaiting ownership of its partition.
    ///
    /// <paramref name="leaseTimeMs"/> is IGNORED: partition ownership has no
    /// lease concept — the lock is held until the partition is revoked, lost,
    /// or the coordinator stops, and callers observe that via
    /// <see cref="IDistributedLock.LockLostToken"/>. The wait for ownership is
    /// capped by <see cref="KafkaLocksOptions.AcquireWaitMs"/> (deliberately a
    /// few seconds: callers poll, so a contested resource must fail fast
    /// rather than park a task for the lease duration). Returns null on
    /// wait-timeout or caller cancellation.
    /// </summary>
    public async Task<IDistributedLock?> TryAcquireLockAsync(
        string resourceKey, int leaseTimeMs, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(resourceKey))
            throw new ArgumentException("resourceKey is required", nameof(resourceKey));
        if (!_options.Enabled) return null;

        var partition = _coordinator.GetPartitionFor(resourceKey);
        var waitCapMs = _options.AcquireWaitMs;

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(waitCapMs));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        // Intra-process gate: acquire a per-key semaphore slot before waiting
        // for partition ownership. This ensures only one caller at a time holds
        // the lock within this process, mirroring the RabbitMQ semantics. The
        // gate is reference-counted (AcquireGateRef/ReleaseGateRef) so it is
        // reclaimed once nobody holds or waits on the key (audit CR#7).
        var gate = AcquireGateRef(resourceKey);
        bool gateAcquired = false;
        try
        {
            gateAcquired = await gate.Semaphore.WaitAsync(TimeSpan.FromMilliseconds(waitCapMs), linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation(
                "kafka-lock not acquired (intra-process gate) resourceKey={ResourceKey} outcome={Outcome}",
                resourceKey, cancellationToken.IsCancellationRequested ? "caller-cancelled" : "wait-timeout");
            ReleaseGateRef(resourceKey, gate, releaseSemaphore: false);
            return null;
        }

        if (!gateAcquired)
        {
            _logger.LogInformation(
                "kafka-lock not acquired (intra-process gate timeout) resourceKey={ResourceKey} waitCapMs={WaitCapMs}",
                resourceKey, waitCapMs);
            ReleaseGateRef(resourceKey, gate, releaseSemaphore: false);
            return null;
        }

        // Gate acquired. From here, the gate MUST be released if we return null
        // (failure path). On success it is handed to KafkaDistributedLock which
        // releases it on Dispose.
        try
        {
            // Re-entry loop: a wait can end because the slot we were awaiting was
            // revoked (rebalance churn) rather than because the caller gave up or
            // the cap expired. The coordinator atomically publishes a fresh slot
            // before waking us, so we simply wait again until the cap expires.
            while (true)
            {
                try
                {
                    var lockLostToken = await _coordinator
                        .WaitForPartitionOwnershipAsync(partition, linked.Token)
                        .ConfigureAwait(false);

                    if (lockLostToken.IsCancellationRequested)
                    {
                        // Ownership was granted but revoked before the continuation
                        // ran. Re-enter against the fresh slot rather than hand the
                        // caller a dead handle or give up early.
                        _logger.LogDebug(
                            "kafka-lock ownership revoked before observation; re-entering wait resourceKey={ResourceKey} partition={Partition}",
                            resourceKey, partition);
                        continue;
                    }

                    _logger.LogInformation(
                        "kafka-lock acquired resourceKey={ResourceKey} partition={Partition}",
                        resourceKey, partition);

                    // Transfer gate ownership to the lock handle — it releases the
                    // semaphore and drops the gate reference on Dispose.
                    gateAcquired = false;
                    return new KafkaDistributedLock(resourceKey, partition, lockLostToken,
                        onRelease: () => ReleaseGateRef(resourceKey, gate, releaseSemaphore: true));
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogInformation(
                            "kafka-lock not acquired resourceKey={ResourceKey} partition={Partition} outcome=caller-cancelled waitCapMs={WaitCapMs}",
                            resourceKey, partition, waitCapMs);
                        return null;
                    }

                    if (timeoutCts.IsCancellationRequested)
                    {
                        _logger.LogInformation(
                            "kafka-lock not acquired resourceKey={ResourceKey} partition={Partition} outcome=wait-timeout waitCapMs={WaitCapMs}",
                            resourceKey, partition, waitCapMs);
                        return null;
                    }

                    // Neither the caller nor the cap cancelled: the slot itself was
                    // revoked while we awaited it. Keep the gate held and re-enter
                    // against the fresh slot.
                    _logger.LogDebug(
                        "kafka-lock wait interrupted outcome=revoked; re-entering wait resourceKey={ResourceKey} partition={Partition}",
                        resourceKey, partition);
                }
            }
        }
        finally
        {
            // Release the gate if we are returning null (gateAcquired still true
            // means we didn't transfer ownership to a KafkaDistributedLock handle).
            if (gateAcquired)
                ReleaseGateRef(resourceKey, gate, releaseSemaphore: true);
        }
    }
}
