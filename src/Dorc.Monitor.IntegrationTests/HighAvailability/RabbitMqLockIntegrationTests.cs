using Dorc.Monitor.HighAvailability;
using Microsoft.Extensions.Logging;
using NSubstitute;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

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

        /// <summary>
        /// S-001 / Test 1: After acquiring a lock, the broker must have accepted the
        /// x-consumer-timeout=0 queue argument. Verified by attempting to re-declare
        /// the same queue with a conflicting argument value — the broker returns
        /// PRECONDITION_FAILED if the existing queue has different arguments.
        /// </summary>
        [TestMethod]
        public async Task LockQueue_BrokerAccepts_ConsumerTimeoutDisabledArgument()
        {
            // Arrange
            var service = new RabbitMqDistributedLockService(logger, configuration);
            var resourceKey = $"env:S001-Test1-{Guid.NewGuid()}";

            // Act — acquire lock (queue is declared with x-consumer-timeout=0)
            var lockObj = await service.TryAcquireLockAsync(resourceKey, 30000, CancellationToken.None);
            Assert.IsNotNull(lockObj, "Lock acquisition must succeed: broker must accept x-consumer-timeout=0 argument");

            // Verify lock is stable — LockLostToken must not be cancelled
            Assert.IsFalse(lockObj.LockLostToken.IsCancellationRequested,
                "LockLostToken must not be cancelled: lock is held with x-consumer-timeout=0 active");

            // Attempt to re-declare the same queue with a different x-consumer-timeout value via
            // a second channel on the same connection. RabbitMQ returns PRECONDITION_FAILED if the
            // declared arguments differ from the existing queue — confirming the original queue
            // was declared with x-consumer-timeout=0.
            var conn = service.connection;
            Assert.IsNotNull(conn, "Internal connection must be available after lock acquisition");

            IChannel? probeChannel = null;
            try
            {
                probeChannel = await conn.CreateChannelAsync(cancellationToken: CancellationToken.None);
                var conflictingArgs = new Dictionary<string, object?>
                {
                    { "x-queue-type", "quorum" },
                    { "x-single-active-consumer", true },
                    { "x-consumer-timeout", 1000L } // Different value — must conflict with the lock queue's 0L
                };
                await probeChannel.QueueDeclareAsync(
                    queue: $"lock.{resourceKey}",
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: conflictingArgs,
                    cancellationToken: CancellationToken.None);

                Assert.Fail("Expected OperationInterruptedException (PRECONDITION_FAILED 406): " +
                    "the existing queue should have been declared with x-consumer-timeout=0, " +
                    "so a re-declaration with x-consumer-timeout=1000 must fail");
            }
            catch (OperationInterruptedException ex)
                when (ex.ShutdownReason?.ReplyCode == 406)
            {
                // PRECONDITION_FAILED 406 — queue already exists with different arguments.
                // This confirms the lock queue was declared with x-consumer-timeout=0.
            }
            finally
            {
                if (probeChannel is not null)
                {
                    try { await probeChannel.CloseAsync(cancellationToken: CancellationToken.None); } catch { }
                    await probeChannel.DisposeAsync();
                }
                await lockObj.DisposeAsync();
                service.Dispose();
            }
        }

        /// <summary>
        /// S-001 / Test 2: Demonstrates that x-consumer-timeout=0 prevents broker channel closure.
        /// A control queue with a short non-zero timeout (1,000 ms) is closed by the broker when
        /// holding an unacknowledged message beyond the timeout. A treatment queue with
        /// x-consumer-timeout=0 (disabled) is NOT closed under the same conditions.
        /// </summary>
        [TestMethod]
        public async Task ConsumerTimeout_WhenSetToZero_PreventsBrokerChannelClosure()
        {
            // Acquire a lock first to establish an authenticated connection,
            // then borrow that connection for the control/treatment channels.
            var bootstrapService = new RabbitMqDistributedLockService(logger, configuration);
            var bootstrapKey = $"env:S001-Test2-Bootstrap-{Guid.NewGuid()}";
            var bootstrapLock = await bootstrapService.TryAcquireLockAsync(bootstrapKey, 30000, CancellationToken.None);
            Assert.IsNotNull(bootstrapLock, "Bootstrap lock must be acquired to obtain a connection");

            var conn = bootstrapService.connection;
            Assert.IsNotNull(conn, "Connection must be available after bootstrap lock acquisition");

            // --- Control: queue with x-consumer-timeout=1000 ms ---
            var controlClosedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var controlQueueName = $"s001-control-{Guid.NewGuid()}";
            var controlChannel = await conn.CreateChannelAsync(cancellationToken: CancellationToken.None);

            controlChannel.ChannelShutdownAsync += (_, _) =>
            {
                controlClosedTcs.TrySetResult(true);
                return Task.CompletedTask;
            };

            await controlChannel.QueueDeclareAsync(
                queue: controlQueueName,
                durable: false,
                exclusive: false,
                autoDelete: true,
                arguments: new Dictionary<string, object?> { { "x-consumer-timeout", 1000L } },
                cancellationToken: CancellationToken.None);

            await controlChannel.BasicPublishAsync(
                exchange: "",
                routingKey: controlQueueName,
                mandatory: false,
                basicProperties: new BasicProperties(),
                body: System.Text.Encoding.UTF8.GetBytes("lock"),
                cancellationToken: CancellationToken.None);

            var controlConsumer = new AsyncEventingBasicConsumer(controlChannel);
            await controlChannel.BasicConsumeAsync(
                queue: controlQueueName,
                autoAck: false,
                consumerTag: "",
                noLocal: false,
                exclusive: false,
                arguments: null,
                consumer: controlConsumer,
                cancellationToken: CancellationToken.None);

            // Wait up to 5 seconds for broker to close the control channel (timeout fires at 1s)
            var controlClosed = await Task.WhenAny(controlClosedTcs.Task, Task.Delay(5000)) == controlClosedTcs.Task;

            // --- Treatment: queue with x-consumer-timeout=0 (disabled) ---
            var treatmentClosedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var treatmentQueueName = $"s001-treatment-{Guid.NewGuid()}";
            var treatmentChannel = await conn.CreateChannelAsync(cancellationToken: CancellationToken.None);

            treatmentChannel.ChannelShutdownAsync += (_, _) =>
            {
                treatmentClosedTcs.TrySetResult(true);
                return Task.CompletedTask;
            };

            await treatmentChannel.QueueDeclareAsync(
                queue: treatmentQueueName,
                durable: false,
                exclusive: false,
                autoDelete: true,
                arguments: new Dictionary<string, object?> { { "x-consumer-timeout", 0L } },
                cancellationToken: CancellationToken.None);

            await treatmentChannel.BasicPublishAsync(
                exchange: "",
                routingKey: treatmentQueueName,
                mandatory: false,
                basicProperties: new BasicProperties(),
                body: System.Text.Encoding.UTF8.GetBytes("lock"),
                cancellationToken: CancellationToken.None);

            var treatmentConsumer = new AsyncEventingBasicConsumer(treatmentChannel);
            await treatmentChannel.BasicConsumeAsync(
                queue: treatmentQueueName,
                autoAck: false,
                consumerTag: "",
                noLocal: false,
                exclusive: false,
                arguments: null,
                consumer: treatmentConsumer,
                cancellationToken: CancellationToken.None);

            // Wait the same 5 seconds — treatment channel must remain open
            var treatmentClosed = await Task.WhenAny(treatmentClosedTcs.Task, Task.Delay(5000)) == treatmentClosedTcs.Task;

            // --- Assertions ---
            Assert.IsTrue(controlClosed,
                "Control channel (x-consumer-timeout=1000ms) must be closed by the broker " +
                "when holding an unacknowledged message for 5 seconds");
            Assert.IsFalse(treatmentClosed,
                "Treatment channel (x-consumer-timeout=0, disabled) must NOT be closed by the broker; " +
                "this is the S-001 fix verification");

            // Cleanup
            try { await treatmentChannel.CloseAsync(cancellationToken: CancellationToken.None); } catch { }
            await treatmentChannel.DisposeAsync();
            await bootstrapLock.DisposeAsync();
            bootstrapService.Dispose();
        }
    }
}
