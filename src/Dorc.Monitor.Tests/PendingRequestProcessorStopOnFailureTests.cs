using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core;
using Dorc.Core.Events;
using Dorc.Core.Interfaces;
using Dorc.Core.VariableResolution;
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

        private PendingRequestProcessor sut = null!;

        [TestInitialize]
        public void Setup()
        {
            mockLoggerFactory = Substitute.For<ILoggerFactory>();
            mockLoggerFactory.CreateLogger(Arg.Any<string>()).Returns(Substitute.For<ILogger>());

            mockComponentProcessor = Substitute.For<IComponentProcessor>();
            mockVariableScopeOptionsResolver = Substitute.For<IVariableScopeOptionsResolver>();
            mockRequestsPersistentSource = Substitute.For<IRequestsPersistentSource>();
            mockPropertyValuesPersistentSource = Substitute.For<IPropertyValuesPersistentSource>();
            mockEnvironmentsPersistentSource = Substitute.For<IEnvironmentsPersistentSource>();
            mockManageProjectsPersistentSource = Substitute.For<IManageProjectsPersistentSource>();
            mockConfigValuesPersistentSource = Substitute.For<IConfigValuesPersistentSource>();
            mockPropertyEvaluator = Substitute.For<IPropertyEvaluator>();
            mockEventsPublisher = Substitute.For<IDeploymentEventsPublisher>();
            mockGitHubArtifactDownloader = Substitute.For<IGitHubArtifactDownloader>();
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
                mockGitHubArtifactDownloader);
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
            mockManageProjectsPersistentSource
                .GetOrderedComponents(Arg.Any<IEnumerable<string>>())
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
            sut.Execute(dto, CancellationToken.None);

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
            sut.Execute(dto, CancellationToken.None);

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
            sut.Execute(dto, CancellationToken.None);

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
            sut.Execute(dto, CancellationToken.None);

            // Assert - comp2 was never deployed
            mockComponentProcessor.DidNotReceive().DeployComponent(
                comp2, Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<bool>(),
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IDictionary<string, VariableValue>>(), Arg.Any<CancellationToken>());
        }

        // -------- GitHub artifact cleanup path --------

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
            sut.Execute(dto, CancellationToken.None);

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
            sut.Execute(dto, CancellationToken.None);

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
            sut.Execute(dto, CancellationToken.None);

            // Assert — downloader APIs never consulted for a non-UNC/non-URL drop
            mockGitHubArtifactDownloader.DidNotReceive().DownloadAndExtract(Arg.Any<string>());
            mockGitHubArtifactDownloader.DidNotReceive().Cleanup(Arg.Any<string>());
        }
    }
}
