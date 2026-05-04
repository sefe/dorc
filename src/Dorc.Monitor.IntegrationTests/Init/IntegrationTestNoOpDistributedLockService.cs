using Dorc.Core.HighAvailability;

namespace Dorc.Monitor.IntegrationTests.Init
{
    /// <summary>
    /// Test-only no-op distributed lock service. The production
    /// <c>NoOpDistributedLockService</c> was removed in S-009 alongside the
    /// substrate-selector flag (Kafka is now the only production substrate).
    /// Integration tests still need a stand-in that doesn't require a live
    /// Kafka broker.
    /// </summary>
    internal sealed class IntegrationTestNoOpDistributedLockService : IDistributedLockService
    {
        public bool IsEnabled => false;
        public Task<IDistributedLock?> TryAcquireLockAsync(string resourceKey, int leaseTimeMs, CancellationToken cancellationToken)
            => Task.FromResult<IDistributedLock?>(null);
    }
}
