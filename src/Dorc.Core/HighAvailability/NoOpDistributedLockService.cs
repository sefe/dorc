namespace Dorc.Core.HighAvailability
{
    /// <summary>
    /// Fallback <see cref="IDistributedLockService"/> for the Kafka-disabled mode
    /// (master switch <c>Kafka:Enabled = false</c>). Reports <see cref="IsEnabled"/>
    /// as <c>false</c> so call sites short-circuit lock acquisition and the Monitor
    /// runs in single-replica DB-poll mode.
    /// </summary>
    public sealed class NoOpDistributedLockService : IDistributedLockService
    {
        public bool IsEnabled => false;

        public Task<IDistributedLock?> TryAcquireLockAsync(string resourceKey, int leaseTimeMs, CancellationToken cancellationToken)
            => Task.FromResult<IDistributedLock?>(null);
    }
}
