using Dorc.Monitor.HighAvailability;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Dorc.Monitor.IntegrationTests.HighAvailability
{
    /// <summary>
    /// Integration tests for RabbitMQ distributed lock service.
    /// These tests require a running RabbitMQ instance with OAuth2 authentication configured.
    /// 
    /// To run these tests:
    /// 1. Set environment variables for RabbitMQ connection
    /// 2. Ensure RabbitMQ is running and accessible
    /// 3. Run with: dotnet test --filter "Category=Integration&Category=RabbitMQ"
    /// </summary>
    [TestClass]
    [TestCategory("Integration")]
    [TestCategory("RabbitMQ")]
    [Ignore("Requires live RabbitMQ server - enable for integration testing")]
    public class RabbitMqLockIntegrationTests
    {
        private ILogger<RabbitMqDistributedLockService> logger;
        private IMonitorConfiguration configuration;
        
        [TestInitialize]
        public void Setup()
        {
            logger = Substitute.For<ILogger<RabbitMqDistributedLockService>>();
            configuration = Substitute.For<IMonitorConfiguration>();
            
            // Configure with environment variables or test configuration
            configuration.HighAvailabilityEnabled.Returns(true);
            configuration.RabbitMqHostName.Returns(Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "localhost");
            configuration.RabbitMqPort.Returns(int.Parse(Environment.GetEnvironmentVariable("RABBITMQ_PORT") ?? "5672"));
            configuration.RabbitMqVirtualHost.Returns("/");
            configuration.RabbitMqOAuthClientId.Returns(Environment.GetEnvironmentVariable("RABBITMQ_OAUTH_CLIENT_ID") ?? "test-client");
            configuration.RabbitMqOAuthClientSecret.Returns(Environment.GetEnvironmentVariable("RABBITMQ_OAUTH_CLIENT_SECRET") ?? "test-secret");
            configuration.RabbitMqOAuthTokenEndpoint.Returns(Environment.GetEnvironmentVariable("RABBITMQ_OAUTH_TOKEN_ENDPOINT") ?? "http://localhost:8080/oauth/token");
            configuration.RabbitMqOAuthScope.Returns("");
            configuration.Environment.Returns("integration-test");
        }

        /// <summary>
        /// Test: Only one monitor can acquire a lock at a time
        /// Verifies the race condition fix where multiple monitors publishing their own lock messages
        /// </summary>
        [TestMethod]
        public async Task TwoMonitors_OnlyOneCanAcquireLock()
        {
            // Arrange
            var service1 = new RabbitMqDistributedLockService(logger, configuration);
            var service2 = new RabbitMqDistributedLockService(logger, configuration);
            var resourceKey = $"env:TestEnv-{Guid.NewGuid()}";

            // Act - Monitor 1 acquires lock
            var lock1 = await service1.TryAcquireLockAsync(resourceKey, 30000, CancellationToken.None);
            
            // Act - Monitor 2 tries to acquire same lock
            var lock2 = await service2.TryAcquireLockAsync(resourceKey, 30000, CancellationToken.None);

            // Assert
            Assert.IsNotNull(lock1, "Monitor 1 should acquire the lock");
            Assert.IsNull(lock2, "Monitor 2 should NOT acquire the lock (already held by Monitor 1)");
            
            // Cleanup
            if (lock1 != null) await lock1.DisposeAsync();
            service1.Dispose();
            service2.Dispose();
        }

        /// <summary>
        /// Test: Lock can be acquired after previous lock is released
        /// Verifies that lock release properly frees the resource
        /// </summary>
        [TestMethod]
        public async Task LockRelease_AllowsSubsequentAcquisition()
        {
            // Arrange
            var service1 = new RabbitMqDistributedLockService(logger, configuration);
            var service2 = new RabbitMqDistributedLockService(logger, configuration);
            var resourceKey = $"env:TestEnv-{Guid.NewGuid()}";

            // Act - Monitor 1 acquires and releases lock
            var lock1 = await service1.TryAcquireLockAsync(resourceKey, 30000, CancellationToken.None);
            Assert.IsNotNull(lock1, "Monitor 1 should acquire the lock");
            
            await lock1.DisposeAsync(); // Release the lock
            await Task.Delay(1000); // Wait for cleanup to complete

            // Act - Monitor 2 can now acquire the lock
            var lock2 = await service2.TryAcquireLockAsync(resourceKey, 30000, CancellationToken.None);

            // Assert
            Assert.IsNotNull(lock2, "Monitor 2 should acquire the lock after Monitor 1 released it");
            
            // Cleanup
            if (lock2 != null) await lock2.DisposeAsync();
            service1.Dispose();
            service2.Dispose();
        }

        /// <summary>
        /// Test: Queue cleanup removes the lock queue after disposal
        /// Verifies that queues don't accumulate in RabbitMQ
        /// </summary>
        [TestMethod]
        public async Task LockDisposal_DeletesQueue()
        {
            // Note: This test requires RabbitMQ Management API access to verify queue deletion
            // For now, it verifies that disposal completes without errors
            
            // Arrange
            var service = new RabbitMqDistributedLockService(logger, configuration);
            var resourceKey = $"env:TestEnv-{Guid.NewGuid()}";

            // Act - Acquire lock
            var lockObj = await service.TryAcquireLockAsync(resourceKey, 30000, CancellationToken.None);
            Assert.IsNotNull(lockObj, "Should acquire the lock");

            // Act - Release lock (should delete queue)
            await lockObj.DisposeAsync();
            await Task.Delay(500); // Allow time for async deletion

            // Try to acquire lock again - queue should be recreated fresh
            var lockObj2 = await service.TryAcquireLockAsync(resourceKey, 30000, CancellationToken.None);
            Assert.IsNotNull(lockObj2, "Should acquire the lock again with fresh queue");

            // Cleanup
            if (lockObj2 != null) await lockObj2.DisposeAsync();
            service.Dispose();
        }

        /// <summary>
        /// Test: Multiple concurrent lock attempts - only first succeeds
        /// Simulates the real-world scenario with multiple monitors
        /// </summary>
        [TestMethod]
        public async Task ConcurrentLockAttempts_OnlyOneSucceeds()
        {
            // Arrange
            var services = Enumerable.Range(0, 5)
                .Select(_ => new RabbitMqDistributedLockService(logger, configuration))
                .ToList();
            var resourceKey = $"env:TestEnv-{Guid.NewGuid()}";

            // Act - 5 monitors try to acquire the same lock simultaneously
            var lockTasks = services.Select(service => 
                service.TryAcquireLockAsync(resourceKey, 30000, CancellationToken.None)
            ).ToList();

            var locks = await Task.WhenAll(lockTasks);

            // Assert
            var acquiredLocks = locks.Where(l => l != null).ToList();
            Assert.AreEqual(1, acquiredLocks.Count, "Only ONE monitor should acquire the lock");
            
            var failedCount = locks.Count(l => l == null);
            Assert.AreEqual(4, failedCount, "Four monitors should fail to acquire the lock");

            // Cleanup
            foreach (var lockObj in acquiredLocks)
            {
                if (lockObj != null) await lockObj.DisposeAsync();
            }
            services.ForEach(s => s.Dispose());
        }

        /// <summary>
        /// Test: Lock timeout prevents indefinite blocking
        /// Verifies that lock acquisition doesn't hang forever
        /// </summary>
        [TestMethod]
        public async Task LockAcquisition_WithTimeout_DoesNotHangIndefinitely()
        {
            // Arrange
            var service = new RabbitMqDistributedLockService(logger, configuration);
            var resourceKey = $"env:TestEnv-{Guid.NewGuid()}";

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            // Act
            var startTime = DateTime.UtcNow;
            var lockObj = await service.TryAcquireLockAsync(resourceKey, 30000, cts.Token);
            var elapsed = DateTime.UtcNow - startTime;

            // Assert
            Assert.IsTrue(elapsed < TimeSpan.FromSeconds(15), 
                "Lock acquisition should complete within reasonable time (not hang)");
            
            // Cleanup
            if (lockObj != null) await lockObj.DisposeAsync();
            service.Dispose();
        }

        /// <summary>
        /// Test: Queue message count check prevents race condition
        /// Directly tests the fix for the split deployment issue
        /// </summary>
        [TestMethod]
        public async Task QueueWithExistingMessage_PreventsDuplicateLockAcquisition()
        {
            // This test specifically validates the race condition fix shown in the screenshot
            // where deployment requests were split across "dorc ut 01" and "live dorc"
            
            // Arrange
            var service1 = new RabbitMqDistributedLockService(logger, configuration);
            var service2 = new RabbitMqDistributedLockService(logger, configuration);
            var resourceKey = $"env:Endur-QA-27-{Guid.NewGuid()}";

            // Act - Monitor 1 acquires lock (publishes message)
            var lock1 = await service1.TryAcquireLockAsync(resourceKey, 30000, CancellationToken.None);
            Assert.IsNotNull(lock1, "Monitor 1 should acquire lock");

            // Give time for message to be fully consumed
            await Task.Delay(100);

            // Act - Monitor 2 checks queue (should see messageCount > 0)
            var lock2 = await service2.TryAcquireLockAsync(resourceKey, 30000, CancellationToken.None);

            // Assert
            Assert.IsNull(lock2, 
                "Monitor 2 should NOT acquire lock - QueueDeclarePassive should show MessageCount > 0");

            // This prevents the scenario where:
            // - Both monitors publish their own lock messages
            // - Both monitors consume their own messages
            // - Both monitors think they hold the lock
            // - Deployment requests get split across monitors

            // Cleanup
            if (lock1 != null) await lock1.DisposeAsync();
            service1.Dispose();
            service2.Dispose();
        }
    }
}
