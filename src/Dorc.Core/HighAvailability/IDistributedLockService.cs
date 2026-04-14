namespace Dorc.Core.HighAvailability
{
    /// <summary>
    /// Service for acquiring and managing distributed locks to ensure only one monitor instance
    /// processes a deployment request at a time across multiple machines.
    /// </summary>
    public interface IDistributedLockService
    {
        /// <summary>
        /// Attempts to acquire a distributed lock for the specified resource.
        /// </summary>
        /// <param name="resourceKey">Unique identifier for the resource to lock (e.g., "request:123" or "env:Production")</param>
        /// <param name="leaseTimeMs">
        /// Substrate-specific wait/lease budget in milliseconds. For the RabbitMQ substrate this is
        /// the per-message TTL (crash-recovery auto-release). For the Kafka substrate this is the
        /// upper bound on time spent waiting for partition ownership before the call returns null
        /// (see SPEC-S-005b R-2).
        /// </param>
        /// <param name="cancellationToken">Cancellation token to abort lock acquisition</param>
        /// <returns>A lock handle if successful, null if lock could not be acquired. The lock is held until disposed.</returns>
        Task<IDistributedLock?> TryAcquireLockAsync(string resourceKey, int leaseTimeMs, CancellationToken cancellationToken);

        /// <summary>
        /// Gets whether the distributed locking is enabled and available.
        /// </summary>
        bool IsEnabled { get; }
    }

    /// <summary>
    /// Represents a distributed lock that must be disposed to release the lock.
    /// </summary>
    public interface IDistributedLock : IAsyncDisposable, IDisposable
    {
        /// <summary>
        /// The resource key this lock is for.
        /// </summary>
        string ResourceKey { get; }

        /// <summary>
        /// Returns true if the underlying lock is still healthy. False indicates the lock
        /// may have been lost (RabbitMQ: channel closed; Kafka: partition revoked/lost).
        /// </summary>
        bool IsValid { get; }

        /// <summary>
        /// A cancellation token that is triggered when the lock is lost.
        /// This should be linked to the deployment cancellation token to ensure immediate
        /// termination of the deployment if the lock is compromised.
        /// </summary>
        CancellationToken LockLostToken { get; }
    }
}
