using Dorc.Core.HighAvailability;

namespace Dorc.Monitor.IntegrationTests.Init
{
    /// <summary>
    /// Test-only no-op distributed lock service. A production
    /// <c>NoOpDistributedLockService</c> exists in Dorc.Core for the
    /// Kafka:Enabled=false fallback mode, but it is wired through the host
    /// container; these integration tests need a local stand-in that doesn't
    /// require a live Kafka broker or the Monitor's DI graph.
    /// </summary>
    internal sealed class IntegrationTestNoOpDistributedLockService : IDistributedLockService
    {
        public bool IsEnabled => false;
        public Task<IDistributedLock?> TryAcquireLockAsync(string resourceKey, int leaseTimeMs, CancellationToken cancellationToken)
            => Task.FromResult<IDistributedLock?>(null);
    }
}
