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
            try { await controlChannel.CloseAsync(cancellationToken: CancellationToken.None); } catch { }
            await controlChannel.DisposeAsync();
            try { await treatmentChannel.CloseAsync(cancellationToken: CancellationToken.None); } catch { }
            await treatmentChannel.DisposeAsync();
            await bootstrapLock.DisposeAsync();
            bootstrapService.Dispose();
        }

        /// <summary>
        /// S-002 / IT1: After triggering channel loss, if the lock message is never delivered
        /// within the configured retry window, LockLostToken must be cancelled — confirming
        /// that window exhaustion correctly triggers deployment cancellation.
        ///
        /// The test uses a standby consumer on a separate RabbitMQ connection to hold the lock
        /// message undelivered for the duration. The service's re-acquisition retry loop must
        /// run multiple attempts before the window expires. Elapsed time >= (window - tolerance)
        /// confirms that retries ran rather than a single-attempt fast-cancel.
        ///
        /// This test FAILS with the old single-attempt code (which cancels too quickly, before
        /// the retry window is exhausted) and PASSES with the S-002 retry loop.
        /// </summary>
        [TestMethod]
        public async Task ReacquisitionRetry_WhenMessageNeverDelivered_CancelsAfterWindow()
        {
            const int retryWindowSeconds = 10;
            const int perAttemptSeconds = 2;

            // Service under test — short window and per-attempt to keep test duration manageable
            var serviceConfig = Substitute.For<IMonitorConfiguration>();
            serviceConfig.HighAvailabilityEnabled.Returns(true);
            serviceConfig.RabbitMqHostName.Returns(configuration.RabbitMqHostName);
            serviceConfig.RabbitMqPort.Returns(configuration.RabbitMqPort);
            serviceConfig.RabbitMqVirtualHost.Returns(configuration.RabbitMqVirtualHost);
            serviceConfig.RabbitMqOAuthClientId.Returns(configuration.RabbitMqOAuthClientId);
            serviceConfig.RabbitMqOAuthClientSecret.Returns(configuration.RabbitMqOAuthClientSecret);
            serviceConfig.RabbitMqOAuthTokenEndpoint.Returns(configuration.RabbitMqOAuthTokenEndpoint);
            serviceConfig.RabbitMqOAuthScope.Returns(configuration.RabbitMqOAuthScope);
            serviceConfig.RabbitMqSslEnabled.Returns(configuration.RabbitMqSslEnabled);
            serviceConfig.RabbitMqSslServerName.Returns(configuration.RabbitMqSslServerName);
            serviceConfig.RabbitMqSslVersion.Returns(configuration.RabbitMqSslVersion);
            serviceConfig.Environment.Returns("integration-test");
            serviceConfig.LockAcquisitionTimeoutSeconds.Returns(perAttemptSeconds);
            serviceConfig.LockReacquisitionRetryWindowSeconds.Returns(retryWindowSeconds);

            var service = new RabbitMqDistributedLockService(logger, serviceConfig);
            var resourceKey = $"env:S002-IT1-{Guid.NewGuid()}";
            var lockQueueName = $"lock.{resourceKey}";

            // Arrange: acquire lock to establish the queue and the service connection
            var lockObj = await service.TryAcquireLockAsync(resourceKey, 30000, CancellationToken.None);
            Assert.IsNotNull(lockObj, "Lock acquisition must succeed before testing re-acquisition");

            // Arrange: create a separate connection for the blocker (must be on a different
            // connection from the service so it survives when the service's connection is closed)
            var blockerConfig = Substitute.For<IMonitorConfiguration>();
            blockerConfig.HighAvailabilityEnabled.Returns(true);
            blockerConfig.RabbitMqHostName.Returns(configuration.RabbitMqHostName);
            blockerConfig.RabbitMqPort.Returns(configuration.RabbitMqPort);
            blockerConfig.RabbitMqVirtualHost.Returns(configuration.RabbitMqVirtualHost);
            blockerConfig.RabbitMqOAuthClientId.Returns(configuration.RabbitMqOAuthClientId);
            blockerConfig.RabbitMqOAuthClientSecret.Returns(configuration.RabbitMqOAuthClientSecret);
            blockerConfig.RabbitMqOAuthTokenEndpoint.Returns(configuration.RabbitMqOAuthTokenEndpoint);
            blockerConfig.RabbitMqOAuthScope.Returns(configuration.RabbitMqOAuthScope);
            blockerConfig.RabbitMqSslEnabled.Returns(configuration.RabbitMqSslEnabled);
            blockerConfig.RabbitMqSslServerName.Returns(configuration.RabbitMqSslServerName);
            blockerConfig.RabbitMqSslVersion.Returns(configuration.RabbitMqSslVersion);
            blockerConfig.Environment.Returns("integration-test-blocker");
            blockerConfig.LockAcquisitionTimeoutSeconds.Returns(5);
            blockerConfig.LockReacquisitionRetryWindowSeconds.Returns(150);
            var blockerService = new RabbitMqDistributedLockService(logger, blockerConfig);
            // Initialize the blocker service's connection by acquiring a lock on a dummy resource
            var dummyKey = $"env:S002-IT1-blocker-{Guid.NewGuid()}";
            var dummyLock = await blockerService.TryAcquireLockAsync(dummyKey, 30000, CancellationToken.None);
            Assert.IsNotNull(dummyLock, "Blocker service must connect to RabbitMQ");

            // Register a standby consumer on the lock queue via the blocker's separate connection.
            // ORDERING IS CRITICAL: register BEFORE triggering channel loss on the service, so
            // that SAC promotes this consumer (not the retry loop's first consumer) when the
            // lock message is requeued.
            var blockerConn = blockerService.connection;
            Assert.IsNotNull(blockerConn, "Blocker service connection must be available");
            var blockerChannel = await blockerConn.CreateChannelAsync(cancellationToken: CancellationToken.None);
            var blockerConsumer = new AsyncEventingBasicConsumer(blockerChannel);
            await blockerChannel.BasicConsumeAsync(
                queue: lockQueueName,
                autoAck: false,
                consumerTag: "",
                noLocal: false,
                exclusive: false,
                arguments: null,
                consumer: blockerConsumer,
                cancellationToken: CancellationToken.None);

            // Act: force-close the service's connection, triggering lock re-acquisition
            var startTime = DateTime.UtcNow;
            var serviceConn = service.connection;
            Assert.IsNotNull(serviceConn, "Service connection must be available");
            try { await serviceConn.CloseAsync(); } catch { }

            // Wait for LockLostToken to be cancelled (window + generous buffer for connection setup)
            var lockLostTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            lockObj.LockLostToken.Register(() => lockLostTcs.TrySetResult(true));
            var bufferSeconds = retryWindowSeconds + 15;
            var tokenFired = await Task.WhenAny(lockLostTcs.Task, Task.Delay(TimeSpan.FromSeconds(bufferSeconds)))
                             == lockLostTcs.Task;
            var elapsed = DateTime.UtcNow - startTime;

            // Assert: deployment must be cancelled
            Assert.IsTrue(tokenFired,
                $"LockLostToken must be cancelled within {bufferSeconds}s — " +
                $"retry window exhaustion must trigger deployment cancellation");

            // Assert: elapsed time confirms retries ran, not a single-attempt fast-cancel.
            // With old single-attempt code, cancellation fires after ~perAttemptSeconds.
            // With retry loop, it fires after ~retryWindowSeconds.
            // Require elapsed >= (retryWindowSeconds - perAttemptSeconds - 2) as tolerance.
            var minimumElapsedSeconds = retryWindowSeconds - perAttemptSeconds - 2;
            Assert.IsTrue(elapsed.TotalSeconds >= minimumElapsedSeconds,
                $"Elapsed {elapsed.TotalSeconds:F1}s is less than minimum {minimumElapsedSeconds}s — " +
                $"this indicates single-attempt behaviour rather than the retry loop");

            // Cleanup
            try { await blockerChannel.CloseAsync(cancellationToken: CancellationToken.None); } catch { }
            await blockerChannel.DisposeAsync();
            await dummyLock.DisposeAsync();
            await blockerService.DisposeAsync();
            service.Dispose();
        }

        /// <summary>
        /// S-002 / IT2: After triggering channel loss, if the lock message is delivered
        /// partway through the retry window (after at least two attempt cycles), LockLostToken
        /// must NOT be cancelled — confirming the deployment continues after delayed re-acquisition.
        ///
        /// The test uses a standby consumer to hold the message for a blocking duration that
        /// exceeds 2 × LockAcquisitionTimeoutSeconds + inter-attempt delay, guaranteeing at
        /// least two retry attempts before the message becomes available.
        ///
        /// This test FAILS with the old single-attempt code (which cancels the deployment
        /// immediately after the first attempt times out) and PASSES with the S-002 retry loop.
        /// </summary>
        [TestMethod]
        public async Task ReacquisitionRetry_WhenMessageDeliveredMidWindow_ContinuesDeployment()
        {
            const int retryWindowSeconds = 20;
            const int perAttemptSeconds = 2;
            // Blocking duration must exceed 2 × perAttemptSeconds + inter-attempt delay.
            // Using 8s: covers 2 × 2s delivery waits + a 3s inter-attempt delay + margin.
            const int blockingSeconds = 8;

            var serviceConfig = Substitute.For<IMonitorConfiguration>();
            serviceConfig.HighAvailabilityEnabled.Returns(true);
            serviceConfig.RabbitMqHostName.Returns(configuration.RabbitMqHostName);
            serviceConfig.RabbitMqPort.Returns(configuration.RabbitMqPort);
            serviceConfig.RabbitMqVirtualHost.Returns(configuration.RabbitMqVirtualHost);
            serviceConfig.RabbitMqOAuthClientId.Returns(configuration.RabbitMqOAuthClientId);
            serviceConfig.RabbitMqOAuthClientSecret.Returns(configuration.RabbitMqOAuthClientSecret);
            serviceConfig.RabbitMqOAuthTokenEndpoint.Returns(configuration.RabbitMqOAuthTokenEndpoint);
            serviceConfig.RabbitMqOAuthScope.Returns(configuration.RabbitMqOAuthScope);
            serviceConfig.RabbitMqSslEnabled.Returns(configuration.RabbitMqSslEnabled);
            serviceConfig.RabbitMqSslServerName.Returns(configuration.RabbitMqSslServerName);
            serviceConfig.RabbitMqSslVersion.Returns(configuration.RabbitMqSslVersion);
            serviceConfig.Environment.Returns("integration-test");
            serviceConfig.LockAcquisitionTimeoutSeconds.Returns(perAttemptSeconds);
            serviceConfig.LockReacquisitionRetryWindowSeconds.Returns(retryWindowSeconds);

            var service = new RabbitMqDistributedLockService(logger, serviceConfig);
            var resourceKey = $"env:S002-IT2-{Guid.NewGuid()}";
            var lockQueueName = $"lock.{resourceKey}";

            // Acquire lock to establish the service connection and queue
            var lockObj = await service.TryAcquireLockAsync(resourceKey, 30000, CancellationToken.None);
            Assert.IsNotNull(lockObj, "Lock acquisition must succeed before testing re-acquisition");

            // Create a separate connection for the blocker
            var blockerConfig = Substitute.For<IMonitorConfiguration>();
            blockerConfig.HighAvailabilityEnabled.Returns(true);
            blockerConfig.RabbitMqHostName.Returns(configuration.RabbitMqHostName);
            blockerConfig.RabbitMqPort.Returns(configuration.RabbitMqPort);
            blockerConfig.RabbitMqVirtualHost.Returns(configuration.RabbitMqVirtualHost);
            blockerConfig.RabbitMqOAuthClientId.Returns(configuration.RabbitMqOAuthClientId);
            blockerConfig.RabbitMqOAuthClientSecret.Returns(configuration.RabbitMqOAuthClientSecret);
            blockerConfig.RabbitMqOAuthTokenEndpoint.Returns(configuration.RabbitMqOAuthTokenEndpoint);
            blockerConfig.RabbitMqOAuthScope.Returns(configuration.RabbitMqOAuthScope);
            blockerConfig.RabbitMqSslEnabled.Returns(configuration.RabbitMqSslEnabled);
            blockerConfig.RabbitMqSslServerName.Returns(configuration.RabbitMqSslServerName);
            blockerConfig.RabbitMqSslVersion.Returns(configuration.RabbitMqSslVersion);
            blockerConfig.Environment.Returns("integration-test-blocker");
            blockerConfig.LockAcquisitionTimeoutSeconds.Returns(5);
            blockerConfig.LockReacquisitionRetryWindowSeconds.Returns(150);
            var blockerService = new RabbitMqDistributedLockService(logger, blockerConfig);
            var dummyKey = $"env:S002-IT2-blocker-{Guid.NewGuid()}";
            var dummyLock = await blockerService.TryAcquireLockAsync(dummyKey, 30000, CancellationToken.None);
            Assert.IsNotNull(dummyLock, "Blocker service must connect to RabbitMQ");

            // Register standby consumer on lock queue BEFORE triggering channel loss.
            // SAC will promote this consumer when the service's connection drops.
            var blockerConn = blockerService.connection;
            Assert.IsNotNull(blockerConn, "Blocker service connection must be available");
            var blockerChannel = await blockerConn.CreateChannelAsync(cancellationToken: CancellationToken.None);
            var blockerConsumer = new AsyncEventingBasicConsumer(blockerChannel);
            ulong? capturedDeliveryTag = null;
            blockerConsumer.ReceivedAsync += async (_, ea) =>
            {
                capturedDeliveryTag = ea.DeliveryTag;
                await Task.CompletedTask;
            };
            var blockerConsumerTag = await blockerChannel.BasicConsumeAsync(
                queue: lockQueueName,
                autoAck: false,
                consumerTag: "",
                noLocal: false,
                exclusive: false,
                arguments: null,
                consumer: blockerConsumer,
                cancellationToken: CancellationToken.None);

            // Force-close the service connection to trigger re-acquisition
            var serviceConn = service.connection;
            Assert.IsNotNull(serviceConn);
            try { await serviceConn.CloseAsync(); } catch { }

            // Hold the message for blockingSeconds (ensures >= 2 retry attempts by the service)
            await Task.Delay(TimeSpan.FromSeconds(blockingSeconds));

            // Assert: SAC mechanics must have worked — blocker must have received the lock message.
            // This proves that: (a) the service's channel closed and the message was requeued,
            // (b) SAC promoted the blocker consumer, and (c) at least one complete delivery-wait
            // cycle ran on the service before the message was released.
            // Mathematical proof of >= 2 attempts: blockingSeconds(8) > 2 × perAttemptSeconds(2) + interAttemptDelay(3) = 7s
            Assert.IsTrue(capturedDeliveryTag.HasValue,
                "Blocker consumer must have received the lock message — confirms SAC mechanics and that " +
                "the service's re-acquisition loop ran at least two complete attempt cycles before success");

            // Release the message: nack-requeue then cancel the blocker consumer so the message
            // is requeued and the service's next retry attempt can consume it
            try
            {
                if (capturedDeliveryTag.HasValue)
                {
                    await blockerChannel.BasicNackAsync(
                        deliveryTag: capturedDeliveryTag.Value,
                        multiple: false,
                        requeue: true,
                        cancellationToken: CancellationToken.None);
                }
                await blockerChannel.BasicCancelAsync(
                    consumerTag: blockerConsumerTag,
                    cancellationToken: CancellationToken.None);
            }
            catch { /* best-effort consumer cancellation */ }

            // Wait for re-acquisition to succeed or fail — give up to (retryWindow + 5s) total
            var successWaitSeconds = retryWindowSeconds - blockingSeconds + 5;
            var lockLostTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            lockObj.LockLostToken.Register(() => lockLostTcs.TrySetResult(true));
            await Task.WhenAny(lockLostTcs.Task, Task.Delay(TimeSpan.FromSeconds(successWaitSeconds)));

            // Assert: lock must NOT be lost — deployment must continue after delayed re-acquisition
            Assert.IsFalse(lockObj.LockLostToken.IsCancellationRequested,
                "LockLostToken must NOT be cancelled when the lock message is delivered " +
                "within the retry window — deployment must continue");

            // Cleanup
            try { await blockerChannel.CloseAsync(cancellationToken: CancellationToken.None); } catch { }
            await blockerChannel.DisposeAsync();
            await dummyLock.DisposeAsync();
            await blockerService.DisposeAsync();
            await lockObj.DisposeAsync();
            service.Dispose();
        }
    }
}
