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
        /// <param name="leaseTimeMs">How long the lock should be held in milliseconds before auto-release</param>
        /// <param name="cancellationToken">Cancellation token to abort lock acquisition</param>
        /// <returns>A lock handle if successful, null if lock could not be acquired</returns>
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
        /// Renews the lock lease to prevent it from expiring.
        /// Should be called periodically during long-running operations.
        /// </summary>
        /// <param name="leaseTimeMs">Extended lease time in milliseconds</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if renewal was successful, false otherwise</returns>
        Task<bool> RenewLeaseAsync(int leaseTimeMs, CancellationToken cancellationToken);

        /// <summary>
        /// Gets whether this lock is still valid (not expired or released).
        /// </summary>
        bool IsValid { get; }
    }
}
