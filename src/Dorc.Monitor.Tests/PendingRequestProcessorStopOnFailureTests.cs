using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core;
using Dorc.Core.Events;
using Dorc.Core.Interfaces;
using Dorc.Core.VariableResolution;
using Dorc.Monitor.Notifications;
using Dorc.Monitor.RequestProcessors;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Dorc.Monitor.Tests
{
    [TestClass]
    public class PendingRequestProcessorStopOnFailureTests
    {
        private ILoggerFactory mockLoggerFactory = null!;
        private IComponentProcessor mockComponentProcessor = null!;
        private IVariableScopeOptionsResolver mockVariableScopeOptionsResolver = null!;
        private IRequestsPersistentSource mockRequestsPersistentSource = null!;
        private IPropertyValuesPersistentSource mockPropertyValuesPersistentSource = null!;
        private IEnvironmentsPersistentSource mockEnvironmentsPersistentSource = null!;
        private IManageProjectsPersistentSource mockManageProjectsPersistentSource = null!;
        private IConfigValuesPersistentSource mockConfigValuesPersistentSource = null!;
        private IPropertyEvaluator mockPropertyEvaluator = null!;
        private IDeploymentEventsPublisher mockEventsPublisher = null!;
        private IGitHubArtifactDownloader mockGitHubArtifactDownloader = null!;
        private IDeploymentNotificationSink mockNotificationSink = null!;
        private IComponentsPersistentSource mockComponentsPersistentSource = null!;

        private PendingRequestProcessor sut = null!;

        [TestInitialize]
        public void Setup()
        {
            mockLoggerFactory = Substitute.For<ILoggerFactory>();
            mockLoggerFactory.CreateLogger(Arg.Any<string>()).Returns(Substitute.For<ILogger>());

            mockComponentProcessor = Substitute.For<IComponentProcessor>();
            mockVariableScopeOptionsResolver = Substitute.For<IVariableScopeOptionsResolver>();
            mockRequestsPersistentSource = Substitute.For<IRequestsPersistentSource>();
            mockRequestsPersistentSource.SetRequestStartStatus(
                Arg.Any<DeploymentRequestApiModel>(), Arg.Any<DeploymentRequestStatus>(), Arg.Any<DateTimeOffset>())
                .Returns(true);
            mockPropertyValuesPersistentSource = Substitute.For<IPropertyValuesPersistentSource>();
            mockEnvironmentsPersistentSource = Substitute.For<IEnvironmentsPersistentSource>();
            mockManageProjectsPersistentSource = Substitute.For<IManageProjectsPersistentSource>();
            mockConfigValuesPersistentSource = Substitute.For<IConfigValuesPersistentSource>();
            mockPropertyEvaluator = Substitute.For<IPropertyEvaluator>();
            mockEventsPublisher = Substitute.For<IDeploymentEventsPublisher>();
            mockGitHubArtifactDownloader = Substitute.For<IGitHubArtifactDownloader>();
            mockNotificationSink = Substitute.For<IDeploymentNotificationSink>();
            mockComponentsPersistentSource = Substitute.For<IComponentsPersistentSource>();
            mockGitHubArtifactDownloader.IsGitHubArtifactUrl(Arg.Any<string>()).Returns(false);

            // Common mock setup
            mockConfigValuesPersistentSource.GetConfigValue("ScriptRoot", Arg.Any<string>())
                .Returns("C:\\Scripts");
            mockConfigValuesPersistentSource.GetConfigValue("ScriptRoot")
                .Returns("C:\\Scripts");
            mockConfigValuesPersistentSource.GetConfigValue("DeploymentLogDir", Arg.Any<string>())
                .Returns("C:\\Logs");
            mockConfigValuesPersistentSource.GetAllConfigValues(true)
                .Returns(Enumerable.Empty<ConfigValueApiModel>());

            mockEnvironmentsPersistentSource.GetEnvironment(Arg.Any<string>())
                .Returns(new EnvironmentApiModel
                {
                    EnvironmentId = 1,
                    EnvironmentName = "TestEnv",
                    EnvironmentIsProd = false,
                    EnvironmentSecure = false
                });

            mockPropertyValuesPersistentSource.LoadAllPropertiesIntoCache()
                .Returns(new Dictionary<string, PropertyValueDto>());

            mockEventsPublisher.PublishRequestStatusChangedAsync(Arg.Any<DeploymentRequestEventData>())
                .Returns(Task.CompletedTask);
            mockEventsPublisher.PublishResultStatusChangedAsync(Arg.Any<DeploymentResultEventData>())
                .Returns(Task.CompletedTask);

            sut = new PendingRequestProcessor(
                mockLoggerFactory,
                mockComponentProcessor,
                mockVariableScopeOptionsResolver,
                mockRequestsPersistentSource,
                mockPropertyValuesPersistentSource,
                mockEnvironmentsPersistentSource,
                mockManageProjectsPersistentSource,
                mockConfigValuesPersistentSource,
                mockPropertyEvaluator,
                mockEventsPublisher,
                mockNotificationSink,
                mockGitHubArtifactDownloader,
                mockComponentsPersistentSource);
        }

        private static RequestToProcessDto CreateRequest(List<ComponentApiModel> components)
        {
            var request = new DeploymentRequestApiModel
            {
                Id = 100,
                EnvironmentName = "TestEnv",
                IsProd = false,
                BuildNumber = "1.0.0",
                RequestDetails = "<details>"
            };

            var detail = new DeploymentRequestDetail
            {
                EnvironmentName = "TestEnv",
                Components = components.Select(c => c.ComponentName).ToList(),
                BuildDetail = new BuildDetail { DropLocation = "C:\\Drop" }
            };

            return new RequestToProcessDto(request, detail);
        }

        private void SetupOrderedComponents(List<ComponentApiModel> components)
        {
            mockRequestsPersistentSource
                .GetSubmittedComponents(Arg.Any<int>())
                .Returns(components);

            // Each component has a deployment result
            var results = components.Select(c => new DeploymentResultApiModel
            {
                Id = c.ComponentId!.Value,
                ComponentId = c.ComponentId!.Value,
                RequestId = 100,
                Status = DeploymentResultStatus.Pending.ToString()
            }).ToList();

            mockRequestsPersistentSource
                .GetDeploymentResultsForRequest(100)
                .Returns(results);
        }

        [TestMethod]
        public void Execute_ComponentFailsWithStopOnFailure_StopsRemainingComponents()
        {
            // Arrange
            var comp1 = new ComponentApiModel { ComponentId = 1, ComponentName = "Comp1", IsEnabled = true, StopOnFailure = true };
            var comp2 = new ComponentApiModel { ComponentId = 2, ComponentName = "Comp2", IsEnabled = true, StopOnFailure = false };
            var components = new List<ComponentApiModel> { comp1, comp2 };

            var dto = CreateRequest(components);
            SetupOrderedComponents(components);

            // comp1 returns false (failure)
            mockComponentProcessor.DeployComponent(
                comp1, Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<bool>(),
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IDictionary<string, VariableValue>>(), Arg.Any<CancellationToken>())
                .Returns(false);

            // comp2 should never be called
            mockComponentProcessor.DeployComponent(
                comp2, Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<bool>(),
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IDictionary<string, VariableValue>>(), Arg.Any<CancellationToken>())
                .Returns(true);

            // Act
            sut.Execute(dto, CancellationToken.None, mockLoggerFactory);

            // Assert - comp2 was never deployed because comp1 had StopOnFailure=true
            mockComponentProcessor.DidNotReceive().DeployComponent(
                comp2, Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<bool>(),
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IDictionary<string, VariableValue>>(), Arg.Any<CancellationToken>());

            // Assert - request marked as Failed
            mockRequestsPersistentSource.Received().SetRequestCompletionStatus(
                100,
                DeploymentRequestStatus.Failed,
                Arg.Any<DateTimeOffset>(),
                Arg.Any<string?>());
        }

        [TestMethod]
        public void Execute_ComponentFailsWithoutStopOnFailure_ContinuesRemainingComponents()
        {
            // Arrange
            var comp1 = new ComponentApiModel { ComponentId = 1, ComponentName = "Comp1", IsEnabled = true, StopOnFailure = false };
            var comp2 = new ComponentApiModel { ComponentId = 2, ComponentName = "Comp2", IsEnabled = true, StopOnFailure = false };
            var components = new List<ComponentApiModel> { comp1, comp2 };

            var dto = CreateRequest(components);
            SetupOrderedComponents(components);

            // comp1 returns false (failure), comp2 returns true (success)
            mockComponentProcessor.DeployComponent(
                comp1, Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<bool>(),
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IDictionary<string, VariableValue>>(), Arg.Any<CancellationToken>())
                .Returns(false);

            mockComponentProcessor.DeployComponent(
                comp2, Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<bool>(),
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IDictionary<string, VariableValue>>(), Arg.Any<CancellationToken>())
                .Returns(true);

            // Act
            sut.Execute(dto, CancellationToken.None, mockLoggerFactory);

            // Assert - comp2 WAS deployed because StopOnFailure=false
            mockComponentProcessor.Received().DeployComponent(
                comp2, Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<bool>(),
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IDictionary<string, VariableValue>>(), Arg.Any<CancellationToken>());
        }

        [TestMethod]
        public void Execute_ComponentFailsWithStopOnFailure_CancelsPendingResults()
        {
            // Arrange
            var comp1 = new ComponentApiModel { ComponentId = 1, ComponentName = "Comp1", IsEnabled = true, StopOnFailure = true };
            var comp2 = new ComponentApiModel { ComponentId = 2, ComponentName = "Comp2", IsEnabled = true, StopOnFailure = false };
            var components = new List<ComponentApiModel> { comp1, comp2 };

            var dto = CreateRequest(components);
            SetupOrderedComponents(components);

            var pendingResult = new DeploymentResultApiModel
            {
                Id = 2,
                ComponentId = 2,
                RequestId = 100,
                Status = DeploymentResultStatus.Pending.ToString()
            };

            // After the loop, GetDeploymentResultsForRequest is called again to find pending results
            mockRequestsPersistentSource
                .GetDeploymentResultsForRequest(100)
                .Returns(new List<DeploymentResultApiModel> { pendingResult });

            mockComponentProcessor.DeployComponent(
                comp1, Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<bool>(),
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IDictionary<string, VariableValue>>(), Arg.Any<CancellationToken>())
                .Returns(false);

            // Act
            sut.Execute(dto, CancellationToken.None, mockLoggerFactory);

            // Assert - pending result for comp2 was cancelled
            mockRequestsPersistentSource.Received().UpdateResultStatus(
                pendingResult,
                Arg.Is<DeploymentResultStatus>(s => s.Value == DeploymentResultStatus.Cancelled.Value));
        }

        [TestMethod]
        public void Execute_ComponentThrowsWithStopOnFailure_StopsRemainingComponents()
        {
            // Arrange - test the exception path still works
            var comp1 = new ComponentApiModel { ComponentId = 1, ComponentName = "Comp1", IsEnabled = true, StopOnFailure = true };
            var comp2 = new ComponentApiModel { ComponentId = 2, ComponentName = "Comp2", IsEnabled = true, StopOnFailure = false };
            var components = new List<ComponentApiModel> { comp1, comp2 };

            var dto = CreateRequest(components);
            SetupOrderedComponents(components);

            // comp1 throws an exception
            mockComponentProcessor.DeployComponent(
                comp1, Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<bool>(),
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IDictionary<string, VariableValue>>(), Arg.Any<CancellationToken>())
                .Returns(x => throw new InvalidOperationException("Script failed"));

            // Act
            sut.Execute(dto, CancellationToken.None, mockLoggerFactory);

            // Assert - comp2 was never deployed
            mockComponentProcessor.DidNotReceive().DeployComponent(
                comp2, Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<bool>(),
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IDictionary<string, VariableValue>>(), Arg.Any<CancellationToken>());
        }

        // -------- GitHub artifact cleanup path --------

        [TestMethod]
        public void Execute_ConfirmationResume_DoesNotDispatchUnapprovedOrCompletedSiblings()
        {
            var components = new List<ComponentApiModel>
            {
                new() { ComponentId = 1, ComponentName = "Approved", IsEnabled = true, ComponentType = ComponentType.Terraform },
                new() { ComponentId = 2, ComponentName = "Unapproved", IsEnabled = true, ComponentType = ComponentType.Terraform },
                new() { ComponentId = 3, ComponentName = "AlreadyComplete", IsEnabled = true, ComponentType = ComponentType.PowerShell }
            };
            SetupOrderedComponents(components);
            mockRequestsPersistentSource.GetDeploymentResultsForRequest(100).Returns(new[]
            {
                new DeploymentResultApiModel { Id = 1, ComponentId = 1, RequestId = 100, Status = DeploymentResultStatus.Confirmed.ToString() },
                new DeploymentResultApiModel { Id = 2, ComponentId = 2, RequestId = 100, Status = DeploymentResultStatus.WaitingConfirmation.ToString() },
                new DeploymentResultApiModel { Id = 3, ComponentId = 3, RequestId = 100, Status = DeploymentResultStatus.Complete.ToString() }
            });
            mockComponentProcessor.DeployComponent(
                components[0], Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<bool>(),
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IDictionary<string, VariableValue>>(), Arg.Any<CancellationToken>()).Returns(true);

            sut.Execute(CreateRequest(components), CancellationToken.None, mockLoggerFactory);

            mockComponentProcessor.Received(1).DeployComponent(
                components[0], Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<bool>(),
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IDictionary<string, VariableValue>>(), Arg.Any<CancellationToken>());
            Assert.AreEqual(1, mockComponentProcessor.ReceivedCalls()
                .Count(call => call.GetMethodInfo().Name == nameof(IComponentProcessor.DeployComponent)));
            mockRequestsPersistentSource.Received().SetRequestCompletionStatus(
                100, DeploymentRequestStatus.WaitingConfirmation, Arg.Any<DateTimeOffset>(), Arg.Any<string?>());
        }

        [TestMethod]
        public void Execute_WhenRequestChangedBeforeStart_DoesNotDispatchOrOverwriteStatus()
        {
            mockRequestsPersistentSource.SetRequestStartStatus(
                Arg.Any<DeploymentRequestApiModel>(), Arg.Any<DeploymentRequestStatus>(), Arg.Any<DateTimeOffset>())
                .Returns(false);
            var components = new List<ComponentApiModel>
            {
                new() { ComponentId = 1, ComponentName = "Cancelled", IsEnabled = true }
            };
            SetupOrderedComponents(components);

            sut.Execute(CreateRequest(components), CancellationToken.None, mockLoggerFactory);

            Assert.IsFalse(mockComponentProcessor.ReceivedCalls().Any());
            mockRequestsPersistentSource.DidNotReceiveWithAnyArgs()
                .SetRequestCompletionStatus(default, default, default, default);
        }

        [TestMethod]
        public void Execute_WithGitHubArtifactDropLocation_CleansUpResolvedPathAfterDeployment()
        {
            // Arrange
            var artifactUrl = "https://api.github.com/repos/sefe/dorc-rabbitmq-installer/actions/artifacts/1234/zip";
            var resolvedLocalPath = Path.Join(Path.GetTempPath(), "dorc-artifacts", Guid.NewGuid().ToString("N"));

            mockGitHubArtifactDownloader.IsGitHubArtifactUrl(artifactUrl).Returns(true);
            mockGitHubArtifactDownloader.DownloadAndExtract(artifactUrl).Returns(resolvedLocalPath);

            var comp1 = new ComponentApiModel { ComponentId = 1, ComponentName = "Comp1", IsEnabled = true };
            var components = new List<ComponentApiModel> { comp1 };
            SetupOrderedComponents(components);
            mockComponentProcessor.DeployComponent(
                comp1, Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<bool>(),
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IDictionary<string, VariableValue>>(), Arg.Any<CancellationToken>())
                .Returns(true);

            var dto = new RequestToProcessDto(
                new DeploymentRequestApiModel
                {
                    Id = 100,
                    EnvironmentName = "TestEnv",
                    IsProd = false,
                    BuildNumber = "1.0.0",
                    RequestDetails = "<details>"
                },
                new DeploymentRequestDetail
                {
                    EnvironmentName = "TestEnv",
                    Components = new List<string> { "Comp1" },
                    BuildDetail = new BuildDetail { DropLocation = artifactUrl }
                });

            // Act
            sut.Execute(dto, CancellationToken.None, mockLoggerFactory);

            // Assert — the downloader was consulted for the URL, extraction
            // returned our fake path, and the finally block cleaned it up
            // exactly once using that exact path.
            mockGitHubArtifactDownloader.Received(1).DownloadAndExtract(artifactUrl);
            mockGitHubArtifactDownloader.Received(1).Cleanup(resolvedLocalPath);
        }

        [TestMethod]
        public void Execute_WithGitHubArtifactDropLocation_CleansUpEvenWhenDeploymentFails()
        {
            // The cleanup guarantee MUST hold even on a failed deployment —
            // that's the whole point of having it in the finally block.
            var artifactUrl = "https://api.github.com/repos/o/r/actions/artifacts/9999/zip";
            var resolvedLocalPath = Path.Join(Path.GetTempPath(), "dorc-artifacts", Guid.NewGuid().ToString("N"));

            mockGitHubArtifactDownloader.IsGitHubArtifactUrl(artifactUrl).Returns(true);
            mockGitHubArtifactDownloader.DownloadAndExtract(artifactUrl).Returns(resolvedLocalPath);

            var comp1 = new ComponentApiModel { ComponentId = 1, ComponentName = "Comp1", IsEnabled = true, StopOnFailure = true };
            var components = new List<ComponentApiModel> { comp1 };
            SetupOrderedComponents(components);
            mockComponentProcessor.DeployComponent(
                comp1, Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<bool>(),
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IDictionary<string, VariableValue>>(), Arg.Any<CancellationToken>())
                .Returns(x => throw new InvalidOperationException("deployment blew up"));

            var dto = new RequestToProcessDto(
                new DeploymentRequestApiModel
                {
                    Id = 100, EnvironmentName = "TestEnv", IsProd = false,
                    BuildNumber = "1.0.0", RequestDetails = "<details>"
                },
                new DeploymentRequestDetail
                {
                    EnvironmentName = "TestEnv",
                    Components = new List<string> { "Comp1" },
                    BuildDetail = new BuildDetail { DropLocation = artifactUrl }
                });

            // Act
            sut.Execute(dto, CancellationToken.None, mockLoggerFactory);

            // Assert — cleanup still fires even though deployment failed
            mockGitHubArtifactDownloader.Received(1).Cleanup(resolvedLocalPath);
        }

        [TestMethod]
        public void Execute_WithLocalDropLocation_DoesNotCallCleanup()
        {
            // Regression guard: if the DropLocation isn't a GitHub artifact URL,
            // Cleanup must NOT be called — there's nothing to clean up and
            // calling Cleanup on a random local path could delete shared data.
            var comp1 = new ComponentApiModel { ComponentId = 1, ComponentName = "Comp1", IsEnabled = true };
            var components = new List<ComponentApiModel> { comp1 };
            SetupOrderedComponents(components);
            mockComponentProcessor.DeployComponent(
                comp1, Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<bool>(),
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IDictionary<string, VariableValue>>(), Arg.Any<CancellationToken>())
                .Returns(true);

            // Default mock: IsGitHubArtifactUrl returns false (set in Setup)
            var dto = CreateRequest(components); // DropLocation = "C:\\Drop"

            // Act
            sut.Execute(dto, CancellationToken.None, mockLoggerFactory);

            // Assert — downloader APIs never consulted for a non-UNC/non-URL drop
            mockGitHubArtifactDownloader.DidNotReceive().DownloadAndExtract(Arg.Any<string>());
            mockGitHubArtifactDownloader.DidNotReceive().Cleanup(Arg.Any<string>());
        }

        [TestMethod]
        public void Execute_ResumedRequestWithCancelledComponent_MarksFailedAndHonorsStopOnFailure()
        {
            // Regression test for request 2101660 / component 4971:
            // When Monitor resumes a stale Running request whose in-flight component was left in Cancelled status,
            // it must NOT leave deploymentRequestStatus as Completed or dispatch subsequent components when StopOnFailure=true.
            var comp1 = new ComponentApiModel { ComponentId = 4971, ComponentName = "040 - RVC Restore Database", IsEnabled = true, StopOnFailure = true, ComponentType = ComponentType.PowerShell };
            var comp2 = new ComponentApiModel { ComponentId = 4972, ComponentName = "050 - Run Acceptance Tests", IsEnabled = true, StopOnFailure = false, ComponentType = ComponentType.PowerShell };
            var components = new List<ComponentApiModel> { comp1, comp2 };

            var cancelledResult = new DeploymentResultApiModel
            {
                Id = 10,
                ComponentId = 4971,
                RequestId = 100,
                Status = DeploymentResultStatus.Cancelled.ToString(),
                Log = "Job 41835857 Running - 95%"
            };
            var pendingResult = new DeploymentResultApiModel
            {
                Id = 11,
                ComponentId = 4972,
                RequestId = 100,
                Status = DeploymentResultStatus.Pending.ToString()
            };

            mockRequestsPersistentSource.GetSubmittedComponents(100).Returns(components);
            mockRequestsPersistentSource.GetDeploymentResultsForRequest(100)
                .Returns(new List<DeploymentResultApiModel> { cancelledResult, pendingResult });
            mockRequestsPersistentSource.UpdateResultStatus(
                cancelledResult,
                Arg.Any<DeploymentResultStatus>(),
                Arg.Any<DeploymentResultStatus>())
                .Returns(true);

            sut.Execute(CreateRequest(components), CancellationToken.None, mockLoggerFactory);

            // Downstream component 4972 must NOT be dispatched
            mockComponentProcessor.DidNotReceiveWithAnyArgs().DeployComponent(
                default!, default!, default, default, default, default, default!, default!, default!, default);

            // Interrupted Cancelled component is transitioned to Failed with formatted explanation log
            mockRequestsPersistentSource.Received(1).UpdateResultLog(
                cancelledResult,
                Arg.Is<string>(log =>
                    log.Contains("Job 41835857 Running - 95%") &&
                    log.Contains("[ERROR]   The previous component execution was interrupted because the active monitor stopped or lost its lock.")));
            mockRequestsPersistentSource.Received(1).UpdateResultStatus(
                cancelledResult,
                Arg.Is<DeploymentResultStatus>(s => s.Value == DeploymentResultStatus.Failed.Value),
                Arg.Is<DeploymentResultStatus>(s => s.Value == DeploymentResultStatus.Cancelled.Value));
            mockComponentsPersistentSource.Received(1).SaveEnvComponentStatus(
                1,
                comp1,
                DeploymentResultStatus.Failed.ToString(),
                100);
            mockEventsPublisher.Received(1).PublishResultStatusChangedAsync(
                Arg.Is<DeploymentResultEventData>(e => e.Status == DeploymentResultStatus.Failed.ToString()));

            // Remaining Pending component is cancelled
            mockRequestsPersistentSource.Received(1).UpdateResultStatus(
                pendingResult,
                Arg.Is<DeploymentResultStatus>(s => s.Value == DeploymentResultStatus.Cancelled.Value));

            // Parent request is marked Failed (never Completed!)
            mockRequestsPersistentSource.Received(1).SetRequestCompletionStatus(
                100,
                DeploymentRequestStatus.Failed,
                Arg.Any<DateTimeOffset>(),
                Arg.Any<string?>());
        }

        [TestMethod]
        public void Execute_WhenInterruptedWhileRequestStillRunningInDb_LeavesRequestRunningForStartupRecovery()
        {
            var comp1 = new ComponentApiModel { ComponentId = 1, ComponentName = "Comp1", IsEnabled = true, StopOnFailure = true };
            var comp2 = new ComponentApiModel { ComponentId = 2, ComponentName = "Comp2", IsEnabled = true, StopOnFailure = false };
            var components = new List<ComponentApiModel> { comp1, comp2 };

            var dto = CreateRequest(components);
            SetupOrderedComponents(components);

            mockComponentProcessor.DeployComponent(
                comp1, Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<bool>(),
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IDictionary<string, VariableValue>>(), Arg.Any<CancellationToken>())
                .Returns(_ => throw new OperationCanceledException());

            // Request in DB is still Running (monitor shutdown / lock loss, not user cancellation)
            mockRequestsPersistentSource.GetRequest(100)
                .Returns(new DeploymentRequestApiModel { Id = 100, Status = DeploymentRequestStatus.Running.ToString() });

            sut.Execute(dto, CancellationToken.None, mockLoggerFactory);

            // Request must NOT be finalized as Cancelled or Completed; instead it transitions Running -> Pending fenced by StartedTime
            mockRequestsPersistentSource.DidNotReceiveWithAnyArgs()
                .SetRequestCompletionStatus(default, default, default, default);
            mockRequestsPersistentSource.Received(1).ResumeInterruptedRunningRequest(
                100,
                Arg.Any<DateTimeOffset>());
        }

        [TestMethod]
        public void Execute_WhenLockLossInterleavesWithUserCancelling_ReconcilesPreservedRunningResultToCancelled()
        {
            var comp1 = new ComponentApiModel { ComponentId = 1, ComponentName = "Comp1", IsEnabled = true, StopOnFailure = true };
            var comp2 = new ComponentApiModel { ComponentId = 2, ComponentName = "Comp2", IsEnabled = true, StopOnFailure = false };
            var components = new List<ComponentApiModel> { comp1, comp2 };

            var comp1Result = new DeploymentResultApiModel { Id = 1, ComponentId = 1, RequestId = 100, Status = DeploymentResultStatus.Pending.ToString() };
            var pendingResult = new DeploymentResultApiModel { Id = 2, ComponentId = 2, RequestId = 100, Status = DeploymentResultStatus.Pending.ToString() };

            var dto = CreateRequest(components);
            mockRequestsPersistentSource.GetSubmittedComponents(100).Returns(components);
            mockRequestsPersistentSource.GetDeploymentResultsForRequest(100)
                .Returns(new List<DeploymentResultApiModel> { comp1Result, pendingResult });

            mockComponentProcessor.DeployComponent(
                comp1, Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<bool>(),
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IDictionary<string, VariableValue>>(), Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    // ComponentProcessor marks result Running on start and preserves Running when interrupted while request is still Running
                    comp1Result.Status = DeploymentResultStatus.Running.ToString();
                    throw new OperationCanceledException();
                });

            // Simulate user changing request to Cancelling after ComponentProcessor preserved the result as Running
            mockRequestsPersistentSource.GetRequest(100)
                .Returns(new DeploymentRequestApiModel { Id = 100, Status = DeploymentRequestStatus.Cancelling.ToString() });
            mockRequestsPersistentSource.UpdateResultStatus(
                Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<DeploymentResultStatus>(),
                Arg.Any<DeploymentResultStatus>()).Returns(true);
            mockRequestsPersistentSource.UpdateResultStatus(
                Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<DeploymentResultStatus>()).Returns(true);

            sut.Execute(dto, CancellationToken.None, mockLoggerFactory);

            // Preserved Running result must be transitioned to Cancelled using expected-status Running
            mockRequestsPersistentSource.Received(1).UpdateResultStatus(
                comp1Result,
                Arg.Is<DeploymentResultStatus>(s => s.Value == DeploymentResultStatus.Cancelled.Value),
                Arg.Is<DeploymentResultStatus>(s => s.Value == DeploymentResultStatus.Running.Value));
            mockRequestsPersistentSource.Received(1).UpdateResultStatus(
                pendingResult,
                Arg.Is<DeploymentResultStatus>(s => s.Value == DeploymentResultStatus.Cancelled.Value));
            mockRequestsPersistentSource.Received(1).SetRequestCompletionStatus(
                100,
                DeploymentRequestStatus.Cancelled,
                Arg.Any<DateTimeOffset>(),
                Arg.Any<string?>());
        }

        [TestMethod]
        public void Execute_ResumedRequestWithRunningComponent_HandlesEmptyLogAndFailedEventFaultAndSkippedTransition()
        {
            var comp1 = new ComponentApiModel { ComponentId = 1, ComponentName = "Comp1", IsEnabled = true, StopOnFailure = false };
            var comp2 = new ComponentApiModel { ComponentId = 2, ComponentName = "Comp2", IsEnabled = true, StopOnFailure = true };
            var components = new List<ComponentApiModel> { comp1, comp2 };

            var runningResult1 = new DeploymentResultApiModel
            {
                Id = 10,
                ComponentId = 1,
                RequestId = 100,
                Status = DeploymentResultStatus.Running.ToString(),
                Log = null
            };
            var runningResult2 = new DeploymentResultApiModel
            {
                Id = 11,
                ComponentId = 2,
                RequestId = 100,
                Status = DeploymentResultStatus.Running.ToString(),
                Log = string.Empty
            };

            mockRequestsPersistentSource.GetSubmittedComponents(100).Returns(components);
            mockRequestsPersistentSource.GetDeploymentResultsForRequest(100)
                .Returns(new List<DeploymentResultApiModel> { runningResult1, runningResult2 });

            // First component transitions to Failed and event publish faults; second component loses CAS (returns false)
            mockRequestsPersistentSource.UpdateResultStatus(
                runningResult1,
                Arg.Any<DeploymentResultStatus>(),
                Arg.Any<DeploymentResultStatus>())
                .Returns(true);
            mockRequestsPersistentSource.UpdateResultStatus(
                runningResult2,
                Arg.Any<DeploymentResultStatus>(),
                Arg.Any<DeploymentResultStatus>())
                .Returns(false);
            mockEventsPublisher.PublishResultStatusChangedAsync(Arg.Any<DeploymentResultEventData>())
                .Returns(Task.FromException(new InvalidOperationException("event bus down")));

            sut.Execute(CreateRequest(components), CancellationToken.None, mockLoggerFactory);

            mockRequestsPersistentSource.Received(1).UpdateResultLog(
                runningResult1,
                Arg.Is<string>(log => log.Contains("[ERROR]   The previous component execution was interrupted because the active monitor stopped or lost its lock.")));
            mockComponentsPersistentSource.Received(1).SaveEnvComponentStatus(
                1,
                comp1,
                DeploymentResultStatus.Failed.ToString(),
                100);
            // Case 4: Verify null-conditional branch when componentsPersistentSource is null
            var sutWithoutComponentsSource = new PendingRequestProcessor(
                mockLoggerFactory,
                mockComponentProcessor,
                mockVariableScopeOptionsResolver,
                mockRequestsPersistentSource,
                mockPropertyValuesPersistentSource,
                mockEnvironmentsPersistentSource,
                mockManageProjectsPersistentSource,
                mockConfigValuesPersistentSource,
                mockPropertyEvaluator,
                mockEventsPublisher,
                mockNotificationSink,
                mockGitHubArtifactDownloader,
                componentsPersistentSource: null);

            mockRequestsPersistentSource.GetDeploymentResultsForRequest(100)
                .Returns(new List<DeploymentResultApiModel> { runningResult1 });
            mockEventsPublisher.PublishResultStatusChangedAsync(Arg.Any<DeploymentResultEventData>())
                .Returns(Task.CompletedTask);
            mockRequestsPersistentSource.GetSubmittedComponents(100)
                .Returns(new List<ComponentApiModel> { comp1 });

            sutWithoutComponentsSource.Execute(CreateRequest(new List<ComponentApiModel> { comp1 }), CancellationToken.None, mockLoggerFactory);
        }

        [TestMethod]
        public void Execute_WhenInterruptedAndDbCallsThrow_ReturnsCleanlyForStartupRecovery()
        {
            var comp1 = new ComponentApiModel { ComponentId = 1, ComponentName = "Comp1", IsEnabled = true, StopOnFailure = true };
            var components = new List<ComponentApiModel> { comp1 };

            var dto = CreateRequest(components);
            SetupOrderedComponents(components);

            var throwOnGetRequest = false;
            mockRequestsPersistentSource.GetRequest(100)
                .Returns(_ =>
                {
                    if (throwOnGetRequest)
                        throw new InvalidOperationException("DB connection lost");
                    return new DeploymentRequestApiModel { Id = 100, Status = DeploymentRequestStatus.Running.ToString() };
                });

            // Case 1: GetRequest throws transient DB error when called inside catch (OperationCanceledException)
            mockComponentProcessor.DeployComponent(
                comp1, Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<bool>(),
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IDictionary<string, VariableValue>>(), Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    throwOnGetRequest = true;
                    throw new OperationCanceledException();
                });

            sut.Execute(dto, CancellationToken.None, mockLoggerFactory);

            mockRequestsPersistentSource.DidNotReceiveWithAnyArgs()
                .SetRequestCompletionStatus(default, default, default, default);

            // Case 2: GetRequest returns Running, ResumeInterruptedRunningRequest throws transient DB error
            throwOnGetRequest = false;
            mockComponentProcessor.DeployComponent(
                comp1, Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<bool>(),
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IDictionary<string, VariableValue>>(), Arg.Any<CancellationToken>())
                .Returns(_ => throw new OperationCanceledException());
            mockRequestsPersistentSource.ResumeInterruptedRunningRequest(100, Arg.Any<DateTimeOffset>())
                .Returns(_ => throw new InvalidOperationException("DB timeout"));

            sut.Execute(dto, CancellationToken.None, mockLoggerFactory);

            mockRequestsPersistentSource.DidNotReceiveWithAnyArgs()
                .SetRequestCompletionStatus(default, default, default, default);

            // Case 3: ResumeInterruptedRunningRequest returns > 0 and event publish faults
            mockRequestsPersistentSource.ResumeInterruptedRunningRequest(100, Arg.Any<DateTimeOffset>())
                .Returns(1);
            mockEventsPublisher.PublishRequestStatusChangedAsync(Arg.Any<DeploymentRequestEventData>())
                .Returns(Task.FromException(new InvalidOperationException("hub down")));

            sut.Execute(dto, CancellationToken.None, mockLoggerFactory);

            Assert.AreEqual(DeploymentRequestStatus.Pending.ToString(), dto.Request.Status);

            // Case 4: GetRequest returns Pending or null on external restart/resume during cancellation
            mockRequestsPersistentSource.GetRequest(100)
                .Returns(new DeploymentRequestApiModel { Id = 100, Status = DeploymentRequestStatus.Pending.ToString() });

            sut.Execute(dto, CancellationToken.None, mockLoggerFactory);

            mockRequestsPersistentSource.Received(1).SetRequestCompletionStatus(
                100,
                DeploymentRequestStatus.Pending,
                Arg.Any<DateTimeOffset>(),
                Arg.Any<string?>());

            mockRequestsPersistentSource.GetRequest(100)
                .Returns((DeploymentRequestApiModel?)null);

            sut.Execute(dto, CancellationToken.None, mockLoggerFactory);

            mockRequestsPersistentSource.Received(1).SetRequestCompletionStatus(
                100,
                DeploymentRequestStatus.Cancelled,
                Arg.Any<DateTimeOffset>(),
                Arg.Any<string?>());
        }
    }
}
