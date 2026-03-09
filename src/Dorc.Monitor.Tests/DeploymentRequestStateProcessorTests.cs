using Dorc.ApiModel;
using Dorc.Core.Events;
using Dorc.Core.Interfaces;
using Dorc.Monitor.HighAvailability;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Collections.Concurrent;

namespace Dorc.Monitor.Tests
{
    [TestClass]
    public class DeploymentRequestStateProcessorTests
    {
        private ILogger<DeploymentRequestStateProcessor> mockLogger = null!;
        private IServiceProvider mockServiceProvider = null!;
        private IDeploymentRequestProcessesPersistentSource mockProcessesPersistentSource = null!;
        private IRequestsPersistentSource mockRequestsPersistentSource = null!;
        private IDeploymentEventsPublisher mockEventPublisher = null!;
        private IDistributedLockService mockDistributedLockService = null!;
        private IMonitorConfiguration mockMonitorConfiguration = null!;

        private DeploymentRequestStateProcessor sut = null!;

        [TestInitialize]
        public void Setup()
        {
            mockLogger = Substitute.For<ILogger<DeploymentRequestStateProcessor>>();
            mockServiceProvider = Substitute.For<IServiceProvider>();
            mockProcessesPersistentSource = Substitute.For<IDeploymentRequestProcessesPersistentSource>();
            mockRequestsPersistentSource = Substitute.For<IRequestsPersistentSource>();
            mockEventPublisher = Substitute.For<IDeploymentEventsPublisher>();
            mockDistributedLockService = Substitute.For<IDistributedLockService>();
            mockMonitorConfiguration = Substitute.For<IMonitorConfiguration>();

            mockEventPublisher.PublishRequestStatusChangedAsync(Arg.Any<DeploymentRequestEventData>())
                .Returns(Task.CompletedTask);

            sut = new DeploymentRequestStateProcessor(
                mockLogger,
                mockServiceProvider,
                mockProcessesPersistentSource,
                mockRequestsPersistentSource,
                mockEventPublisher,
                mockDistributedLockService,
                mockMonitorConfiguration);
        }

        private static List<DeploymentRequestApiModel> CreateRequests(params int[] ids)
        {
            return ids.Select(id => new DeploymentRequestApiModel
            {
                Id = id,
                EnvironmentName = "TestEnv",
                Status = DeploymentRequestStatus.Restarting.ToString(),
                IsProd = false,
                UserName = "testuser",
                RequestDetails = "<DeploymentRequestDetail xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\"><Components /><ComponentsToSkip /><Properties /></DeploymentRequestDetail>"
            }).ToList();
        }

        private static List<DeploymentRequestApiModel> CreateCancelRequests(params int[] ids)
        {
            return ids.Select(id => new DeploymentRequestApiModel
            {
                Id = id,
                EnvironmentName = "TestEnv",
                Status = DeploymentRequestStatus.Cancelling.ToString(),
                IsProd = false,
                UserName = "testuser"
            }).ToList();
        }

        // =====================================================================
        // Fix 3a: RestartRequests - status switch happens FIRST
        // =====================================================================

        [TestMethod]
        public void RestartRequests_NoRequests_DoesNothing()
        {
            // Arrange
            mockRequestsPersistentSource
                .GetRequestsWithStatus(DeploymentRequestStatus.Restarting, false)
                .Returns(Enumerable.Empty<DeploymentRequestApiModel>());

            var cancellationSources = new ConcurrentDictionary<int, CancellationTokenSource>();

            // Act
            sut.RestartRequests(false, cancellationSources, CancellationToken.None);

            // Assert - should not call any DB operations
            mockRequestsPersistentSource.DidNotReceive()
                .SwitchDeploymentRequestStatuses(
                    Arg.Any<IList<DeploymentRequestApiModel>>(),
                    Arg.Any<DeploymentRequestStatus>(),
                    Arg.Any<DeploymentRequestStatus>());
            mockRequestsPersistentSource.DidNotReceive()
                .ClearAllDeploymentResults(Arg.Any<IList<int>>());
        }

        [TestMethod]
        public void RestartRequests_SwitchStatusReturnsZero_DoesNotClearResults()
        {
            // Arrange: another monitor already processed these
            var requests = CreateRequests(1, 2);
            mockRequestsPersistentSource
                .GetRequestsWithStatus(DeploymentRequestStatus.Restarting, false)
                .Returns(requests);
            mockRequestsPersistentSource
                .SwitchDeploymentRequestStatuses(
                    Arg.Any<IList<DeploymentRequestApiModel>>(),
                    DeploymentRequestStatus.Restarting,
                    DeploymentRequestStatus.Pending)
                .Returns(0);

            var cancellationSources = new ConcurrentDictionary<int, CancellationTokenSource>();

            // Act
            sut.RestartRequests(false, cancellationSources, CancellationToken.None);

            // Assert - SwitchStatus was called (FIRST)
            mockRequestsPersistentSource.Received(1)
                .SwitchDeploymentRequestStatuses(
                    Arg.Any<IList<DeploymentRequestApiModel>>(),
                    DeploymentRequestStatus.Restarting,
                    DeploymentRequestStatus.Pending);
            // ClearAllDeploymentResults should NOT be called
            mockRequestsPersistentSource.DidNotReceive()
                .ClearAllDeploymentResults(Arg.Any<IList<int>>());
            // Event publisher should NOT be called
            mockEventPublisher.DidNotReceive()
                .PublishRequestStatusChangedAsync(Arg.Any<DeploymentRequestEventData>());
        }

        [TestMethod]
        public void RestartRequests_SwitchStatusReturnsAll_ClearsResultsAndPublishes()
        {
            // Arrange: we win the optimistic concurrency race for all requests
            var requests = CreateRequests(1, 2);
            mockRequestsPersistentSource
                .GetRequestsWithStatus(DeploymentRequestStatus.Restarting, false)
                .Returns(requests);
            mockRequestsPersistentSource
                .SwitchDeploymentRequestStatuses(
                    Arg.Any<IList<DeploymentRequestApiModel>>(),
                    DeploymentRequestStatus.Restarting,
                    DeploymentRequestStatus.Pending)
                .Returns(2);

            var cancellationSources = new ConcurrentDictionary<int, CancellationTokenSource>();

            // Act
            sut.RestartRequests(false, cancellationSources, CancellationToken.None);

            // Assert
            // SwitchStatus called FIRST
            mockRequestsPersistentSource.Received(1)
                .SwitchDeploymentRequestStatuses(
                    Arg.Any<IList<DeploymentRequestApiModel>>(),
                    DeploymentRequestStatus.Restarting,
                    DeploymentRequestStatus.Pending);
            // ClearAllDeploymentResults called (because we won)
            mockRequestsPersistentSource.Received(1)
                .ClearAllDeploymentResults(Arg.Is<IList<int>>(ids => ids.Count == 2));
            // Event published for each request
            mockEventPublisher.Received(2)
                .PublishRequestStatusChangedAsync(Arg.Any<DeploymentRequestEventData>());
        }

        [TestMethod]
        public void RestartRequests_SwitchStatusReturnsPartial_ClearsResultsAndPublishesAndLogsPartial()
        {
            // Arrange: we won for 1 of 2 requests
            var requests = CreateRequests(1, 2);
            mockRequestsPersistentSource
                .GetRequestsWithStatus(DeploymentRequestStatus.Restarting, false)
                .Returns(requests);
            mockRequestsPersistentSource
                .SwitchDeploymentRequestStatuses(
                    Arg.Any<IList<DeploymentRequestApiModel>>(),
                    DeploymentRequestStatus.Restarting,
                    DeploymentRequestStatus.Pending)
                .Returns(1);

            var cancellationSources = new ConcurrentDictionary<int, CancellationTokenSource>();

            // Act
            sut.RestartRequests(false, cancellationSources, CancellationToken.None);

            // Assert - ClearAllDeploymentResults still called (partial success)
            mockRequestsPersistentSource.Received(1)
                .ClearAllDeploymentResults(Arg.Is<IList<int>>(ids => ids.Count == 2));
            // Events published for all IDs (terminate/publish are idempotent)
            mockEventPublisher.Received(2)
                .PublishRequestStatusChangedAsync(Arg.Any<DeploymentRequestEventData>());
        }

        [TestMethod]
        public void RestartRequests_SwitchStatusCalledBeforeClearResults()
        {
            // Arrange: verify ordering - SwitchStatus must happen before ClearAllDeploymentResults
            var requests = CreateRequests(1);
            mockRequestsPersistentSource
                .GetRequestsWithStatus(DeploymentRequestStatus.Restarting, false)
                .Returns(requests);

            var callOrder = new List<string>();
            mockRequestsPersistentSource
                .SwitchDeploymentRequestStatuses(
                    Arg.Any<IList<DeploymentRequestApiModel>>(),
                    Arg.Any<DeploymentRequestStatus>(),
                    Arg.Any<DeploymentRequestStatus>())
                .Returns(x =>
                {
                    callOrder.Add("SwitchStatus");
                    return 1;
                });
            mockRequestsPersistentSource
                .When(x => x.ClearAllDeploymentResults(Arg.Any<IList<int>>()))
                .Do(_ => callOrder.Add("ClearResults"));

            var cancellationSources = new ConcurrentDictionary<int, CancellationTokenSource>();

            // Act
            sut.RestartRequests(false, cancellationSources, CancellationToken.None);

            // Assert - SwitchStatus was called BEFORE ClearResults
            Assert.AreEqual(2, callOrder.Count);
            Assert.AreEqual("SwitchStatus", callOrder[0]);
            Assert.AreEqual("ClearResults", callOrder[1]);
        }

        // =====================================================================
        // Fix 3b: CancelRequests - conditional SwitchDeploymentResultsStatus
        // =====================================================================

        [TestMethod]
        public void CancelRequests_WhenSwitchReturnsZero_DoesNotSwitchDeploymentResults()
        {
            // Arrange: another monitor already cancelled these
            var requests = CreateCancelRequests(1, 2);
            mockRequestsPersistentSource
                .GetRequestsWithStatus(DeploymentRequestStatus.Cancelling, false)
                .Returns(requests);
            mockRequestsPersistentSource
                .SwitchDeploymentRequestStatuses(
                    Arg.Any<IList<DeploymentRequestApiModel>>(),
                    DeploymentRequestStatus.Cancelling,
                    DeploymentRequestStatus.Cancelled,
                    Arg.Any<DateTimeOffset>())
                .Returns(0);

            var cancellationSources = new ConcurrentDictionary<int, CancellationTokenSource>();

            // Act
            sut.CancelRequests(false, cancellationSources, CancellationToken.None);

            // Assert - SwitchDeploymentResultsStatuses should NOT be called
            mockRequestsPersistentSource.DidNotReceive()
                .SwitchDeploymentResultsStatuses(
                    Arg.Any<IList<DeploymentRequestApiModel>>(),
                    Arg.Is<DeploymentResultStatus>(s => s.Value == "Pending"),
                    Arg.Is<DeploymentResultStatus>(s => s.Value == "Cancelled"));
        }

        [TestMethod]
        public void CancelRequests_WhenSwitchReturnsPositive_SwitchesDeploymentResults()
        {
            // Arrange: we successfully cancelled some
            var requests = CreateCancelRequests(1, 2);
            mockRequestsPersistentSource
                .GetRequestsWithStatus(DeploymentRequestStatus.Cancelling, false)
                .Returns(requests);
            mockRequestsPersistentSource
                .SwitchDeploymentRequestStatuses(
                    Arg.Any<IList<DeploymentRequestApiModel>>(),
                    DeploymentRequestStatus.Cancelling,
                    DeploymentRequestStatus.Cancelled,
                    Arg.Any<DateTimeOffset>())
                .Returns(2);

            var cancellationSources = new ConcurrentDictionary<int, CancellationTokenSource>();

            // Act
            sut.CancelRequests(false, cancellationSources, CancellationToken.None);

            // Assert - SwitchDeploymentResultsStatuses IS called
            // Note: DeploymentResultStatus doesn't override Object.Equals so we match by Value property
            mockRequestsPersistentSource.Received(1)
                .SwitchDeploymentResultsStatuses(
                    Arg.Any<IList<DeploymentRequestApiModel>>(),
                    Arg.Is<DeploymentResultStatus>(s => s.Value == "Pending"),
                    Arg.Is<DeploymentResultStatus>(s => s.Value == "Cancelled"));
        }

        [TestMethod]
        public void CancelRequests_WhenSwitchReturnsPartial_StillSwitchesDeploymentResults()
        {
            // Arrange: we cancelled 1 of 2
            var requests = CreateCancelRequests(1, 2);
            mockRequestsPersistentSource
                .GetRequestsWithStatus(DeploymentRequestStatus.Cancelling, false)
                .Returns(requests);
            mockRequestsPersistentSource
                .SwitchDeploymentRequestStatuses(
                    Arg.Any<IList<DeploymentRequestApiModel>>(),
                    DeploymentRequestStatus.Cancelling,
                    DeploymentRequestStatus.Cancelled,
                    Arg.Any<DateTimeOffset>())
                .Returns(1);

            var cancellationSources = new ConcurrentDictionary<int, CancellationTokenSource>();

            // Act
            sut.CancelRequests(false, cancellationSources, CancellationToken.None);

            // Assert - SwitchDeploymentResultsStatuses IS called (partial > 0)
            mockRequestsPersistentSource.Received(1)
                .SwitchDeploymentResultsStatuses(
                    Arg.Any<IList<DeploymentRequestApiModel>>(),
                    Arg.Is<DeploymentResultStatus>(s => s.Value == "Pending"),
                    Arg.Is<DeploymentResultStatus>(s => s.Value == "Cancelled"));
        }

        [TestMethod]
        public void CancelRequests_NoRequests_DoesNothing()
        {
            // Arrange
            mockRequestsPersistentSource
                .GetRequestsWithStatus(DeploymentRequestStatus.Cancelling, false)
                .Returns(Enumerable.Empty<DeploymentRequestApiModel>());

            var cancellationSources = new ConcurrentDictionary<int, CancellationTokenSource>();

            // Act
            sut.CancelRequests(false, cancellationSources, CancellationToken.None);

            // Assert
            mockRequestsPersistentSource.DidNotReceive()
                .SwitchDeploymentRequestStatuses(
                    Arg.Any<IList<DeploymentRequestApiModel>>(),
                    Arg.Any<DeploymentRequestStatus>(),
                    Arg.Any<DeploymentRequestStatus>(),
                    Arg.Any<DateTimeOffset>());
            mockRequestsPersistentSource.DidNotReceive()
                .SwitchDeploymentResultsStatuses(
                    Arg.Any<IList<DeploymentRequestApiModel>>(),
                    Arg.Is<DeploymentResultStatus>(s => s.Value == "Pending"),
                    Arg.Is<DeploymentResultStatus>(s => s.Value == "Cancelled"));
        }

        // =====================================================================
        // Fix 2b: ExecuteRequests - lock backoff
        // =====================================================================

        private List<DeploymentRequestApiModel> CreatePendingRequests(string envName, params int[] ids)
        {
            return ids.Select(id => new DeploymentRequestApiModel
            {
                Id = id,
                EnvironmentName = envName,
                Status = DeploymentRequestStatus.Pending.ToString(),
                IsProd = false,
                UserName = "testuser",
                RequestDetails = "<DeploymentRequestDetail xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\"><Components /><ComponentsToSkip /><Properties /></DeploymentRequestDetail>"
            }).ToList();
        }

        [TestMethod]
        public async Task ExecuteRequests_LockAcquisitionFails_RecordsBackoff()
        {
            // Arrange
            var requests = CreatePendingRequests("EnvA", 1);
            mockRequestsPersistentSource
                .GetRequestsWithStatus(
                    DeploymentRequestStatus.Pending,
                    DeploymentRequestStatus.Running,
                    DeploymentRequestStatus.Confirmed,
                    DeploymentRequestStatus.Paused,
                    false)
                .Returns(requests);

            mockDistributedLockService.IsEnabled.Returns(true);
            mockDistributedLockService
                .TryAcquireLockAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns((IDistributedLock?)null);

            var cancellationSources = new ConcurrentDictionary<int, CancellationTokenSource>();

            // Act - first call: lock fails, backoff recorded
            var tasks = sut.ExecuteRequests(false, cancellationSources, CancellationToken.None);
            await Task.WhenAll(tasks);

            // Act - second call immediately: environment should be in backoff, no lock attempt
            mockDistributedLockService.ClearReceivedCalls();
            var tasks2 = sut.ExecuteRequests(false, cancellationSources, CancellationToken.None);
            await Task.WhenAll(tasks2);

            // Assert - second call should not attempt lock acquisition (skipped due to backoff)
            await mockDistributedLockService.DidNotReceive()
                .TryAcquireLockAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        }

        [TestMethod]
        public async Task ExecuteRequests_LockAcquisitionSucceeds_ClearsBackoff()
        {
            // Arrange
            var mockLock = Substitute.For<IDistributedLock>();
            mockLock.ResourceKey.Returns("env:EnvB");

            var requests = CreatePendingRequests("EnvB", 10);
            mockRequestsPersistentSource
                .GetRequestsWithStatus(
                    DeploymentRequestStatus.Pending,
                    DeploymentRequestStatus.Running,
                    DeploymentRequestStatus.Confirmed,
                    DeploymentRequestStatus.Paused,
                    false)
                .Returns(requests);

            mockDistributedLockService.IsEnabled.Returns(true);

            // First call: lock fails (records backoff)
            mockDistributedLockService
                .TryAcquireLockAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns((IDistributedLock?)null);

            var cancellationSources = new ConcurrentDictionary<int, CancellationTokenSource>();

            var tasks = sut.ExecuteRequests(false, cancellationSources, CancellationToken.None);
            await Task.WhenAll(tasks);

            // Now simulate backoff expiry by making the lock succeed
            // and resetting - we can't easily manipulate the backoff dictionary directly
            // since it's private. But we can verify the flow works when lock succeeds:
            // Create a fresh processor to test the success path
            var sut2 = new DeploymentRequestStateProcessor(
                mockLogger,
                mockServiceProvider,
                mockProcessesPersistentSource,
                mockRequestsPersistentSource,
                mockEventPublisher,
                mockDistributedLockService,
                mockMonitorConfiguration);

            mockDistributedLockService
                .TryAcquireLockAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(mockLock);
            mockRequestsPersistentSource.GetRequest(10)
                .Returns(new DeploymentRequestApiModel
                {
                    Id = 10,
                    Status = DeploymentRequestStatus.Pending.ToString()
                });
            mockRequestsPersistentSource
                .UpdateNonProcessedRequest(
                    Arg.Any<DeploymentRequestApiModel>(),
                    Arg.Any<DeploymentRequestStatus>(),
                    Arg.Any<DateTimeOffset>())
                .Returns(0); // Return 0 to avoid going into execution

            var tasks2 = sut2.ExecuteRequests(false, cancellationSources, CancellationToken.None);
            await Task.WhenAll(tasks2);

            // Assert - lock was acquired, so a subsequent call should NOT be in backoff
            mockDistributedLockService.ClearReceivedCalls();
            var tasks3 = sut2.ExecuteRequests(false, cancellationSources, CancellationToken.None);
            await Task.WhenAll(tasks3);

            // The lock should be attempted again (not skipped due to backoff)
            await mockDistributedLockService.Received()
                .TryAcquireLockAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        }

        [TestMethod]
        public async Task ExecuteRequests_DistributedLockDisabled_NoBackoffRecorded()
        {
            // Arrange
            var requests = CreatePendingRequests("EnvC", 20);
            mockRequestsPersistentSource
                .GetRequestsWithStatus(
                    DeploymentRequestStatus.Pending,
                    DeploymentRequestStatus.Running,
                    DeploymentRequestStatus.Confirmed,
                    DeploymentRequestStatus.Paused,
                    false)
                .Returns(requests);

            mockDistributedLockService.IsEnabled.Returns(false);
            mockRequestsPersistentSource
                .UpdateNonProcessedRequest(
                    Arg.Any<DeploymentRequestApiModel>(),
                    Arg.Any<DeploymentRequestStatus>(),
                    Arg.Any<DateTimeOffset>())
                .Returns(0); // Return 0 to avoid going into execution

            var cancellationSources = new ConcurrentDictionary<int, CancellationTokenSource>();

            // Act - first call
            var tasks = sut.ExecuteRequests(false, cancellationSources, CancellationToken.None);
            await Task.WhenAll(tasks);

            // Act - second call: should still proceed (no backoff when locking is disabled)
            mockRequestsPersistentSource.ClearReceivedCalls();
            var tasks2 = sut.ExecuteRequests(false, cancellationSources, CancellationToken.None);
            await Task.WhenAll(tasks2);

            // Assert - UpdateNonProcessedRequest called again (environment was not skipped)
            mockRequestsPersistentSource.Received()
                .UpdateNonProcessedRequest(
                    Arg.Any<DeploymentRequestApiModel>(),
                    Arg.Any<DeploymentRequestStatus>(),
                    Arg.Any<DateTimeOffset>());
        }

        // =====================================================================
        // RecoverOrphanedRequestsAsync tests
        // =====================================================================

        private static List<DeploymentRequestApiModel> CreateOrphanedRequests(string envName, DeploymentRequestStatus status, params int[] ids)
        {
            return ids.Select(id => new DeploymentRequestApiModel
            {
                Id = id,
                EnvironmentName = envName,
                Status = status.ToString(),
                IsProd = false,
                UserName = "testuser"
            }).ToList();
        }

        [TestMethod]
        public async Task RecoverOrphanedRequests_NoOrphanedRequests_DoesNothing()
        {
            // Arrange
            mockRequestsPersistentSource
                .GetRequestsWithStatus(DeploymentRequestStatus.Running, DeploymentRequestStatus.Requesting, false)
                .Returns(Enumerable.Empty<DeploymentRequestApiModel>());

            // Act
            await sut.RecoverOrphanedRequestsAsync(false);

            // Assert - no status switches or result clears
            mockRequestsPersistentSource.DidNotReceive()
                .SwitchDeploymentRequestStatuses(
                    Arg.Any<IList<DeploymentRequestApiModel>>(),
                    Arg.Any<DeploymentRequestStatus>(),
                    Arg.Any<DeploymentRequestStatus>());
            mockRequestsPersistentSource.DidNotReceive()
                .ClearAllDeploymentResults(Arg.Any<IList<int>>());
        }

        [TestMethod]
        public async Task RecoverOrphanedRequests_RunningRequests_ResetsTosPendingAndClearsResults()
        {
            // Arrange
            var orphaned = CreateOrphanedRequests("TestEnv", DeploymentRequestStatus.Running, 1, 2);
            mockRequestsPersistentSource
                .GetRequestsWithStatus(DeploymentRequestStatus.Running, DeploymentRequestStatus.Requesting, false)
                .Returns(orphaned);
            mockDistributedLockService.IsEnabled.Returns(false);
            mockRequestsPersistentSource
                .SwitchDeploymentRequestStatuses(
                    Arg.Any<IList<DeploymentRequestApiModel>>(),
                    DeploymentRequestStatus.Running,
                    DeploymentRequestStatus.Pending)
                .Returns(2);

            // Act
            await sut.RecoverOrphanedRequestsAsync(false);

            // Assert - Running → Pending
            mockRequestsPersistentSource.Received(1)
                .SwitchDeploymentRequestStatuses(
                    Arg.Is<IList<DeploymentRequestApiModel>>(l => l.Count == 2),
                    DeploymentRequestStatus.Running,
                    DeploymentRequestStatus.Pending);

            // Assert - deployment results cleared
            mockRequestsPersistentSource.Received(1)
                .ClearAllDeploymentResults(Arg.Is<IList<int>>(ids => ids.Count == 2));

            // Assert - events published
            mockEventPublisher.Received(2)
                .PublishRequestStatusChangedAsync(Arg.Any<DeploymentRequestEventData>());
        }

        [TestMethod]
        public async Task RecoverOrphanedRequests_RequestingRequests_ResetsToPending()
        {
            // Arrange
            var orphaned = CreateOrphanedRequests("TestEnv", DeploymentRequestStatus.Requesting, 5);
            mockRequestsPersistentSource
                .GetRequestsWithStatus(DeploymentRequestStatus.Running, DeploymentRequestStatus.Requesting, false)
                .Returns(orphaned);
            mockDistributedLockService.IsEnabled.Returns(false);
            mockRequestsPersistentSource
                .SwitchDeploymentRequestStatuses(
                    Arg.Any<IList<DeploymentRequestApiModel>>(),
                    DeploymentRequestStatus.Requesting,
                    DeploymentRequestStatus.Pending)
                .Returns(1);

            // Act
            await sut.RecoverOrphanedRequestsAsync(false);

            // Assert - Requesting → Pending
            mockRequestsPersistentSource.Received(1)
                .SwitchDeploymentRequestStatuses(
                    Arg.Is<IList<DeploymentRequestApiModel>>(l => l.Count == 1),
                    DeploymentRequestStatus.Requesting,
                    DeploymentRequestStatus.Pending);
        }

        [TestMethod]
        public async Task RecoverOrphanedRequests_MixedStatuses_ResetsBothRunningAndRequesting()
        {
            // Arrange - mixed Running and Requesting in same environment
            var runningReqs = CreateOrphanedRequests("TestEnv", DeploymentRequestStatus.Running, 1);
            var requestingReqs = CreateOrphanedRequests("TestEnv", DeploymentRequestStatus.Requesting, 2);
            var allOrphaned = runningReqs.Concat(requestingReqs).ToList();

            mockRequestsPersistentSource
                .GetRequestsWithStatus(DeploymentRequestStatus.Running, DeploymentRequestStatus.Requesting, false)
                .Returns(allOrphaned);
            mockDistributedLockService.IsEnabled.Returns(false);
            mockRequestsPersistentSource
                .SwitchDeploymentRequestStatuses(
                    Arg.Any<IList<DeploymentRequestApiModel>>(),
                    Arg.Is(DeploymentRequestStatus.Running),
                    Arg.Is(DeploymentRequestStatus.Pending))
                .Returns(1);
            mockRequestsPersistentSource
                .SwitchDeploymentRequestStatuses(
                    Arg.Any<IList<DeploymentRequestApiModel>>(),
                    Arg.Is(DeploymentRequestStatus.Requesting),
                    Arg.Is(DeploymentRequestStatus.Pending))
                .Returns(1);

            // Act
            await sut.RecoverOrphanedRequestsAsync(false);

            // Assert - both statuses reset
            mockRequestsPersistentSource.Received(1)
                .SwitchDeploymentRequestStatuses(
                    Arg.Any<IList<DeploymentRequestApiModel>>(),
                    Arg.Is(DeploymentRequestStatus.Running),
                    Arg.Is(DeploymentRequestStatus.Pending));
            mockRequestsPersistentSource.Received(1)
                .SwitchDeploymentRequestStatuses(
                    Arg.Any<IList<DeploymentRequestApiModel>>(),
                    Arg.Is(DeploymentRequestStatus.Requesting),
                    Arg.Is(DeploymentRequestStatus.Pending));
        }

        [TestMethod]
        public async Task RecoverOrphanedRequests_HAEnabled_LockAcquired_ResetsRequests()
        {
            // Arrange
            var mockLock = Substitute.For<IDistributedLock>();
            mockLock.ResourceKey.Returns("env:TestEnv");

            var orphaned = CreateOrphanedRequests("TestEnv", DeploymentRequestStatus.Running, 1);
            mockRequestsPersistentSource
                .GetRequestsWithStatus(DeploymentRequestStatus.Running, DeploymentRequestStatus.Requesting, false)
                .Returns(orphaned);
            mockDistributedLockService.IsEnabled.Returns(true);
            mockDistributedLockService
                .TryAcquireLockAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(mockLock);
            mockRequestsPersistentSource
                .SwitchDeploymentRequestStatuses(
                    Arg.Any<IList<DeploymentRequestApiModel>>(),
                    DeploymentRequestStatus.Running,
                    DeploymentRequestStatus.Pending)
                .Returns(1);

            // Act
            await sut.RecoverOrphanedRequestsAsync(false);

            // Assert - lock acquired and requests reset
            await mockDistributedLockService.Received(1)
                .TryAcquireLockAsync("env:TestEnv", Arg.Any<int>(), Arg.Any<CancellationToken>());
            mockRequestsPersistentSource.Received(1)
                .SwitchDeploymentRequestStatuses(
                    Arg.Any<IList<DeploymentRequestApiModel>>(),
                    DeploymentRequestStatus.Running,
                    DeploymentRequestStatus.Pending);

            // Assert - lock released
            await mockLock.Received(1).DisposeAsync();
        }

        [TestMethod]
        public async Task RecoverOrphanedRequests_HAEnabled_LockNotAcquired_SkipsEnvironment()
        {
            // Arrange - another monitor holds the lock
            var orphaned = CreateOrphanedRequests("TestEnv", DeploymentRequestStatus.Running, 1);
            mockRequestsPersistentSource
                .GetRequestsWithStatus(DeploymentRequestStatus.Running, DeploymentRequestStatus.Requesting, false)
                .Returns(orphaned);
            mockDistributedLockService.IsEnabled.Returns(true);
            mockDistributedLockService
                .TryAcquireLockAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns((IDistributedLock?)null);

            // Act
            await sut.RecoverOrphanedRequestsAsync(false);

            // Assert - no status changes (skipped because lock not acquired)
            mockRequestsPersistentSource.DidNotReceive()
                .SwitchDeploymentRequestStatuses(
                    Arg.Any<IList<DeploymentRequestApiModel>>(),
                    Arg.Any<DeploymentRequestStatus>(),
                    Arg.Any<DeploymentRequestStatus>());
            mockRequestsPersistentSource.DidNotReceive()
                .ClearAllDeploymentResults(Arg.Any<IList<int>>());
        }

        [TestMethod]
        public async Task RecoverOrphanedRequests_KillsRunnerProcessesBeforeReset()
        {
            // Arrange
            var orphaned = CreateOrphanedRequests("TestEnv", DeploymentRequestStatus.Running, 42);
            mockRequestsPersistentSource
                .GetRequestsWithStatus(DeploymentRequestStatus.Running, DeploymentRequestStatus.Requesting, false)
                .Returns(orphaned);
            mockDistributedLockService.IsEnabled.Returns(false);
            mockProcessesPersistentSource
                .GetAssociatedRunnerProcessIds(42)
                .Returns(new List<int> { 9999 });
            mockRequestsPersistentSource
                .SwitchDeploymentRequestStatuses(
                    Arg.Any<IList<DeploymentRequestApiModel>>(),
                    DeploymentRequestStatus.Running,
                    DeploymentRequestStatus.Pending)
                .Returns(1);

            // Act
            await sut.RecoverOrphanedRequestsAsync(false);

            // Assert - runner processes queried for cleanup
            mockProcessesPersistentSource.Received(1)
                .GetAssociatedRunnerProcessIds(42);
        }

        [TestMethod]
        public async Task RecoverOrphanedRequests_MultipleEnvironments_RecoversBothIndependently()
        {
            // Arrange - orphaned requests across two environments
            var envARequests = CreateOrphanedRequests("EnvA", DeploymentRequestStatus.Running, 1);
            var envBRequests = CreateOrphanedRequests("EnvB", DeploymentRequestStatus.Running, 2);
            var allOrphaned = envARequests.Concat(envBRequests).ToList();

            mockRequestsPersistentSource
                .GetRequestsWithStatus(DeploymentRequestStatus.Running, DeploymentRequestStatus.Requesting, false)
                .Returns(allOrphaned);
            mockDistributedLockService.IsEnabled.Returns(false);
            mockRequestsPersistentSource
                .SwitchDeploymentRequestStatuses(
                    Arg.Any<IList<DeploymentRequestApiModel>>(),
                    DeploymentRequestStatus.Running,
                    DeploymentRequestStatus.Pending)
                .Returns(1);

            // Act
            await sut.RecoverOrphanedRequestsAsync(false);

            // Assert - switch called twice (once per environment)
            mockRequestsPersistentSource.Received(2)
                .SwitchDeploymentRequestStatuses(
                    Arg.Any<IList<DeploymentRequestApiModel>>(),
                    DeploymentRequestStatus.Running,
                    DeploymentRequestStatus.Pending);

            // Assert - results cleared for both environments
            mockRequestsPersistentSource.Received(2)
                .ClearAllDeploymentResults(Arg.Any<IList<int>>());
        }
    }
}
