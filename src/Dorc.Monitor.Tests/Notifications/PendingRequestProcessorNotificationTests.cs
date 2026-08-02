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

namespace Dorc.Monitor.Tests.Notifications
{
    [TestClass]
    public class PendingRequestProcessorNotificationTests
    {
        private IComponentProcessor mockComponentProcessor = null!;
        private IRequestsPersistentSource mockRequestsPersistentSource = null!;
        private IManageProjectsPersistentSource mockManageProjectsPersistentSource = null!;
        private IDeploymentNotificationSink mockNotificationSink = null!;

        private PendingRequestProcessor sut = null!;

        [TestInitialize]
        public void Setup()
        {
            var mockLoggerFactory = Substitute.For<ILoggerFactory>();
            mockLoggerFactory.CreateLogger(Arg.Any<string>()).Returns(Substitute.For<ILogger>());

            mockComponentProcessor = Substitute.For<IComponentProcessor>();
            mockRequestsPersistentSource = Substitute.For<IRequestsPersistentSource>();
            mockManageProjectsPersistentSource = Substitute.For<IManageProjectsPersistentSource>();
            mockNotificationSink = Substitute.For<IDeploymentNotificationSink>();

            var mockConfigValuesPersistentSource = Substitute.For<IConfigValuesPersistentSource>();
            mockConfigValuesPersistentSource.GetConfigValue("ScriptRoot", Arg.Any<string>()).Returns("C:\\Scripts");
            mockConfigValuesPersistentSource.GetConfigValue("ScriptRoot").Returns("C:\\Scripts");
            mockConfigValuesPersistentSource.GetConfigValue("DeploymentLogDir", Arg.Any<string>()).Returns("C:\\Logs");
            mockConfigValuesPersistentSource.GetAllConfigValues(true).Returns(Enumerable.Empty<ConfigValueApiModel>());

            var mockEnvironmentsPersistentSource = Substitute.For<IEnvironmentsPersistentSource>();
            mockEnvironmentsPersistentSource.GetEnvironment(Arg.Any<string>())
                .Returns(new EnvironmentApiModel
                {
                    EnvironmentId = 1,
                    EnvironmentName = "TestEnv",
                    EnvironmentIsProd = false,
                    EnvironmentSecure = false
                });

            var mockPropertyValuesPersistentSource = Substitute.For<IPropertyValuesPersistentSource>();
            mockPropertyValuesPersistentSource.LoadAllPropertiesIntoCache()
                .Returns(new Dictionary<string, PropertyValueDto>());

            var mockEventsPublisher = Substitute.For<IDeploymentEventsPublisher>();
            mockEventsPublisher.PublishRequestStatusChangedAsync(Arg.Any<DeploymentRequestEventData>())
                .Returns(Task.CompletedTask);
            mockEventsPublisher.PublishResultStatusChangedAsync(Arg.Any<DeploymentResultEventData>())
                .Returns(Task.CompletedTask);

            var mockGitHubArtifactDownloader = Substitute.For<IGitHubArtifactDownloader>();
            mockGitHubArtifactDownloader.IsGitHubArtifactUrl(Arg.Any<string>()).Returns(false);

            sut = new PendingRequestProcessor(
                mockLoggerFactory,
                mockComponentProcessor,
                Substitute.For<IVariableScopeOptionsResolver>(),
                mockRequestsPersistentSource,
                mockPropertyValuesPersistentSource,
                mockEnvironmentsPersistentSource,
                mockManageProjectsPersistentSource,
                mockConfigValuesPersistentSource,
                Substitute.For<IPropertyEvaluator>(),
                mockEventsPublisher,
                mockNotificationSink,
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
                UserName = "jane.doe@example.com",
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

        private void SetupDeployResult(ComponentApiModel component, bool succeeds)
        {
            mockComponentProcessor.DeployComponent(
                component, Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<bool>(),
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IDictionary<string, VariableValue>>(), Arg.Any<CancellationToken>())
                .Returns(succeeds);
        }

        [TestMethod]
        public void Execute_AllComponentsSucceed_NotifiesExactlyOnceWithCompleted()
        {
            var comp = new ComponentApiModel { ComponentId = 1, ComponentName = "Comp1", IsEnabled = true };
            var components = new List<ComponentApiModel> { comp };
            var dto = CreateRequest(components);
            SetupOrderedComponents(components);
            SetupDeployResult(comp, succeeds: true);

            sut.Execute(dto, CancellationToken.None);

            mockNotificationSink.ReceivedWithAnyArgs(1).NotifyRequestCompletedAsync(default!, default!, default, default);
            mockNotificationSink.Received(1).NotifyRequestCompletedAsync(
                dto.Request,
                DeploymentRequestStatus.Completed.ToString(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>());
        }

        [TestMethod]
        public void Execute_ComponentFailsWithStopOnFailure_NotifiesExactlyOnceWithFailed()
        {
            var comp = new ComponentApiModel { ComponentId = 1, ComponentName = "Comp1", IsEnabled = true, StopOnFailure = true };
            var components = new List<ComponentApiModel> { comp };
            var dto = CreateRequest(components);
            SetupOrderedComponents(components);
            SetupDeployResult(comp, succeeds: false);

            sut.Execute(dto, CancellationToken.None);

            mockNotificationSink.ReceivedWithAnyArgs(1).NotifyRequestCompletedAsync(default!, default!, default, default);
            mockNotificationSink.Received(1).NotifyRequestCompletedAsync(
                dto.Request,
                DeploymentRequestStatus.Failed.ToString(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>());
        }

        [TestMethod]
        public void Execute_NoComponents_NotifiesExactlyOnceWithCompleted()
        {
            var dto = CreateRequest(new List<ComponentApiModel>());
            SetupOrderedComponents(new List<ComponentApiModel>());

            sut.Execute(dto, CancellationToken.None);

            mockNotificationSink.ReceivedWithAnyArgs(1).NotifyRequestCompletedAsync(default!, default!, default, default);
            mockNotificationSink.Received(1).NotifyRequestCompletedAsync(
                dto.Request,
                DeploymentRequestStatus.Completed.ToString(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>());
        }

        [TestMethod]
        public void Execute_SinkReturnsFaultedTask_DoesNotAffectRequestCompletion()
        {
            var comp = new ComponentApiModel { ComponentId = 1, ComponentName = "Comp1", IsEnabled = true };
            var components = new List<ComponentApiModel> { comp };
            var dto = CreateRequest(components);
            SetupOrderedComponents(components);
            SetupDeployResult(comp, succeeds: true);

            mockNotificationSink.NotifyRequestCompletedAsync(default!, default!, default, default)
                .ReturnsForAnyArgs(Task.FromException(new InvalidOperationException("async sink failure")));

            sut.Execute(dto, CancellationToken.None);

            mockRequestsPersistentSource.Received(1).SetRequestCompletionStatus(
                100,
                DeploymentRequestStatus.Completed,
                Arg.Any<DateTimeOffset>());
        }

        [TestMethod]
        public void Execute_SinkThrowsSynchronously_DoesNotAffectRequestCompletion()
        {
            var comp = new ComponentApiModel { ComponentId = 1, ComponentName = "Comp1", IsEnabled = true };
            var components = new List<ComponentApiModel> { comp };
            var dto = CreateRequest(components);
            SetupOrderedComponents(components);
            SetupDeployResult(comp, succeeds: true);

            mockNotificationSink.NotifyRequestCompletedAsync(default!, default!, default, default)
                .ReturnsForAnyArgs(_ => throw new InvalidOperationException("sink blew up"));

            sut.Execute(dto, CancellationToken.None);

            mockRequestsPersistentSource.Received(1).SetRequestCompletionStatus(
                100,
                DeploymentRequestStatus.Completed,
                Arg.Any<DateTimeOffset>());
        }
    }
}
