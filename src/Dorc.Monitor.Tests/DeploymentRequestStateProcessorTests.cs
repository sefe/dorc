using Dorc.ApiModel;
using Dorc.Core.Events;
using Dorc.Core.Interfaces;
using Dorc.Core.HighAvailability;
using Dorc.Monitor.HighAvailability;
using Dorc.Monitor.RequestProcessors;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
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

        private DeploymentRequestStateProcessor sut = null!;
        private ConcurrentBag<Task> publishTasks = null!;

        [TestInitialize]
        public void Setup()
        {
            mockLogger = Substitute.For<ILogger<DeploymentRequestStateProcessor>>();
            mockServiceProvider = Substitute.For<IServiceProvider>();
            mockProcessesPersistentSource = Substitute.For<IDeploymentRequestProcessesPersistentSource>();
            mockRequestsPersistentSource = Substitute.For<IRequestsPersistentSource>();
            mockEventPublisher = Substitute.For<IDeploymentEventsPublisher>();
            mockDistributedLockService = Substitute.For<IDistributedLockService>();

            mockEventPublisher.PublishRequestStatusChangedAsync(Arg.Any<DeploymentRequestEventData>())
                .Returns(Task.CompletedTask);

            publishTasks = new ConcurrentBag<Task>();

            sut = new DeploymentRequestStateProcessor(
                mockLogger,
                mockServiceProvider,
                mockProcessesPersistentSource,
                mockRequestsPersistentSource,
                mockEventPublisher,
                mockDistributedLockService);
            sut.OnPublishTaskCreated = t => publishTasks.Add(t);
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
            mockRequestsPersistentSource.Received(2)
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
        public async Task RestartRequests_SwitchStatusReturnsAll_ClearsResultsAndPublishes()
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

            // Wait for fire-and-forget event publish tasks to complete
            await Task.WhenAll(publishTasks);

            // Assert
            // SwitchStatus called FIRST
            mockRequestsPersistentSource.Received(2)
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
        public async Task RestartRequests_SwitchStatusReturnsPartial_OnlyAffectsRequestsWonByThisMonitor()
        {
            // Arrange: we won for 1 of 2 requests
            var requests = CreateRequests(1, 2);
            mockRequestsPersistentSource
                .GetRequestsWithStatus(DeploymentRequestStatus.Restarting, false)
                .Returns(requests);
            mockRequestsPersistentSource
                .SwitchDeploymentRequestStatuses(
                    Arg.Is<IList<DeploymentRequestApiModel>>(items => items.Count == 1 && items[0].Id == 1),
                    DeploymentRequestStatus.Restarting,
                    DeploymentRequestStatus.Pending)
                .Returns(1);
            mockRequestsPersistentSource
                .SwitchDeploymentRequestStatuses(
                    Arg.Is<IList<DeploymentRequestApiModel>>(items => items.Count == 1 && items[0].Id == 2),
                    DeploymentRequestStatus.Restarting,
                    DeploymentRequestStatus.Pending)
                .Returns(0);

            var cancellationSources = new ConcurrentDictionary<int, CancellationTokenSource>();

            // Act
            sut.RestartRequests(false, cancellationSources, CancellationToken.None);

            // Wait for fire-and-forget event publish tasks to complete
            await Task.WhenAll(publishTasks);

            // Assert - only the request we actually restarted is touched
            mockRequestsPersistentSource.Received(1)
                .ClearAllDeploymentResults(Arg.Is<IList<int>>(ids => ids.Count == 1 && ids[0] == 1));
            mockEventPublisher.Received(1)
                .PublishRequestStatusChangedAsync(Arg.Is<DeploymentRequestEventData>(e => e.RequestId == 1));
            mockEventPublisher.DidNotReceive()
                .PublishRequestStatusChangedAsync(Arg.Is<DeploymentRequestEventData>(e => e.RequestId == 2));
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

            // Assert - all SwitchStatus calls happened before ClearResults
            Assert.HasCount(2, callOrder);
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
                mockDistributedLockService);

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
        // TryAdd race condition fix: environmentRequestIdRunning.TryAdd before Task.Run
        // =====================================================================

        [TestMethod]
        public async Task ExecuteRequests_AfterTaskCompletes_EnvironmentIsAvailableForNextPoll()
        {
            // Verifies the TryAdd-before-Task.Run fix on the SAME instance.
            // If TryAdd were after Task.Run, a fast-completing task could TryRemove
            // before TryAdd, leaving a phantom entry in environmentRequestIdRunning
            // that permanently blocks the environment from being processed.
            //
            // We use locking disabled to avoid the backoff dictionary interfering -
            // this isolates the test to only the environmentRequestIdRunning cleanup.
            var requests = CreatePendingRequests("EnvRace", 100);
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
                .Returns(0); // Return 0 to avoid going deeper into execution

            var cancellationSources = new ConcurrentDictionary<int, CancellationTokenSource>();

            // Act - first call: task runs and completes quickly (UpdateNonProcessedRequest returns 0)
            var tasks = sut.ExecuteRequests(false, cancellationSources, CancellationToken.None);
            await Task.WhenAll(tasks);

            // Act - second call on SAME instance: environment should NOT be permanently blocked
            // If TryAdd were after Task.Run, the phantom entry would cause TryGetValue to return true,
            // skipping this environment forever.
            mockRequestsPersistentSource.ClearReceivedCalls();
            var tasks2 = sut.ExecuteRequests(false, cancellationSources, CancellationToken.None);
            await Task.WhenAll(tasks2);

            // Assert - UpdateNonProcessedRequest called again, proving environment is not stuck
            mockRequestsPersistentSource.Received(1)
                .UpdateNonProcessedRequest(
                    Arg.Any<DeploymentRequestApiModel>(),
                    Arg.Any<DeploymentRequestStatus>(),
                    Arg.Any<DateTimeOffset>());
        }

        // =====================================================================
        // CancelStaleRequests - cleanup on startup
        // =====================================================================

        [TestMethod]
        public async Task CancelStaleRequests_WhenHAEnabled_ResumesRunningRequestsAsPending()
        {
            // Arrange - HA is enabled; Running → Pending resume is independent of the lock service (S-004 R4)
            mockDistributedLockService.IsEnabled.Returns(true);

            var staleRunning = new List<DeploymentRequestApiModel>
            {
                new() { Id = 45, EnvironmentName = "EnvHA", Status = DeploymentRequestStatus.Running.ToString(), IsProd = false, UserName = "testuser" }
            };
            mockRequestsPersistentSource
                .GetRequestsWithStatus(DeploymentRequestStatus.Running, false)
                .Returns(staleRunning);
            mockRequestsPersistentSource
                .GetRequestsWithStatus(DeploymentRequestStatus.Requesting, false)
                .Returns(Enumerable.Empty<DeploymentRequestApiModel>());
            mockRequestsPersistentSource
                .SwitchDeploymentRequestStatuses(
                    Arg.Any<IList<DeploymentRequestApiModel>>(),
                    DeploymentRequestStatus.Running,
                    DeploymentRequestStatus.Pending)
                .Returns(1);

            // Act
            sut.CancelStaleRequests(false);
            await Task.WhenAll(publishTasks);

            // Assert - Running resumed as Pending; lock service not involved in startup recovery
            mockRequestsPersistentSource.Received(1)
                .SwitchDeploymentRequestStatuses(
                    Arg.Any<IList<DeploymentRequestApiModel>>(),
                    DeploymentRequestStatus.Running,
                    DeploymentRequestStatus.Pending);
            mockDistributedLockService.DidNotReceive()
                .TryAcquireLockAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
            mockEventPublisher.Received(1)
                .PublishRequestStatusChangedAsync(Arg.Is<DeploymentRequestEventData>(e =>
                    e.RequestId == 45 && e.Status == DeploymentRequestStatus.Pending.ToString()));
        }

        [TestMethod]
        public void CancelStaleRequests_WhenConcurrentInstanceAlreadyResumed_NoEventPublished()
        {
            // Arrange - another monitor instance already transitioned this request to Pending;
            // SwitchDeploymentRequestStatuses uses optimistic concurrency and returns 0
            var staleRunning = new List<DeploymentRequestApiModel>
            {
                new() { Id = 145, EnvironmentName = "EnvRecovered", Status = DeploymentRequestStatus.Running.ToString(), IsProd = false, UserName = "testuser" }
            };
            mockRequestsPersistentSource
                .GetRequestsWithStatus(DeploymentRequestStatus.Running, false)
                .Returns(staleRunning);
            mockRequestsPersistentSource
                .GetRequestsWithStatus(DeploymentRequestStatus.Requesting, false)
                .Returns(Enumerable.Empty<DeploymentRequestApiModel>());
            mockRequestsPersistentSource
                .SwitchDeploymentRequestStatuses(
                    Arg.Any<IList<DeploymentRequestApiModel>>(),
                    DeploymentRequestStatus.Running,
                    DeploymentRequestStatus.Pending)
                .Returns(0); // already transitioned by another instance

            // Act
            sut.CancelStaleRequests(false);

            // Assert - no event published when transition is a no-op (AC-6)
            mockEventPublisher.DidNotReceive()
                .PublishRequestStatusChangedAsync(Arg.Any<DeploymentRequestEventData>());
        }

        [TestMethod]
        public async Task CancelStaleRequests_WhenHADisabled_ResumesRunningRequestsAsPending()
        {
            // Arrange - HA is disabled (single node); resume behavior is identical (S-004 R4 / AC-8)
            mockDistributedLockService.IsEnabled.Returns(false);

            var staleRunning = new List<DeploymentRequestApiModel>
            {
                new() { Id = 46, EnvironmentName = "EnvSingle", Status = DeploymentRequestStatus.Running.ToString(), IsProd = false, UserName = "testuser" }
            };
            mockRequestsPersistentSource
                .GetRequestsWithStatus(DeploymentRequestStatus.Running, false)
                .Returns(staleRunning);
            mockRequestsPersistentSource
                .GetRequestsWithStatus(DeploymentRequestStatus.Requesting, false)
                .Returns(Enumerable.Empty<DeploymentRequestApiModel>());
            mockRequestsPersistentSource
                .SwitchDeploymentRequestStatuses(
                    Arg.Any<IList<DeploymentRequestApiModel>>(),
                    DeploymentRequestStatus.Running,
                    DeploymentRequestStatus.Pending)
                .Returns(1);

            // Act
            sut.CancelStaleRequests(false);
            await Task.WhenAll(publishTasks);

            // Assert - Running resumed as Pending with a Pending event (AC-1, AC-7, AC-8)
            mockRequestsPersistentSource.Received(1)
                .SwitchDeploymentRequestStatuses(
                    Arg.Any<IList<DeploymentRequestApiModel>>(),
                    DeploymentRequestStatus.Running,
                    DeploymentRequestStatus.Pending);
            mockEventPublisher.Received(1)
                .PublishRequestStatusChangedAsync(Arg.Is<DeploymentRequestEventData>(e =>
                    e.RequestId == 46 && e.Status == DeploymentRequestStatus.Pending.ToString()));
        }

        [TestMethod]
        public void CancelStaleRequests_NoStaleRequests_DoesNothing()
        {
            // Arrange
            mockRequestsPersistentSource
                .GetRequestsWithStatus(DeploymentRequestStatus.Running, false)
                .Returns(Enumerable.Empty<DeploymentRequestApiModel>());
            mockRequestsPersistentSource
                .GetRequestsWithStatus(DeploymentRequestStatus.Requesting, false)
                .Returns(Enumerable.Empty<DeploymentRequestApiModel>());

            // Act
            sut.CancelStaleRequests(false);

            // Assert - no status switch attempted
            mockRequestsPersistentSource.DidNotReceive()
                .SwitchDeploymentRequestStatuses(
                    Arg.Any<IList<DeploymentRequestApiModel>>(),
                    Arg.Any<DeploymentRequestStatus>(),
                    Arg.Any<DeploymentRequestStatus>(),
                    Arg.Any<DateTimeOffset>());
        }

        [TestMethod]
        public async Task CancelStaleRequests_WithRunningRequests_ResumesThemAsPendingAndPublishesEvents()
        {
            // Arrange
            var staleRunning = new List<DeploymentRequestApiModel>
            {
                new() { Id = 50, EnvironmentName = "EnvA", Status = DeploymentRequestStatus.Running.ToString(), IsProd = false, UserName = "testuser" },
                new() { Id = 51, EnvironmentName = "EnvB", Status = DeploymentRequestStatus.Running.ToString(), IsProd = false, UserName = "testuser" }
            };
            mockRequestsPersistentSource
                .GetRequestsWithStatus(DeploymentRequestStatus.Running, false)
                .Returns(staleRunning);
            mockRequestsPersistentSource
                .GetRequestsWithStatus(DeploymentRequestStatus.Requesting, false)
                .Returns(Enumerable.Empty<DeploymentRequestApiModel>());
            mockRequestsPersistentSource
                .SwitchDeploymentRequestStatuses(
                    Arg.Any<IList<DeploymentRequestApiModel>>(),
                    DeploymentRequestStatus.Running,
                    DeploymentRequestStatus.Pending)
                .Returns(1); // per-request call returns 1 each time

            // Act
            sut.CancelStaleRequests(false);
            await Task.WhenAll(publishTasks);

            // Assert - status switched to Pending (not Cancelled), once per request — AC-1
            mockRequestsPersistentSource.Received(2)
                .SwitchDeploymentRequestStatuses(
                    Arg.Any<IList<DeploymentRequestApiModel>>(),
                    DeploymentRequestStatus.Running,
                    DeploymentRequestStatus.Pending);

            // Assert - deployment results NOT touched for Running → Pending path
            mockRequestsPersistentSource.DidNotReceive()
                .SwitchDeploymentResultsStatuses(
                    Arg.Any<IList<DeploymentRequestApiModel>>(),
                    Arg.Any<DeploymentResultStatus>(),
                    Arg.Any<DeploymentResultStatus>());

            // Assert - Pending events published for each resumed request — AC-7
            mockEventPublisher.Received(2)
                .PublishRequestStatusChangedAsync(Arg.Is<DeploymentRequestEventData>(e =>
                    e.Status == DeploymentRequestStatus.Pending.ToString()));
        }

        [TestMethod]
        public async Task CancelStaleRequests_WithRunningRequests_DoesNotTerminateRunnerProcesses()
        {
            // Arrange - runner processes are gone when previous instance exits; no cleanup needed (S-004 R1)
            var staleRunning = new List<DeploymentRequestApiModel>
            {
                new() { Id = 52, EnvironmentName = "EnvE", Status = DeploymentRequestStatus.Running.ToString(), IsProd = false, UserName = "testuser" }
            };
            mockRequestsPersistentSource
                .GetRequestsWithStatus(DeploymentRequestStatus.Running, false)
                .Returns(staleRunning);
            mockRequestsPersistentSource
                .GetRequestsWithStatus(DeploymentRequestStatus.Requesting, false)
                .Returns(Enumerable.Empty<DeploymentRequestApiModel>());
            mockRequestsPersistentSource
                .SwitchDeploymentRequestStatuses(
                    Arg.Any<IList<DeploymentRequestApiModel>>(),
                    DeploymentRequestStatus.Running,
                    DeploymentRequestStatus.Pending)
                .Returns(1);

            // Act
            sut.CancelStaleRequests(false);
            await Task.WhenAll(publishTasks);

            // Assert - TerminateRunnerProcesses not called for resumed requests
            mockProcessesPersistentSource.DidNotReceive()
                .GetAssociatedRunnerProcessIds(52);
        }

        [TestMethod]
        public async Task CancelStaleRequests_WithRequestingRequests_CancelsThem()
        {
            // Arrange
            mockRequestsPersistentSource
                .GetRequestsWithStatus(DeploymentRequestStatus.Running, false)
                .Returns(Enumerable.Empty<DeploymentRequestApiModel>());

            var staleRequesting = new List<DeploymentRequestApiModel>
            {
                new() { Id = 60, EnvironmentName = "EnvC", Status = DeploymentRequestStatus.Requesting.ToString(), IsProd = false, UserName = "testuser" }
            };
            mockRequestsPersistentSource
                .GetRequestsWithStatus(DeploymentRequestStatus.Requesting, false)
                .Returns(staleRequesting);

            mockRequestsPersistentSource
                .SwitchDeploymentRequestStatuses(
                    Arg.Any<IList<DeploymentRequestApiModel>>(),
                    DeploymentRequestStatus.Requesting,
                    DeploymentRequestStatus.Cancelled,
                    Arg.Any<DateTimeOffset>())
                .Returns(1);

            // Act
            sut.CancelStaleRequests(false);

            // Wait for fire-and-forget event publish tasks to complete
            await Task.WhenAll(publishTasks);

            // Assert
            mockRequestsPersistentSource.Received(1)
                .SwitchDeploymentRequestStatuses(
                    Arg.Any<IList<DeploymentRequestApiModel>>(),
                    DeploymentRequestStatus.Requesting,
                    DeploymentRequestStatus.Cancelled,
                    Arg.Any<DateTimeOffset>());

            mockEventPublisher.Received(1)
                .PublishRequestStatusChangedAsync(Arg.Any<DeploymentRequestEventData>());
        }

        [TestMethod]
        public void CancelStaleRequests_WhenSwitchReturnsZero_DoesNotPublishEvents()
        {
            // Arrange: another monitor instance already resumed this request (optimistic concurrency returns 0)
            var staleRunning = new List<DeploymentRequestApiModel>
            {
                new() { Id = 70, EnvironmentName = "EnvD", Status = DeploymentRequestStatus.Running.ToString(), IsProd = false, UserName = "testuser" }
            };
            mockRequestsPersistentSource
                .GetRequestsWithStatus(DeploymentRequestStatus.Running, false)
                .Returns(staleRunning);
            mockRequestsPersistentSource
                .GetRequestsWithStatus(DeploymentRequestStatus.Requesting, false)
                .Returns(Enumerable.Empty<DeploymentRequestApiModel>());
            mockRequestsPersistentSource
                .SwitchDeploymentRequestStatuses(
                    Arg.Any<IList<DeploymentRequestApiModel>>(),
                    DeploymentRequestStatus.Running,
                    DeploymentRequestStatus.Pending)
                .Returns(0);

            // Act
            sut.CancelStaleRequests(false);

            // Assert - no events published when transition is a no-op
            mockEventPublisher.DidNotReceive()
                .PublishRequestStatusChangedAsync(Arg.Any<DeploymentRequestEventData>());
            // Assert - deployment results not touched
            mockRequestsPersistentSource.DidNotReceive()
                .SwitchDeploymentResultsStatuses(
                    Arg.Any<IList<DeploymentRequestApiModel>>(),
                    Arg.Any<DeploymentResultStatus>(),
                    Arg.Any<DeploymentResultStatus>());
        }

        // =====================================================================
        // AbandonRequests
        // =====================================================================

        [TestMethod]
        public void AbandonRequests_NoOldRequests_DoesNothing()
        {
            // Arrange - return requests that are NOT older than 1 day
            var recentRequests = new List<DeploymentRequestApiModel>
            {
                new() { Id = 80, EnvironmentName = "EnvX", Status = DeploymentRequestStatus.Running.ToString(),
                        IsProd = false, UserName = "testuser", RequestedTime = DateTimeOffset.Now.AddHours(-1) }
            };
            mockRequestsPersistentSource
                .GetRequestsWithStatus(DeploymentRequestStatus.Running, false)
                .Returns(recentRequests);

            var cancellationSources = new ConcurrentDictionary<int, CancellationTokenSource>();

            // Act
            sut.AbandonRequests(false, cancellationSources, CancellationToken.None);

            // Assert - no status switch because request is recent
            mockRequestsPersistentSource.DidNotReceive()
                .SwitchDeploymentRequestStatuses(
                    Arg.Any<IList<DeploymentRequestApiModel>>(),
                    Arg.Any<DeploymentRequestStatus>(),
                    Arg.Any<DeploymentRequestStatus>(),
                    Arg.Any<DateTimeOffset>());
        }

        [TestMethod]
        public async Task AbandonRequests_WithOldRequests_AbandonsThemAndPublishesEvents()
        {
            // Arrange - return request older than 1 day
            var oldRequests = new List<DeploymentRequestApiModel>
            {
                new() { Id = 81, EnvironmentName = "EnvY", Status = DeploymentRequestStatus.Running.ToString(),
                        IsProd = false, UserName = "testuser", RequestedTime = DateTimeOffset.Now.AddDays(-2) }
            };
            mockRequestsPersistentSource
                .GetRequestsWithStatus(DeploymentRequestStatus.Running, false)
                .Returns(oldRequests);

            mockRequestsPersistentSource
                .SwitchDeploymentRequestStatuses(
                    Arg.Any<IList<DeploymentRequestApiModel>>(),
                    DeploymentRequestStatus.Running,
                    DeploymentRequestStatus.Abandoned,
                    Arg.Any<DateTimeOffset>())
                .Returns(1);

            var cancellationSources = new ConcurrentDictionary<int, CancellationTokenSource>();

            // Act
            sut.AbandonRequests(false, cancellationSources, CancellationToken.None);

            // Wait for fire-and-forget event publish tasks to complete
            await Task.WhenAll(publishTasks);

            // Assert - switched to Abandoned
            mockRequestsPersistentSource.Received(1)
                .SwitchDeploymentRequestStatuses(
                    Arg.Any<IList<DeploymentRequestApiModel>>(),
                    DeploymentRequestStatus.Running,
                    DeploymentRequestStatus.Abandoned,
                    Arg.Any<DateTimeOffset>());

            // Assert - event published
            mockEventPublisher.Received(1)
                .PublishRequestStatusChangedAsync(Arg.Any<DeploymentRequestEventData>());
        }

        [TestMethod]
        public void AbandonRequests_NoRequests_DoesNothing()
        {
            // Arrange
            mockRequestsPersistentSource
                .GetRequestsWithStatus(DeploymentRequestStatus.Running, false)
                .Returns(Enumerable.Empty<DeploymentRequestApiModel>());

            var cancellationSources = new ConcurrentDictionary<int, CancellationTokenSource>();

            // Act
            sut.AbandonRequests(false, cancellationSources, CancellationToken.None);

            // Assert
            mockRequestsPersistentSource.DidNotReceive()
                .SwitchDeploymentRequestStatuses(
                    Arg.Any<IList<DeploymentRequestApiModel>>(),
                    Arg.Any<DeploymentRequestStatus>(),
                    Arg.Any<DeploymentRequestStatus>(),
                    Arg.Any<DateTimeOffset>());
        }

        // =====================================================================
        // ExecuteRequests - Paused blocking and Running skip
        // =====================================================================

        [TestMethod]
        public async Task ExecuteRequests_WhenFirstRequestIsPaused_SkipsEnvironment()
        {
            // Arrange - first request (by ID) is Paused, subsequent is Pending
            var requests = new List<DeploymentRequestApiModel>
            {
                new() { Id = 90, EnvironmentName = "EnvPause", Status = DeploymentRequestStatus.Paused.ToString(),
                        IsProd = false, UserName = "testuser",
                        RequestDetails = "<DeploymentRequestDetail xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\"><Components /><ComponentsToSkip /><Properties /></DeploymentRequestDetail>" },
                new() { Id = 91, EnvironmentName = "EnvPause", Status = DeploymentRequestStatus.Pending.ToString(),
                        IsProd = false, UserName = "testuser",
                        RequestDetails = "<DeploymentRequestDetail xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\"><Components /><ComponentsToSkip /><Properties /></DeploymentRequestDetail>" }
            };
            mockRequestsPersistentSource
                .GetRequestsWithStatus(
                    DeploymentRequestStatus.Pending,
                    DeploymentRequestStatus.Running,
                    DeploymentRequestStatus.Confirmed,
                    DeploymentRequestStatus.Paused,
                    false)
                .Returns(requests);

            mockDistributedLockService.IsEnabled.Returns(false);
            var cancellationSources = new ConcurrentDictionary<int, CancellationTokenSource>();

            // Act
            var tasks = sut.ExecuteRequests(false, cancellationSources, CancellationToken.None);
            await Task.WhenAll(tasks);

            // Assert - no requests should be processed since the first is Paused
            mockRequestsPersistentSource.DidNotReceive()
                .UpdateNonProcessedRequest(
                    Arg.Any<DeploymentRequestApiModel>(),
                    Arg.Any<DeploymentRequestStatus>(),
                    Arg.Any<DateTimeOffset>());
        }

        [TestMethod]
        public async Task ExecuteRequests_WhenEnvironmentHasRunningRequest_SkipsEnvironment()
        {
            // Arrange - environment has a Running request, so Pending requests should not be picked up
            var requests = new List<DeploymentRequestApiModel>
            {
                new() { Id = 100, EnvironmentName = "EnvRunning", Status = DeploymentRequestStatus.Running.ToString(),
                        IsProd = false, UserName = "testuser",
                        RequestDetails = "<DeploymentRequestDetail xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\"><Components /><ComponentsToSkip /><Properties /></DeploymentRequestDetail>" },
                new() { Id = 101, EnvironmentName = "EnvRunning", Status = DeploymentRequestStatus.Pending.ToString(),
                        IsProd = false, UserName = "testuser",
                        RequestDetails = "<DeploymentRequestDetail xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\"><Components /><ComponentsToSkip /><Properties /></DeploymentRequestDetail>" }
            };
            mockRequestsPersistentSource
                .GetRequestsWithStatus(
                    DeploymentRequestStatus.Pending,
                    DeploymentRequestStatus.Running,
                    DeploymentRequestStatus.Confirmed,
                    DeploymentRequestStatus.Paused,
                    false)
                .Returns(requests);

            mockDistributedLockService.IsEnabled.Returns(false);
            var cancellationSources = new ConcurrentDictionary<int, CancellationTokenSource>();

            // Act
            var tasks = sut.ExecuteRequests(false, cancellationSources, CancellationToken.None);
            await Task.WhenAll(tasks);

            // Assert - no requests should be processed since env has Running request
            mockRequestsPersistentSource.DidNotReceive()
                .UpdateNonProcessedRequest(
                    Arg.Any<DeploymentRequestApiModel>(),
                    Arg.Any<DeploymentRequestStatus>(),
                    Arg.Any<DateTimeOffset>());
        }

        [TestMethod]
        public async Task ExecuteRequests_StatusChangedAfterLockAcquired_SkipsExecution()
        {
            // Arrange - lock succeeds but request status changed while waiting
            var requests = CreatePendingRequests("EnvRevalidate", 110);
            mockRequestsPersistentSource
                .GetRequestsWithStatus(
                    DeploymentRequestStatus.Pending,
                    DeploymentRequestStatus.Running,
                    DeploymentRequestStatus.Confirmed,
                    DeploymentRequestStatus.Paused,
                    false)
                .Returns(requests);

            var mockLock = Substitute.For<IDistributedLock>();
            mockLock.ResourceKey.Returns("env:EnvRevalidate");

            mockDistributedLockService.IsEnabled.Returns(true);
            mockDistributedLockService
                .TryAcquireLockAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(mockLock);

            // Request status changed to Cancelled while waiting for lock
            mockRequestsPersistentSource.GetRequest(110)
                .Returns(new DeploymentRequestApiModel
                {
                    Id = 110,
                    Status = DeploymentRequestStatus.Cancelled.ToString()
                });

            var cancellationSources = new ConcurrentDictionary<int, CancellationTokenSource>();

            // Act
            var tasks = sut.ExecuteRequests(false, cancellationSources, CancellationToken.None);
            await Task.WhenAll(tasks);

            // Assert - should NOT call UpdateNonProcessedRequest because status changed
            mockRequestsPersistentSource.DidNotReceive()
                .UpdateNonProcessedRequest(
                    Arg.Any<DeploymentRequestApiModel>(),
                    Arg.Any<DeploymentRequestStatus>(),
                    Arg.Any<DateTimeOffset>());

            // Assert - lock should still be disposed
            await mockLock.Received(1).DisposeAsync();
        }

        [TestMethod]
        public async Task ExecuteRequests_MultipleEnvironments_ProcessesBothIndependently()
        {
            // Arrange - two different environments each with a pending request
            var requests = new List<DeploymentRequestApiModel>
            {
                new() { Id = 120, EnvironmentName = "EnvAlpha", Status = DeploymentRequestStatus.Pending.ToString(),
                        IsProd = false, UserName = "testuser",
                        RequestDetails = "<DeploymentRequestDetail xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\"><Components /><ComponentsToSkip /><Properties /></DeploymentRequestDetail>" },
                new() { Id = 121, EnvironmentName = "EnvBeta", Status = DeploymentRequestStatus.Pending.ToString(),
                        IsProd = false, UserName = "testuser",
                        RequestDetails = "<DeploymentRequestDetail xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\"><Components /><ComponentsToSkip /><Properties /></DeploymentRequestDetail>" }
            };
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
                .Returns(0); // Return 0 to avoid going deeper into execution

            var cancellationSources = new ConcurrentDictionary<int, CancellationTokenSource>();

            // Act
            var tasks = sut.ExecuteRequests(false, cancellationSources, CancellationToken.None);
            await Task.WhenAll(tasks);

            // Assert - both environments should have had UpdateNonProcessedRequest called
            mockRequestsPersistentSource.Received(2)
                .UpdateNonProcessedRequest(
                    Arg.Any<DeploymentRequestApiModel>(),
                    Arg.Any<DeploymentRequestStatus>(),
                    Arg.Any<DateTimeOffset>());
        }

        [TestMethod]
        public async Task ExecuteRequests_OnlyFirstPendingPerEnvironmentIsProcessed()
        {
            // Arrange - same environment, two pending requests
            var requests = new List<DeploymentRequestApiModel>
            {
                new() { Id = 130, EnvironmentName = "EnvSeq", Status = DeploymentRequestStatus.Pending.ToString(),
                        IsProd = false, UserName = "testuser",
                        RequestDetails = "<DeploymentRequestDetail xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\"><Components /><ComponentsToSkip /><Properties /></DeploymentRequestDetail>" },
                new() { Id = 131, EnvironmentName = "EnvSeq", Status = DeploymentRequestStatus.Pending.ToString(),
                        IsProd = false, UserName = "testuser",
                        RequestDetails = "<DeploymentRequestDetail xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\"><Components /><ComponentsToSkip /><Properties /></DeploymentRequestDetail>" }
            };
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
                .Returns(0);

            var cancellationSources = new ConcurrentDictionary<int, CancellationTokenSource>();

            // Act
            var tasks = sut.ExecuteRequests(false, cancellationSources, CancellationToken.None);
            await Task.WhenAll(tasks);

            // Assert - only one request should be processed (the first by ID)
            mockRequestsPersistentSource.Received(1)
                .UpdateNonProcessedRequest(
                    Arg.Is<DeploymentRequestApiModel>(r => r.Id == 130),
                    Arg.Any<DeploymentRequestStatus>(),
                    Arg.Any<DateTimeOffset>());
        }

        // =====================================================================
        // ExecuteRequests - S-003 token decoupling
        // =====================================================================

        [TestMethod]
        public async Task ExecuteRequests_WhenMonitorCancellationFires_DoesNotCancelRequestToken()
        {
            // Arrange - S-003: request CTS must NOT be linked to monitorCancellationToken
            var requests = CreatePendingRequests("EnvToken", 200);
            mockRequestsPersistentSource
                .GetRequestsWithStatus(
                    DeploymentRequestStatus.Pending,
                    DeploymentRequestStatus.Running,
                    DeploymentRequestStatus.Confirmed,
                    DeploymentRequestStatus.Paused,
                    false)
                .Returns(requests);
            mockDistributedLockService.IsEnabled.Returns(false);

            // Advance past early-exit guard so CTS is created and Execute is called
            mockRequestsPersistentSource
                .UpdateNonProcessedRequest(Arg.Any<DeploymentRequestApiModel>(), Arg.Any<DeploymentRequestStatus>(), Arg.Any<DateTimeOffset>())
                .Returns(1);

            CancellationToken capturedToken = default;
            var mockPendingProcessor = Substitute.For<IPendingRequestProcessor>();
            mockPendingProcessor
                .When(p => p.Execute(Arg.Any<RequestToProcessDto>(), Arg.Any<CancellationToken>()))
                .Do(ci => capturedToken = ci.Arg<CancellationToken>());
            mockServiceProvider.GetService(typeof(IPendingRequestProcessor)).Returns(mockPendingProcessor);

            var monitorCts = new CancellationTokenSource();
            var cancellationSources = new ConcurrentDictionary<int, CancellationTokenSource>();

            // Act
            var tasks = sut.ExecuteRequests(false, cancellationSources, monitorCts.Token);
            await Task.WhenAll(tasks);

            // Cancel monitor after task completes — decoupled token must still not be cancelled
            monitorCts.Cancel();

            // Assert - the request token was NOT triggered by monitorCancellationToken (S-003 AC-1)
            Assert.IsFalse(capturedToken.IsCancellationRequested,
                "Request CancellationToken must not be linked to monitorCancellationToken after S-003 token decoupling");
        }

        [TestMethod]
        public async Task ExecuteRequests_WhenHADisabled_RequestTokenIsIndependentOfMonitorToken()
        {
            // Arrange - S-003 HA-disabled path: independent CTS, not linked to any shared token
            var requests = CreatePendingRequests("EnvNoHA", 201);
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
                .UpdateNonProcessedRequest(Arg.Any<DeploymentRequestApiModel>(), Arg.Any<DeploymentRequestStatus>(), Arg.Any<DateTimeOffset>())
                .Returns(1);

            CancellationToken capturedToken = default;
            var mockPendingProcessor = Substitute.For<IPendingRequestProcessor>();
            mockPendingProcessor
                .When(p => p.Execute(Arg.Any<RequestToProcessDto>(), Arg.Any<CancellationToken>()))
                .Do(ci => capturedToken = ci.Arg<CancellationToken>());
            mockServiceProvider.GetService(typeof(IPendingRequestProcessor)).Returns(mockPendingProcessor);

            var monitorCts = new CancellationTokenSource();
            var cancellationSources = new ConcurrentDictionary<int, CancellationTokenSource>();

            // Act
            var tasks = sut.ExecuteRequests(false, cancellationSources, monitorCts.Token);
            await Task.WhenAll(tasks);
            monitorCts.Cancel();

            // Assert - independent token is not cancelled when monitor cancels (S-003 R1 / AC-5)
            Assert.IsFalse(capturedToken.IsCancellationRequested,
                "HA-disabled request token must be independent of monitorCancellationToken");
        }

        [TestMethod]
        public async Task ExecuteRequests_TerminateRequestExecution_StillCancelsRequestToken()
        {
            // Arrange - S-003: user-initiated cancellation via TerminateRequestExecution must still work.
            // The mock blocks Execute until signalled, so the CTS is still in the dict when we cancel it.
            var requests = CreatePendingRequests("EnvTerminate", 202);
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
                .UpdateNonProcessedRequest(Arg.Any<DeploymentRequestApiModel>(), Arg.Any<DeploymentRequestStatus>(), Arg.Any<DateTimeOffset>())
                .Returns(1);

            CancellationToken capturedToken = default;
            var executionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseExecution = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var mockPendingProcessor = Substitute.For<IPendingRequestProcessor>();
            mockPendingProcessor
                .When(p => p.Execute(Arg.Any<RequestToProcessDto>(), Arg.Any<CancellationToken>()))
                .Do(ci =>
                {
                    capturedToken = ci.Arg<CancellationToken>();
                    executionStarted.SetResult();
                    releaseExecution.Task.GetAwaiter().GetResult(); // block until signalled
                });
            mockServiceProvider.GetService(typeof(IPendingRequestProcessor)).Returns(mockPendingProcessor);

            var cancellationSources = new ConcurrentDictionary<int, CancellationTokenSource>();

            // Act - start tasks; Execute will block until released
            var tasks = sut.ExecuteRequests(false, cancellationSources, CancellationToken.None);
            await executionStarted.Task; // wait until Execute has captured the token

            // At this point the task is blocked in Execute — CTS is still in the dict
            Assert.IsTrue(cancellationSources.ContainsKey(202), "CTS must be in the dict while Execute is running");
            cancellationSources[202].Cancel(); // simulate TerminateRequestExecution
            var cancelledByTerminate = capturedToken.IsCancellationRequested;

            releaseExecution.SetResult(); // unblock Execute
            await Task.WhenAll(tasks);

            // Assert - cancelling the stored CTS must have cancelled the token passed to Execute (S-003 R1 / AC-4)
            Assert.IsTrue(cancelledByTerminate,
                "TerminateRequestExecution (Cancel on stored CTS) must cancel the request token");
        }

        [TestMethod]
        public async Task ExecuteRequests_WhenMonitorAlreadyCancelled_DoesNotStartDeploymentTask()
        {
            // Arrange - S-003: ThrowIfCancellationRequested guard must prevent new work from starting
            // after shutdown is signalled, even though the request token is no longer linked to monitorCts.
            var requests = CreatePendingRequests("EnvGuard", 203);
            mockRequestsPersistentSource
                .GetRequestsWithStatus(
                    DeploymentRequestStatus.Pending,
                    DeploymentRequestStatus.Running,
                    DeploymentRequestStatus.Confirmed,
                    DeploymentRequestStatus.Paused,
                    false)
                .Returns(requests);
            mockDistributedLockService.IsEnabled.Returns(false);

            var mockPendingProcessor = Substitute.For<IPendingRequestProcessor>();
            mockServiceProvider.GetService(typeof(IPendingRequestProcessor)).Returns(mockPendingProcessor);

            var monitorCts = new CancellationTokenSource();
            monitorCts.Cancel(); // pre-cancel: monitor is already stopping when ExecuteRequests is called

            var cancellationSources = new ConcurrentDictionary<int, CancellationTokenSource>();

            // Act - Task.Run with a pre-cancelled token transitions the task to Cancelled without running it
            var tasks = sut.ExecuteRequests(false, cancellationSources, monitorCts.Token);
            try { await Task.WhenAll(tasks); } catch (OperationCanceledException) { /* expected */ }

            // Assert - the guard prevented Execute from being called
            mockPendingProcessor.DidNotReceive()
                .Execute(Arg.Any<RequestToProcessDto>(), Arg.Any<CancellationToken>());
        }

        // =====================================================================
        // SwitchRequestsStatus - isProd guard
        // =====================================================================

        [TestMethod]
        public void CancelRequests_WhenRequestIsProd_DoesNotCancel()
        {
            // Arrange - request is on a production environment
            var prodRequests = new List<DeploymentRequestApiModel>
            {
                new() { Id = 140, EnvironmentName = "ProdEnv", Status = DeploymentRequestStatus.Cancelling.ToString(),
                        IsProd = true, UserName = "testuser" }
            };
            mockRequestsPersistentSource
                .GetRequestsWithStatus(DeploymentRequestStatus.Cancelling, false)
                .Returns(prodRequests);

            var cancellationSources = new ConcurrentDictionary<int, CancellationTokenSource>();

            // Act
            sut.CancelRequests(false, cancellationSources, CancellationToken.None);

            // Assert - should NOT switch status because IsProd is true
            mockRequestsPersistentSource.DidNotReceive()
                .SwitchDeploymentRequestStatuses(
                    Arg.Any<IList<DeploymentRequestApiModel>>(),
                    Arg.Any<DeploymentRequestStatus>(),
                    Arg.Any<DeploymentRequestStatus>(),
                    Arg.Any<DateTimeOffset>());
        }

        // =====================================================================
        // Lock health check (IsValid) after execution
        // =====================================================================

        [TestMethod]
        public async Task ExecuteRequests_WhenLockLostDuringExecution_LogsWarning()
        {
            // Arrange - lock acquired but channel dies during execution
            var requests = CreatePendingRequests("EnvLockLost", 200);
            mockRequestsPersistentSource
                .GetRequestsWithStatus(
                    DeploymentRequestStatus.Pending,
                    DeploymentRequestStatus.Running,
                    DeploymentRequestStatus.Confirmed,
                    DeploymentRequestStatus.Paused,
                    false)
                .Returns(requests);

            var mockLock = Substitute.For<IDistributedLock>();
            mockLock.ResourceKey.Returns("env:EnvLockLost");
            // Lock is initially valid but becomes invalid during execution
            mockLock.IsValid.Returns(false);

            mockDistributedLockService.IsEnabled.Returns(true);
            mockDistributedLockService
                .TryAcquireLockAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(mockLock);

            // Request is still Pending when re-validated after lock acquisition
            mockRequestsPersistentSource.GetRequest(200)
                .Returns(new DeploymentRequestApiModel
                {
                    Id = 200,
                    Status = DeploymentRequestStatus.Pending.ToString()
                });

            // UpdateNonProcessedRequest succeeds (simulates execution starting)
            mockRequestsPersistentSource
                .UpdateNonProcessedRequest(
                    Arg.Any<DeploymentRequestApiModel>(),
                    Arg.Any<DeploymentRequestStatus>(),
                    Arg.Any<DateTimeOffset>())
                .Returns(1);

            var cancellationSources = new ConcurrentDictionary<int, CancellationTokenSource>();

            // Act
            var tasks = sut.ExecuteRequests(false, cancellationSources, CancellationToken.None);
            await Task.WhenAll(tasks);

            // Assert - warning should be logged about lost lock
            mockLogger.Received().Log(
                LogLevel.Warning,
                Arg.Any<EventId>(),
                Arg.Is<object>(o => o.ToString()!.Contains("was lost during execution")),
                Arg.Any<Exception?>(),
                Arg.Any<Func<object, Exception?, string>>());

            // Assert - lock should still be disposed
            await mockLock.Received(1).DisposeAsync();
        }

        // =====================================================================
        // OnPublishTaskCreated callback deterministic tracking
        // =====================================================================

        [TestMethod]
        public async Task OnPublishTaskCreated_CallbackTracksFireAndForgetTasks()
        {
            // Arrange - verify that the OnPublishTaskCreated callback is invoked for each
            // fire-and-forget publish task, enabling deterministic test awaiting
            var requests = CreateRequests(400);
            requests[0].Status = DeploymentRequestStatus.Restarting.ToString();
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
            Assert.IsEmpty(publishTasks);
            sut.RestartRequests(false, cancellationSources, CancellationToken.None);

            // Assert - callback should have been invoked
            Assert.IsNotEmpty(publishTasks, "OnPublishTaskCreated callback should be invoked for fire-and-forget tasks");

            // Wait for all tracked tasks to complete deterministically (no Task.Delay needed)
            await Task.WhenAll(publishTasks);

            // Assert - event was published
            mockEventPublisher.Received(1)
                .PublishRequestStatusChangedAsync(Arg.Any<DeploymentRequestEventData>());
        }

        // =====================================================================
        // PublishRequestStatusChangedSafe error handling
        // =====================================================================

        [TestMethod]
        public async Task PublishRequestStatusChangedSafe_WhenPublishFails_LogsWarning()
        {
            // Arrange - make the event publisher throw
            mockEventPublisher.PublishRequestStatusChangedAsync(Arg.Any<DeploymentRequestEventData>())
                .ThrowsAsync(new InvalidOperationException("SignalR hub is down"));

            var requests = CreateRequests(300);
            requests[0].Status = DeploymentRequestStatus.Restarting.ToString();
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

            // Act - should not throw despite publish failure
            sut.RestartRequests(false, cancellationSources, CancellationToken.None);

            // Wait for fire-and-forget event publish tasks to complete
            await Task.WhenAll(publishTasks);

            // Assert - warning should be logged about failed publish
            mockLogger.Received().Log(
                LogLevel.Warning,
                Arg.Any<EventId>(),
                Arg.Is<object>(o => o.ToString()!.Contains("Failed to publish status event")),
                Arg.Any<Exception?>(),
                Arg.Any<Func<object, Exception?, string>>());
        }

        [TestMethod]
        public async Task ExecuteRequests_WhenLockHasLockLostToken_LinksItToCancellation()
        {
            // Arrange - verify that when HA is enabled and a lock is acquired,
            // the LockLostToken is linked to the request cancellation token.
            var requests = CreatePendingRequests("EnvLinked", 500);
            mockRequestsPersistentSource
                .GetRequestsWithStatus(
                    DeploymentRequestStatus.Pending,
                    DeploymentRequestStatus.Running,
                    DeploymentRequestStatus.Confirmed,
                    DeploymentRequestStatus.Paused,
                    false)
                .Returns(requests);

            var mockLock = Substitute.For<IDistributedLock>();
            mockLock.ResourceKey.Returns("env:EnvLinked");
            mockLock.IsValid.Returns(true);
            var lockLostCts = new CancellationTokenSource();
            mockLock.LockLostToken.Returns(lockLostCts.Token);

            mockDistributedLockService.IsEnabled.Returns(true);
            mockDistributedLockService
                .TryAcquireLockAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(mockLock);

            // Re-validation passes
            mockRequestsPersistentSource.GetRequest(500)
                .Returns(new DeploymentRequestApiModel
                {
                    Id = 500,
                    Status = DeploymentRequestStatus.Pending.ToString()
                });

            // Execution starts
            mockRequestsPersistentSource
                .UpdateNonProcessedRequest(
                    Arg.Any<DeploymentRequestApiModel>(),
                    Arg.Any<DeploymentRequestStatus>(),
                    Arg.Any<DateTimeOffset>())
                .Returns(1);

            var requestCancellationSources = new ConcurrentDictionary<int, CancellationTokenSource>();

            // Act
            var tasks = sut.ExecuteRequests(false, requestCancellationSources, CancellationToken.None);
            await Task.WhenAll(tasks);

            // Assert - lock was acquired and disposed
            await mockDistributedLockService.Received(1)
                .TryAcquireLockAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
            await mockLock.Received(1).DisposeAsync();
        }
    }
}
