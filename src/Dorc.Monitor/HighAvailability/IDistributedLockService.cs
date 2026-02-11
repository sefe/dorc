namespace Dorc.Monitor.HighAvailability
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
        /// Reserved for future use. In the current RabbitMQ implementation, the lock is held until
        /// the returned <see cref="IDistributedLock"/> is disposed, regardless of this value.
        /// The parameter is retained for potential future implementations that may support
        /// auto-expiring locks (e.g., Redis-based distributed locks with TTL).
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
    }
}
