using Dorc.Core.HighAvailability;
using Dorc.Kafka.Lock.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Lock;

/// <summary>
/// Kafka consumer-group-based implementation of <see cref="IDistributedLockService"/>
/// per SPEC-S-005b R-2 / ADR-S-005.
///
/// Resource-key → partition via <see cref="MurmurHash2"/>; ownership acquired
/// by awaiting the coordinator's partition assignment. <paramref name="leaseTimeMs"/>
/// is reinterpreted as a wait-cap (ms): the call blocks up to that long waiting
/// for ownership, then returns null on timeout (ADR-deviation documented in
/// SPEC-S-005b R-2). Caller cancellation also yields null.
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

    public async Task<IDistributedLock?> TryAcquireLockAsync(
        string resourceKey, int leaseTimeMs, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(resourceKey))
            throw new ArgumentException("resourceKey is required", nameof(resourceKey));
        if (!_options.Enabled) return null;

        var partition = _coordinator.GetPartitionFor(resourceKey);
        var waitCapMs = leaseTimeMs > 0 ? leaseTimeMs : _options.LockWaitDefaultTimeoutMs;

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(waitCapMs));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            var lockLostToken = await _coordinator
                .WaitForPartitionOwnershipAsync(partition, linked.Token)
                .ConfigureAwait(false);

            if (lockLostToken.IsCancellationRequested)
            {
                // Rare race: ownership was granted but revoked before the continuation
                // ran. Treat as contention (null return) rather than hand the caller a
                // dead handle + a misleading "acquired" log.
                _logger.LogInformation(
                    "kafka-lock not acquired resourceKey={ResourceKey} partition={Partition} outcome=revoked-before-observe",
                    resourceKey, partition);
                return null;
            }

            _logger.LogInformation(
                "kafka-lock acquired resourceKey={ResourceKey} partition={Partition}",
                resourceKey, partition);

            return new KafkaDistributedLock(resourceKey, partition, lockLostToken);
        }
        catch (OperationCanceledException)
        {
            var outcome = cancellationToken.IsCancellationRequested ? "caller-cancelled" : "wait-timeout";
            _logger.LogInformation(
                "kafka-lock not acquired resourceKey={ResourceKey} partition={Partition} outcome={Outcome} waitCapMs={WaitCapMs}",
                resourceKey, partition, outcome, waitCapMs);
            return null;
        }
    }
}
