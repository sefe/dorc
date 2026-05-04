using Dorc.Core.HighAvailability;

namespace Dorc.Kafka.Lock;

/// <summary>
/// Handle returned from <see cref="KafkaDistributedLockService.TryAcquireLockAsync"/>.
/// <see cref="LockLostToken"/> is the coordinator's per-partition CTS token
/// captured at acquisition time; it fires on cooperative-rebalance revoke,
/// session-timeout partition loss, or coordinator shutdown (SPEC-S-005b R-3).
/// Dispose is a no-op beyond bookkeeping — there is no explicit release
/// message in the partition-ownership model.
/// </summary>
internal sealed class KafkaDistributedLock : IDistributedLock
{
    public KafkaDistributedLock(string resourceKey, int partition, CancellationToken lockLostToken)
    {
        ResourceKey = resourceKey;
        Partition = partition;
        LockLostToken = lockLostToken;
    }

    public string ResourceKey { get; }
    public int Partition { get; }
    public CancellationToken LockLostToken { get; }
    public bool IsValid => !LockLostToken.IsCancellationRequested;

    public void Dispose() { /* partition-ownership model: nothing to release per-handle */ }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
