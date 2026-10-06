using Dorc.Core.HighAvailability;

namespace Dorc.Kafka.Lock;

/// <summary>
/// Handle returned from <see cref="KafkaDistributedLockService.TryAcquireLockAsync"/>.
/// <see cref="LockLostToken"/> is the coordinator's per-partition CTS token
/// captured at acquisition time; it fires on cooperative-rebalance revoke,
/// session-timeout partition loss, or coordinator shutdown.
///
/// <para>Dispose runs the supplied release callback, which releases the
/// intra-process semaphore gate (and drops the gate's reference count so the
/// owning service can reclaim it — see KafkaDistributedLockService) so the next
/// caller for the same resource key can proceed. Note that Kafka partition
/// ownership (cluster-level mutual exclusion) is retained until a rebalance —
/// the gate controls within-process exclusivity only.</para>
/// </summary>
internal sealed class KafkaDistributedLock : IDistributedLock
{
    private readonly Action? _onRelease;
    private int _disposed;

    public KafkaDistributedLock(string resourceKey, int partition, CancellationToken lockLostToken,
        Action? onRelease = null)
    {
        ResourceKey = resourceKey;
        Partition = partition;
        LockLostToken = lockLostToken;
        _onRelease = onRelease;
    }

    public string ResourceKey { get; }
    public int Partition { get; }
    public CancellationToken LockLostToken { get; }
    public bool IsValid => !LockLostToken.IsCancellationRequested;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _onRelease?.Invoke();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
