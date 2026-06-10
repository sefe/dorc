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
/// </summary>
public sealed class KafkaDistributedLockService : IDistributedLockService
{
    private readonly KafkaLockCoordinator _coordinator;
    private readonly KafkaLocksOptions _options;
    private readonly ILogger<KafkaDistributedLockService> _logger;

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

                return new KafkaDistributedLock(resourceKey, partition, lockLostToken);
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
                // revoked while we awaited it. Re-enter against the fresh slot.
                _logger.LogDebug(
                    "kafka-lock wait interrupted outcome=revoked; re-entering wait resourceKey={ResourceKey} partition={Partition}",
                    resourceKey, partition);
            }
        }
    }
}
