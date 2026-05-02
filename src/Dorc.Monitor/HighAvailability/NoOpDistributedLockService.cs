namespace Dorc.Monitor.HighAvailability
{
    /// <summary>
    /// No-op distributed lock service used when high availability is disabled.
    /// Always returns null locks, allowing normal single-instance operation.
    /// </summary>
    public class NoOpDistributedLockService : IDistributedLockService
    {
        public bool IsEnabled => false;

        public Task<IDistributedLock?> TryAcquireLockAsync(string resourceKey, int leaseTimeMs, CancellationToken cancellationToken)
        {
            // Always return null - no locking when HA is disabled
            return Task.FromResult<IDistributedLock?>(null);
        }
    }
}
