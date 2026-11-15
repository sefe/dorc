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
            mockEnvironmentsPersistentSource.TryTransitionEphemeralState(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<string>(),
                Arg.Any<int?>())
                .Returns(true);

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
        public void Execute_DestroyRequest_ReversesComponentExecutionOrderAndPropagatesDestroyMarker()
        {
            mockEnvironmentsPersistentSource.GetEnvironment(Arg.Any<string>())
                .Returns(new EnvironmentApiModel
                {
                    EnvironmentId = 1,
                    EnvironmentName = "TestEnv",
                    IsEphemeral = true,
                    EnvironmentIsProd = false
                });
            mockEnvironmentsPersistentSource.DeleteEnvironmentInternal(1).Returns(true);

            var postgres = new ComponentApiModel { ComponentId = 1, ComponentName = "Infra-Postgres", IsEnabled = true, ComponentType = ComponentType.Terraform };
            var kafka = new ComponentApiModel { ComponentId = 2, ComponentName = "Infra-Kafka", IsEnabled = true, ComponentType = ComponentType.Terraform };
            var components = new List<ComponentApiModel> { postgres, kafka };
            SetupOrderedComponents(components);

            var executedOrder = new List<string>();
            IDictionary<string, VariableValue>? capturedProps = null;
            mockComponentProcessor.DeployComponent(
                Arg.Do<ComponentApiModel>(c => executedOrder.Add(c.ComponentName!)),
                Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<bool>(),
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Do<IDictionary<string, VariableValue>>(p => capturedProps = p),
                Arg.Any<CancellationToken>())
                .Returns(true);

            var dto = CreateRequest(components);
            dto.Details.Properties = new List<PropertyPair>
            {
                new("TerraformOperation", "Destroy")
            };

            sut.Execute(dto, CancellationToken.None, mockLoggerFactory);

            CollectionAssert.AreEqual(new[] { "Infra-Kafka", "Infra-Postgres" }, executedOrder);
            Assert.IsNotNull(capturedProps);
            Assert.AreEqual("Destroy", capturedProps[ComponentProcessor.TerraformOperationPropertyName].Value);
        }

        [TestMethod]
        public void Execute_DestroyRequest_OnNonEphemeralOrProdEnvironment_FailsImmediatelyWithoutExecutingComponents()
        {
            var postgres = new ComponentApiModel { ComponentId = 1, ComponentName = "Infra-Postgres", IsEnabled = true, ComponentType = ComponentType.Terraform };
            SetupOrderedComponents(new List<ComponentApiModel> { postgres });

            // 1) Non-ephemeral environment
            mockEnvironmentsPersistentSource.GetEnvironment(Arg.Any<string>()).Returns(new EnvironmentApiModel
            {
                EnvironmentId = 1, EnvironmentName = "TestEnv", IsEphemeral = false, EnvironmentIsProd = false
            });
            var dtoNonEph = CreateRequest(new List<ComponentApiModel> { postgres });
            dtoNonEph.Details.Properties = new List<PropertyPair> { new("TerraformOperation", "Destroy") };
            sut.Execute(dtoNonEph, CancellationToken.None, mockLoggerFactory);

            // 2) Production ephemeral environment
            mockEnvironmentsPersistentSource.GetEnvironment(Arg.Any<string>()).Returns(new EnvironmentApiModel
            {
                EnvironmentId = 2, EnvironmentName = "TestEnv", IsEphemeral = true, EnvironmentIsProd = true
            });
            var dtoProd = CreateRequest(new List<ComponentApiModel> { postgres });
            dtoProd.Details.Properties = new List<PropertyPair> { new("TerraformOperation", "Destroy") };
            sut.Execute(dtoProd, CancellationToken.None, mockLoggerFactory);

            // 3) Ephemeral environment with IsProd request flag
            mockEnvironmentsPersistentSource.GetEnvironment(Arg.Any<string>()).Returns(new EnvironmentApiModel
            {
                EnvironmentId = 3, EnvironmentName = "TestEnv", IsEphemeral = true, EnvironmentIsProd = false
            });
            var dtoReqProd = CreateRequest(new List<ComponentApiModel> { postgres });
            dtoReqProd.Request.IsProd = true;
            dtoReqProd.Details.Properties = new List<PropertyPair> { new("TerraformOperation", "Destroy") };
            sut.Execute(dtoReqProd, CancellationToken.None, mockLoggerFactory);

            // 4) Already-deleted environment (environment == null) on DestroyRequest -> completes cleanly without throwing NRE
            mockEnvironmentsPersistentSource.GetEnvironment(Arg.Any<string>()).Returns((EnvironmentApiModel?)null);
            var dtoDeletedEnv = CreateRequest(new List<ComponentApiModel> { postgres });
            dtoDeletedEnv.Details.Properties = new List<PropertyPair> { new("TerraformOperation", "Destroy") };
            sut.Execute(dtoDeletedEnv, CancellationToken.None, mockLoggerFactory);

            // 5) Missing environment (environment == null) on non-destroy request -> throws InvalidOperationException and marks Failed
            var dtoMissingNonDestroy = CreateRequest(new List<ComponentApiModel> { postgres });
            sut.Execute(dtoMissingNonDestroy, CancellationToken.None, mockLoggerFactory);

            mockComponentProcessor.DidNotReceiveWithAnyArgs().DeployComponent(
                default!, default!, default, default, default, default, default!, default!, default!, default);
            mockRequestsPersistentSource.Received(3).SetRequestCompletionStatus(
                100,
                DeploymentRequestStatus.Failed,
                Arg.Any<DateTimeOffset>(),
                Arg.Any<string>());
            mockRequestsPersistentSource.Received(1).SetRequestCompletionStatus(
                100,
                DeploymentRequestStatus.Errored,
                Arg.Any<DateTimeOffset>(),
                Arg.Any<string>());
            mockRequestsPersistentSource.Received(1).SetRequestCompletionStatus(
                100,
                DeploymentRequestStatus.Completed,
                Arg.Any<DateTimeOffset>(),
                null);
        }

        [TestMethod]
        public void Execute_DestroyRequest_IncludesDisabledTerraformComponentsAndChainsDeferredValidationRequests()
        {
            var ephEnv = new EnvironmentApiModel
            {
                EnvironmentId = 70,
                EnvironmentName = "eph-pr-70",
                IsEphemeral = true,
                EnvironmentIsProd = false,
                EphemeralState = EphemeralEnvironmentStates.Provisioning
            };
            mockEnvironmentsPersistentSource.GetEnvironment(Arg.Any<string>())
                .Returns(_ => new EnvironmentApiModel
                {
                    EnvironmentId = 70,
                    EnvironmentName = "eph-pr-70",
                    IsEphemeral = true,
                    EnvironmentIsProd = false,
                    EphemeralState = EphemeralEnvironmentStates.Provisioning
                });
            mockEnvironmentsPersistentSource.DeleteEnvironmentInternal(70).Returns(true);

            // Destroy request with a disabled Terraform component must still tear it down (both when result is Disabled and Pending)
            var disabledTf = new ComponentApiModel { ComponentId = 1, ComponentName = "DisabledTf", IsEnabled = false, ComponentType = ComponentType.Terraform };
            SetupOrderedComponents(new List<ComponentApiModel> { disabledTf });
            mockRequestsPersistentSource.GetDeploymentResultsForRequest(100).Returns(new List<DeploymentResultApiModel>
            {
                new() { Id = 10, ComponentId = 1, RequestId = 100, Status = DeploymentResultStatus.Disabled.ToString() }
            });
            mockComponentProcessor.DeployComponent(
                disabledTf, Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<bool>(),
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IDictionary<string, VariableValue>>(), Arg.Any<CancellationToken>())
                .Returns(true);

            var destroyDto = CreateRequest(new List<ComponentApiModel> { disabledTf });
            destroyDto.Details.Properties = new List<PropertyPair>
            {
                new("TerraformOperation", "Destroy"),
                new("EphemeralEnvironmentId", "0")
            };
            sut.Execute(destroyDto, CancellationToken.None, mockLoggerFactory);
            mockComponentProcessor.Received(1).DeployComponent(
                disabledTf, Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<bool>(),
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IDictionary<string, VariableValue>>(), Arg.Any<CancellationToken>());

            // DeferredValidationComponents chaining:
            // 1) Valid component IDs & tokens -> submits chained validation request and transitions to Testing
            var valComp = new ComponentApiModel
            {
                ComponentId = 201,
                ComponentName = "SmokeValidation",
                IsEnabled = true,
                Children = new List<ComponentApiModel>
                {
                    new()
                    {
                        ComponentId = 203,
                        ComponentName = "NestedSmokeValidation",
                        IsEnabled = true,
                        Children = new List<ComponentApiModel>()
                    }
                }
            };
            var nullNameComp = new ComponentApiModel { ComponentId = 202, ComponentName = "", IsEnabled = true };
            var nullIdComp = new ComponentApiModel { ComponentId = null, ComponentName = "NoId", IsEnabled = true };
            mockEnvironmentsPersistentSource.GetMappedProjects(Arg.Any<string>()).Returns(new List<ProjectApiModel>
            {
                new() { ProjectId = 1, ProjectName = "TestProject" },
                new() { ProjectId = 2, ProjectName = "EmptyProject" },
                new() { ProjectId = 3, ProjectName = "ExtraProject" }
            });
            mockManageProjectsPersistentSource.GetOrderedComponents(2).Returns((IEnumerable<ComponentApiModel>)null!);
            mockManageProjectsPersistentSource.GetOrderedComponents(3).Returns(new List<ComponentApiModel> { valComp, nullNameComp, nullIdComp });
            mockRequestsPersistentSource.SubmitRequest(Arg.Any<Dorc.PersistentData.Model.DeploymentRequest>()).Returns(888);

            var provComp = new ComponentApiModel { ComponentId = 10, ComponentName = "Infra", IsEnabled = true };
            SetupOrderedComponents(new List<ComponentApiModel> { provComp });
            mockComponentProcessor.DeployComponent(
                provComp, Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<bool>(),
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IDictionary<string, VariableValue>>(), Arg.Any<CancellationToken>())
                .Returns(true);

            var deferredDto = CreateRequest(new List<ComponentApiModel> { provComp });
            deferredDto.Details.Properties = new List<PropertyPair>
            {
                new(TerraformDestroyOperationConstants.DeferredValidationComponentsPropertyName, "201,203,999,NamedValidation"),
                new(TerraformDestroyOperationConstants.DeferredValidationBuildNumPropertyName, "20260417.99"),
                new(TerraformDestroyOperationConstants.DeferredValidationBuildUrlPropertyName, "https://dev.azure.com/org/proj/_build/results?buildId=99"),
                new(TerraformDestroyOperationConstants.EphemeralTargetStatePropertyName, EphemeralEnvironmentStates.Testing),
                new(TerraformDestroyOperationConstants.EphemeralEnvironmentIdPropertyName, "70"),
                new("CustomProp", "Val")
            };
            sut.Execute(deferredDto, CancellationToken.None, mockLoggerFactory);
            mockRequestsPersistentSource.Received(1).SubmitRequest(
                Arg.Is<Dorc.PersistentData.Model.DeploymentRequest>(r =>
                    r.Components == "SmokeValidation|NestedSmokeValidation|999|NamedValidation" &&
                    r.BuildNumber == "20260417.99" &&
                    r.BuildUri == "vstfs:///Build/Build/99" &&
                    r.DropLocation == "C:\\Drop"));
            mockEnvironmentsPersistentSource.Received().TryTransitionEphemeralState(
                "eph-pr-70",
                Arg.Any<IReadOnlyCollection<string>>(),
                EphemeralEnvironmentStates.Testing,
                70);

            // Destroy request when environment is already in TeardownFailed cancels immediately and returns
            mockEnvironmentsPersistentSource.GetEnvironment(Arg.Any<string>())
                .Returns(_ => new EnvironmentApiModel
                {
                    EnvironmentId = 70,
                    EnvironmentName = "eph-pr-70",
                    IsEphemeral = true,
                    EnvironmentIsProd = false,
                    EphemeralState = EphemeralEnvironmentStates.TeardownFailed
                });
            var siblingDestroyAfterFail = CreateRequest(new List<ComponentApiModel> { disabledTf });
            siblingDestroyAfterFail.Details.Properties = new List<PropertyPair> { new("TerraformOperation", "Destroy") };
            sut.Execute(siblingDestroyAfterFail, CancellationToken.None, mockLoggerFactory);
            Assert.AreEqual(DeploymentRequestStatus.Cancelled.ToString(), siblingDestroyAfterFail.Request.Status);
            mockEnvironmentsPersistentSource.GetEnvironment(Arg.Any<string>())
                .Returns(_ => new EnvironmentApiModel
                {
                    EnvironmentId = 70,
                    EnvironmentName = "eph-pr-70",
                    IsEphemeral = true,
                    EnvironmentIsProd = false,
                    EphemeralState = EphemeralEnvironmentStates.Provisioning
                });

            // When TryTransitionEphemeralState returns false (e.g. teardown claimed the environment in the meantime), chained validation request is NOT submitted
            mockEnvironmentsPersistentSource.TryTransitionEphemeralState(
                "eph-pr-70",
                Arg.Any<IReadOnlyCollection<string>>(),
                EphemeralEnvironmentStates.Testing,
                Arg.Any<int?>()).Returns(false);
            sut.UpdateEphemeralEnvironmentLifecycleState(
                ephEnv,
                deferredDto.Details,
                isDestroyRequest: false,
                DeploymentRequestStatus.Completed,
                sourceRequest: new DeploymentRequestApiModel { UserName = "alice" });
            mockRequestsPersistentSource.Received(1).SubmitRequest(Arg.Any<Dorc.PersistentData.Model.DeploymentRequest>());
            mockEnvironmentsPersistentSource.TryTransitionEphemeralState(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<string>(),
                Arg.Any<int?>()).Returns(true);

            // 2) Cover SubmitDeferredValidationRequest when GetMappedProjects returns null, sourceRequest/requestDetail fields fallback, and whitespace DeferredValidationComponents
            mockEnvironmentsPersistentSource.GetMappedProjects(Arg.Any<string>()).Returns((IEnumerable<ProjectApiModel>)null!);
            var minimalDetail = new DeploymentRequestDetail
            {
                BuildDetail = new BuildDetail(),
                Properties = new List<PropertyPair>
                {
                    new(TerraformDestroyOperationConstants.DeferredValidationComponentsPropertyName, "SmokeValidation")
                }
            };
            sut.UpdateEphemeralEnvironmentLifecycleState(
                ephEnv,
                minimalDetail,
                isDestroyRequest: false,
                DeploymentRequestStatus.Completed,
                sourceRequest: new DeploymentRequestApiModel());

            var sourceFallbackDetail = new DeploymentRequestDetail
            {
                BuildDetail = new BuildDetail(),
                Properties = new List<PropertyPair>
                {
                    new(TerraformDestroyOperationConstants.DeferredValidationComponentsPropertyName, "SmokeValidation")
                }
            };
            ephEnv.EphemeralState = EphemeralEnvironmentStates.Provisioning;
            sut.UpdateEphemeralEnvironmentLifecycleState(
                ephEnv,
                sourceFallbackDetail,
                isDestroyRequest: false,
                DeploymentRequestStatus.Completed,
                sourceRequest: new DeploymentRequestApiModel
                {
                    Project = "FallbackProj",
                    BuildNumber = "FallbackBuild",
                    DropLocation = "FallbackDrop",
                    BuildUri = "FallbackUri",
                    UserName = "FallbackUser"
                });

            var detailDropFallback = new DeploymentRequestDetail
            {
                BuildDetail = new BuildDetail { DropLocation = "file:///from-detail-drop" },
                Properties = new List<PropertyPair>
                {
                    new(TerraformDestroyOperationConstants.DeferredValidationComponentsPropertyName, "SmokeValidation")
                }
            };
            ephEnv.EphemeralState = EphemeralEnvironmentStates.Provisioning;
            sut.UpdateEphemeralEnvironmentLifecycleState(
                ephEnv,
                detailDropFallback,
                isDestroyRequest: false,
                DeploymentRequestStatus.Completed,
                sourceRequest: new DeploymentRequestApiModel { UserName = "alice", BuildUri = "vstfs:///Build/Build/77" });

            var deferredFileUrlDetail = new DeploymentRequestDetail
            {
                BuildDetail = new BuildDetail { DropLocation = "catalog://bundled" },
                Properties = new List<PropertyPair>
                {
                    new(TerraformDestroyOperationConstants.DeferredValidationComponentsPropertyName, "SmokeValidation"),
                    new(TerraformDestroyOperationConstants.DeferredValidationBuildUrlPropertyName, "file:///drops/val")
                }
            };
            ephEnv.EphemeralState = EphemeralEnvironmentStates.Provisioning;
            sut.UpdateEphemeralEnvironmentLifecycleState(
                ephEnv,
                deferredFileUrlDetail,
                isDestroyRequest: false,
                DeploymentRequestStatus.Completed,
                sourceRequest: new DeploymentRequestApiModel { UserName = "alice", DropLocation = "dorc-catalog://bundled", BuildUri = "vstfs:///Build/Build/88" });

            var httpArtifactsDropDetail = new DeploymentRequestDetail
            {
                BuildDetail = new BuildDetail { DropLocation = "vstfs:///Build/Build/88" },
                Properties = new List<PropertyPair>
                {
                    new(TerraformDestroyOperationConstants.DeferredValidationComponentsPropertyName, "SmokeValidation"),
                    new(TerraformDestroyOperationConstants.DeferredValidationBuildUrlPropertyName, "https://dev.azure.com/org/proj/_apis/build/Builds/88/artifacts")
                }
            };
            ephEnv.EphemeralState = EphemeralEnvironmentStates.Provisioning;
            sut.UpdateEphemeralEnvironmentLifecycleState(
                ephEnv,
                httpArtifactsDropDetail,
                isDestroyRequest: false,
                DeploymentRequestStatus.Completed,
                sourceRequest: new DeploymentRequestApiModel { UserName = "alice" });

            var plainHttpDropDetail = new DeploymentRequestDetail
            {
                BuildDetail = new BuildDetail(),
                Properties = new List<PropertyPair>
                {
                    new(TerraformDestroyOperationConstants.DeferredValidationComponentsPropertyName, "SmokeValidation"),
                    new(TerraformDestroyOperationConstants.DeferredValidationBuildUrlPropertyName, "http://artifacts.internal/drop/88")
                }
            };
            ephEnv.EphemeralState = EphemeralEnvironmentStates.Provisioning;
            sut.UpdateEphemeralEnvironmentLifecycleState(
                ephEnv,
                plainHttpDropDetail,
                isDestroyRequest: false,
                DeploymentRequestStatus.Completed,
                sourceRequest: new DeploymentRequestApiModel { UserName = "alice" });

            var whitespaceDeferredDetail = new DeploymentRequestDetail
            {
                Properties = new List<PropertyPair>
                {
                    new(TerraformDestroyOperationConstants.DeferredValidationComponentsPropertyName, "   ")
                }
            };
            ephEnv.EphemeralState = EphemeralEnvironmentStates.Provisioning;
            sut.UpdateEphemeralEnvironmentLifecycleState(
                ephEnv,
                whitespaceDeferredDetail,
                isDestroyRequest: false,
                DeploymentRequestStatus.Completed,
                sourceRequest: new DeploymentRequestApiModel { UserName = "alice" });

            // 3) Cover deferredBuildUrl with file:// and UNC path \\ so dropLocation is populated from deferredBuildUrl
            var fileDeferredDetail = new DeploymentRequestDetail
            {
                Properties = new List<PropertyPair>
                {
                    new(TerraformDestroyOperationConstants.DeferredValidationComponentsPropertyName, "SmokeValidation"),
                    new(TerraformDestroyOperationConstants.DeferredValidationBuildUrlPropertyName, "file:///drops/val")
                }
            };
            ephEnv.EphemeralState = EphemeralEnvironmentStates.Provisioning;
            sut.UpdateEphemeralEnvironmentLifecycleState(
                ephEnv,
                fileDeferredDetail,
                isDestroyRequest: false,
                DeploymentRequestStatus.Completed,
                sourceRequest: new DeploymentRequestApiModel { UserName = "alice" });

            var uncDeferredDetail = new DeploymentRequestDetail
            {
                Properties = new List<PropertyPair>
                {
                    new(TerraformDestroyOperationConstants.DeferredValidationComponentsPropertyName, "SmokeValidation"),
                    new(TerraformDestroyOperationConstants.DeferredValidationBuildUrlPropertyName, @"\\server\share\val")
                }
            };
            ephEnv.EphemeralState = EphemeralEnvironmentStates.Provisioning;
            sut.UpdateEphemeralEnvironmentLifecycleState(
                ephEnv,
                uncDeferredDetail,
                isDestroyRequest: false,
                DeploymentRequestStatus.Completed,
                sourceRequest: new DeploymentRequestApiModel { UserName = "alice" });

            // 4) Cover HasRemainingActiveDestroyRequests during multi-project destroy completion
            var destroyDetail = new DeploymentRequestDetail
            {
                Properties = new List<PropertyPair>
                {
                    new(TerraformDestroyOperationConstants.PropertyName, TerraformDestroyOperationConstants.DestroyValue)
                }
            };
            var destroyXml = new DeploymentRequestDetailSerializer().Serialize(destroyDetail);
            mockEnvironmentsPersistentSource.ClearReceivedCalls();
            mockRequestsPersistentSource.GetRequestsWithStatus(
                DeploymentRequestStatus.Pending,
                DeploymentRequestStatus.Running,
                DeploymentRequestStatus.Requesting,
                DeploymentRequestStatus.WaitingConfirmation,
                false)
                .Returns(new[]
                {
                    new DeploymentRequestApiModel { Id = 500, EnvironmentName = "eph-pr-70", RequestDetails = destroyXml },
                    new DeploymentRequestApiModel { Id = 501, EnvironmentName = "other-env", RequestDetails = destroyXml },
                    new DeploymentRequestApiModel { Id = 502, EnvironmentName = "eph-pr-70", RequestDetails = "<DeploymentRequestDetail />" },
                    new DeploymentRequestApiModel { Id = 503, EnvironmentName = "eph-pr-70", RequestDetails = destroyXml, Status = DeploymentRequestStatus.Running.ToString() },
                    new DeploymentRequestApiModel { Id = 504, EnvironmentName = "eph-pr-70", RequestDetails = destroyXml, Status = DeploymentRequestStatus.Requesting.ToString() },
                    new DeploymentRequestApiModel { Id = 505, EnvironmentName = "eph-pr-70", RequestDetails = destroyXml, Status = DeploymentRequestStatus.Pending.ToString() }
                });
            mockRequestsPersistentSource.GetRequestsWithStatus(
                DeploymentRequestStatus.Confirmed,
                DeploymentRequestStatus.Paused,
                false)
                .Returns(new[]
                {
                    new DeploymentRequestApiModel { Id = 506, EnvironmentName = "eph-pr-70", RequestDetails = destroyXml, Status = DeploymentRequestStatus.Confirmed.ToString() }
                });
            sut.UpdateEphemeralEnvironmentLifecycleState(
                ephEnv,
                destroyDetail,
                isDestroyRequest: true,
                DeploymentRequestStatus.Completed,
                sourceRequest: new DeploymentRequestApiModel { Id = 500 });
            mockEnvironmentsPersistentSource.DidNotReceive().DeleteEnvironmentInternal(70);

            // When a sibling destroy request fails, CancelRemainingActiveDestroyRequests cancels all remaining active sibling destroy batches
            sut.UpdateEphemeralEnvironmentLifecycleState(
                ephEnv,
                destroyDetail,
                isDestroyRequest: true,
                DeploymentRequestStatus.Failed,
                sourceRequest: new DeploymentRequestApiModel { Id = 500 });
            mockRequestsPersistentSource.Received().UpdateRequestStatus(503, DeploymentRequestStatus.Cancelling, "system:ephemeral-teardown", Arg.Any<DateTimeOffset>());
            mockRequestsPersistentSource.Received().UpdateRequestStatus(504, DeploymentRequestStatus.Cancelling, "system:ephemeral-teardown", Arg.Any<DateTimeOffset>());
            mockRequestsPersistentSource.Received().UpdateRequestStatus(505, DeploymentRequestStatus.Cancelled, "system:ephemeral-teardown", Arg.Any<DateTimeOffset>());
            mockRequestsPersistentSource.Received().UpdateRequestStatus(506, DeploymentRequestStatus.Cancelled, "system:ephemeral-teardown", Arg.Any<DateTimeOffset>());

            // If environment is already TeardownFailed (or became TeardownFailed in DB) when a sibling destroy batch completes, it does not delete or overwrite TeardownFailed
            var teardownFailedEnv = new EnvironmentApiModel
            {
                EnvironmentId = 72,
                EnvironmentName = "eph-pr-72",
                IsEphemeral = true,
                EphemeralState = EphemeralEnvironmentStates.TearingDown
            };
            mockEnvironmentsPersistentSource.GetEnvironment("eph-pr-72").Returns(new EnvironmentApiModel
            {
                EnvironmentId = 72,
                EnvironmentName = "eph-pr-72",
                IsEphemeral = true,
                EphemeralState = EphemeralEnvironmentStates.TeardownFailed
            });
            mockRequestsPersistentSource.GetRequestsWithStatus(
                DeploymentRequestStatus.Pending,
                DeploymentRequestStatus.Running,
                DeploymentRequestStatus.Requesting,
                DeploymentRequestStatus.WaitingConfirmation,
                false)
                .Returns(Array.Empty<DeploymentRequestApiModel>());
            mockRequestsPersistentSource.GetRequestsWithStatus(
                DeploymentRequestStatus.Confirmed,
                DeploymentRequestStatus.Paused,
                false)
                .Returns(Array.Empty<DeploymentRequestApiModel>());
            sut.UpdateEphemeralEnvironmentLifecycleState(
                teardownFailedEnv,
                destroyDetail,
                isDestroyRequest: true,
                DeploymentRequestStatus.Completed,
                sourceRequest: new DeploymentRequestApiModel { Id = 500 });
            mockEnvironmentsPersistentSource.DidNotReceive().DeleteEnvironmentInternal(72);

            // Cover multi-project DestroyBatchCount and SiblingDestroyRequestIds coordination:
            // a) Batch 1 finishes before Batch 2 exists in DB -> waits (does not delete)
            ephEnv.EphemeralState = EphemeralEnvironmentStates.TearingDown;
            var multiBatchFirstDetail = new DeploymentRequestDetail
            {
                Properties = new List<PropertyPair>
                {
                    new(TerraformDestroyOperationConstants.PropertyName, TerraformDestroyOperationConstants.DestroyValue),
                    new(TerraformDestroyOperationConstants.DestroyBatchCountPropertyName, "2")
                }
            };
            mockRequestsPersistentSource.GetRequest(601).Returns((DeploymentRequestApiModel)null!);
            sut.UpdateEphemeralEnvironmentLifecycleState(
                ephEnv,
                multiBatchFirstDetail,
                isDestroyRequest: true,
                DeploymentRequestStatus.Completed,
                sourceRequest: new DeploymentRequestApiModel { Id = 600 });
            mockEnvironmentsPersistentSource.DidNotReceive().DeleteEnvironmentInternal(70);

            // b) Batch 1 finishes when Batch 2 (601) exists via GetRequest probe, first while Running (waits), then when Completed (finalizes)
            mockRequestsPersistentSource.GetRequest(601).Returns(new DeploymentRequestApiModel
            {
                Id = 601,
                EnvironmentName = "eph-pr-70",
                RequestDetails = destroyXml
            });
            mockRequestsPersistentSource.GetRequestStatus(601).Returns(
                new RequestStatusDto { Id = 601, Status = DeploymentRequestStatus.Running.ToString() },
                (RequestStatusDto)null!,
                new RequestStatusDto { Id = 601, Status = DeploymentRequestStatus.Completed.ToString() });
            sut.UpdateEphemeralEnvironmentLifecycleState(
                ephEnv,
                multiBatchFirstDetail,
                isDestroyRequest: true,
                DeploymentRequestStatus.Completed,
                sourceRequest: new DeploymentRequestApiModel { Id = 600 });
            sut.UpdateEphemeralEnvironmentLifecycleState(
                ephEnv,
                multiBatchFirstDetail,
                isDestroyRequest: true,
                DeploymentRequestStatus.Completed,
                sourceRequest: new DeploymentRequestApiModel { Id = 600 });
            mockEnvironmentsPersistentSource.DidNotReceive().DeleteEnvironmentInternal(70);

            // c) Batch 2 with explicit SiblingDestroyRequestIds ("600,invalid,602,0") when all siblings are Completed -> finalizes
            var multiBatchSecondDetail = new DeploymentRequestDetail
            {
                Properties = new List<PropertyPair>
                {
                    new(TerraformDestroyOperationConstants.PropertyName, TerraformDestroyOperationConstants.DestroyValue),
                    new(TerraformDestroyOperationConstants.DestroyBatchCountPropertyName, "2"),
                    new(TerraformDestroyOperationConstants.SiblingDestroyRequestIdsPropertyName, "600,invalid,602,0")
                }
            };
            mockRequestsPersistentSource.GetRequestStatus(600).Returns(new RequestStatusDto { Id = 600, Status = DeploymentRequestStatus.Completed.ToString() });
            mockEnvironmentsPersistentSource.GetEnvironment("eph-pr-70").Returns(new EnvironmentApiModel
            {
                EnvironmentId = 70,
                EnvironmentName = "eph-pr-70",
                IsEphemeral = true,
                EphemeralState = "   "
            });
            sut.UpdateEphemeralEnvironmentLifecycleState(
                ephEnv,
                multiBatchSecondDetail,
                isDestroyRequest: true,
                DeploymentRequestStatus.Completed,
                sourceRequest: new DeploymentRequestApiModel { Id = 602 });
            mockEnvironmentsPersistentSource.Received(1).DeleteEnvironmentInternal(70);
            mockEnvironmentsPersistentSource.ClearReceivedCalls();

            // d) DestroyBatchCount = "1" or invalid -> treated as single-batch
            var singleBatchCountDetail = new DeploymentRequestDetail
            {
                Properties = new List<PropertyPair>
                {
                    new(TerraformDestroyOperationConstants.PropertyName, TerraformDestroyOperationConstants.DestroyValue),
                    new(TerraformDestroyOperationConstants.DestroyBatchCountPropertyName, "1")
                }
            };
            sut.UpdateEphemeralEnvironmentLifecycleState(
                ephEnv,
                singleBatchCountDetail,
                isDestroyRequest: true,
                DeploymentRequestStatus.Completed,
                sourceRequest: new DeploymentRequestApiModel { Id = 605 });
            sut.UpdateEphemeralEnvironmentLifecycleState(
                ephEnv,
                null,
                isDestroyRequest: true,
                DeploymentRequestStatus.Completed,
                sourceRequest: new DeploymentRequestApiModel { Id = 605 });
            sut.UpdateEphemeralEnvironmentLifecycleState(
                ephEnv,
                new DeploymentRequestDetail { Properties = null! },
                isDestroyRequest: true,
                DeploymentRequestStatus.Completed,
                sourceRequest: new DeploymentRequestApiModel { Id = 605 });
            sut.UpdateEphemeralEnvironmentLifecycleState(
                ephEnv,
                new DeploymentRequestDetail { Properties = null! },
                isDestroyRequest: true,
                DeploymentRequestStatus.Completed,
                sourceRequest: new DeploymentRequestApiModel { Id = 0 });
            mockEnvironmentsPersistentSource.ClearReceivedCalls();

            // And when GetRequestsWithStatus returns null or empty environment name, DeleteEnvironmentInternal proceeds
            ephEnv.EphemeralState = EphemeralEnvironmentStates.TearingDown;
            mockEnvironmentsPersistentSource.GetEnvironment("eph-pr-70").Returns((EnvironmentApiModel)null!);
            mockRequestsPersistentSource.GetRequestsWithStatus(
                DeploymentRequestStatus.Pending,
                DeploymentRequestStatus.Running,
                DeploymentRequestStatus.Requesting,
                DeploymentRequestStatus.WaitingConfirmation,
                false)
                .Returns((IEnumerable<DeploymentRequestApiModel>)null!);
            mockRequestsPersistentSource.GetRequestsWithStatus(
                DeploymentRequestStatus.Confirmed,
                DeploymentRequestStatus.Paused,
                false)
                .Returns((IEnumerable<DeploymentRequestApiModel>)null!);
            sut.UpdateEphemeralEnvironmentLifecycleState(
                ephEnv,
                destroyDetail,
                isDestroyRequest: true,
                DeploymentRequestStatus.Completed,
                sourceRequest: new DeploymentRequestApiModel { Id = 500 });
            mockEnvironmentsPersistentSource.Received(1).DeleteEnvironmentInternal(70);

            sut.UpdateEphemeralEnvironmentLifecycleState(
                new EnvironmentApiModel { EnvironmentId = 71, EnvironmentName = "", IsEphemeral = true },
                destroyDetail,
                isDestroyRequest: true,
                DeploymentRequestStatus.Completed,
                sourceRequest: new DeploymentRequestApiModel { Id = 500 });
            sut.UpdateEphemeralEnvironmentLifecycleState(
                new EnvironmentApiModel { EnvironmentId = 71, EnvironmentName = "", IsEphemeral = true },
                destroyDetail,
                isDestroyRequest: true,
                DeploymentRequestStatus.Failed,
                sourceRequest: new DeploymentRequestApiModel { Id = 500 });
        }

        [TestMethod]
        public void OrderComponentsForExecution_And_IsDestroyRequest_HandleAllBranches()
        {
            var c1 = new ComponentApiModel { ComponentId = 1, ComponentName = "Infra-Postgres" };
            var c2 = new ComponentApiModel { ComponentId = 2, ComponentName = "Infra-Kafka" };
            var list = new List<ComponentApiModel> { c1, c2 };

            CollectionAssert.AreEqual(new[] { c1, c2 }, PendingRequestProcessor.OrderComponentsForExecution(list, false).ToList());
            CollectionAssert.AreEqual(new[] { c2, c1 }, PendingRequestProcessor.OrderComponentsForExecution(list, true).ToList());
            CollectionAssert.AreEqual(new[] { c1 }, PendingRequestProcessor.OrderComponentsForExecution(new List<ComponentApiModel> { c1 }, true).ToList());

            var cNoName = new ComponentApiModel { ComponentId = 3, ComponentName = "" };
            var cUnlisted = new ComponentApiModel { ComponentId = 4, ComponentName = "Infra-Unlisted" };
            var detailWithOrder = new DeploymentRequestDetail
            {
                Components = new List<string> { "Infra-Kafka", "", "Infra-Kafka", "Infra-Postgres" }
            };
            // Non-destroy request without explicit ephemeral order preserves submittedComponents order
            CollectionAssert.AreEqual(
                new[] { c1, c2 },
                PendingRequestProcessor.OrderComponentsForExecution(list, false, detailWithOrder).ToList());
            CollectionAssert.AreEqual(
                new[] { c1, c2 },
                PendingRequestProcessor.OrderComponentsForExecution(list, false, new DeploymentRequestDetail
                {
                    Components = new List<string> { "Infra-Kafka", "Infra-Postgres" },
                    Properties = new List<PropertyPair> { new("OtherProp", "val") }
                }).ToList());
            // Non-destroy request with EphemeralTargetState or DeferredValidationComponents honors requestDetail.Components order
            CollectionAssert.AreEqual(
                new[] { c2, c1 },
                PendingRequestProcessor.OrderComponentsForExecution(list, false, new DeploymentRequestDetail
                {
                    Components = new List<string> { "Infra-Kafka", "Infra-Postgres" },
                    Properties = new List<PropertyPair>
                    {
                        new(TerraformDestroyOperationConstants.EphemeralTargetStatePropertyName, EphemeralEnvironmentStates.Ready)
                    }
                }).ToList());
            CollectionAssert.AreEqual(
                new[] { c2, c1 },
                PendingRequestProcessor.OrderComponentsForExecution(list, false, new DeploymentRequestDetail
                {
                    Components = new List<string> { "Infra-Kafka", "Infra-Postgres" },
                    Properties = new List<PropertyPair>
                    {
                        new(TerraformDestroyOperationConstants.DeferredValidationComponentsPropertyName, "2")
                    }
                }).ToList());
            CollectionAssert.AreEqual(
                new[] { cUnlisted, cNoName, c1, c2 },
                PendingRequestProcessor.OrderComponentsForExecution(new List<ComponentApiModel> { c1, c2, cNoName, cUnlisted }, true, detailWithOrder).ToList());

            var detailPreReversed = new DeploymentRequestDetail
            {
                Components = new List<string> { "Infra-Kafka", "Infra-Postgres" },
                Properties = new List<PropertyPair>
                {
                    new("OtherProp", "val"),
                    new(TerraformDestroyOperationConstants.DestroyOrderPreReversedPropertyName, "false"),
                    new(TerraformDestroyOperationConstants.DestroyOrderPreReversedPropertyName, "true")
                }
            };
            CollectionAssert.AreEqual(
                new[] { c2, c1 },
                PendingRequestProcessor.OrderComponentsForExecution(list, true, detailPreReversed).ToList());

            var cNullId = new ComponentApiModel { ComponentId = null, ComponentName = "Infra-Postgres" };
            CollectionAssert.AreEqual(
                new[] { c2, c1, cNullId },
                PendingRequestProcessor.OrderComponentsForExecution(
                    new List<ComponentApiModel> { c1, c2, cNullId },
                    true,
                    new DeploymentRequestDetail
                    {
                        Components = null!,
                        Properties = new List<PropertyPair>
                        {
                            new(TerraformDestroyOperationConstants.DestroyOrderPreReversedPropertyName, "true"),
                            new(TerraformDestroyOperationConstants.DestroyComponentIdsPropertyName, "2,1")
                        }
                    }).ToList());

            Assert.IsFalse(PendingRequestProcessor.IsDestroyRequest(null));
            Assert.IsFalse(PendingRequestProcessor.IsDestroyRequest(new DeploymentRequestDetail { Properties = null! }));
            Assert.IsFalse(PendingRequestProcessor.IsDestroyRequest(new DeploymentRequestDetail
            {
                Properties = new List<PropertyPair>
                {
                    new("", "Destroy"),
                    new("TerraformOperation", ""),
                    new("TerraformOperation", "Plan"),
                    new("IsDestroy", "false")
                }
            }));
            Assert.IsTrue(PendingRequestProcessor.IsDestroyRequest(new DeploymentRequestDetail
            {
                Properties = new List<PropertyPair> { new("TerraformOperation", "Destroy") }
            }));
            Assert.IsTrue(PendingRequestProcessor.IsDestroyRequest(new DeploymentRequestDetail
            {
                Properties = new List<PropertyPair> { new("IsDestroy", "true") }
            }));
            Assert.IsFalse(PendingRequestProcessor.IsDestroyRequest(
                null,
                new EnvironmentApiModel { IsEphemeral = true, EphemeralState = EphemeralEnvironmentStates.TearingDown }));
        }

        [DataTestMethod]
        [DataRow(false, EphemeralEnvironmentStates.Ready)]
        [DataRow(true, EphemeralEnvironmentStates.Testing)]
        public void Execute_EphemeralProvisioningSuccess_TransitionsStateToReadyOrTesting(bool validationTriggered, string expectedState)
        {
            mockEnvironmentsPersistentSource.GetEnvironment(Arg.Any<string>())
                .Returns(new EnvironmentApiModel
                {
                    EnvironmentId = 50,
                    EnvironmentName = "eph-pr-50",
                    IsEphemeral = true,
                    EphemeralState = EphemeralEnvironmentStates.Provisioning
                });

            var comp = new ComponentApiModel { ComponentId = 1, ComponentName = "Infra-Postgres", IsEnabled = true };
            SetupOrderedComponents(new List<ComponentApiModel> { comp });
            mockComponentProcessor.DeployComponent(
                comp, Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<bool>(),
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IDictionary<string, VariableValue>>(), Arg.Any<CancellationToken>())
                .Returns(true);

            var dto = CreateRequest(new List<ComponentApiModel> { comp });
            dto.Details.Properties = validationTriggered
                ? new List<PropertyPair> { new("RunValidationRequest", "true") }
                : new List<PropertyPair> { new(TerraformDestroyOperationConstants.EphemeralTargetStatePropertyName, EphemeralEnvironmentStates.Ready) };

            sut.Execute(dto, CancellationToken.None, mockLoggerFactory);

            mockEnvironmentsPersistentSource.Received(1).TryTransitionEphemeralState(
                "eph-pr-50",
                Arg.Any<IReadOnlyCollection<string>>(),
                expectedState);
        }

        [TestMethod]
        public void Execute_EphemeralTeardownFailure_TransitionsToTeardownFailedWithoutDeletingEnvironment()
        {
            var env = new EnvironmentApiModel
            {
                EnvironmentId = 51,
                EnvironmentName = "eph-pr-51",
                IsEphemeral = true,
                EphemeralState = EphemeralEnvironmentStates.TearingDown
            };
            mockEnvironmentsPersistentSource.GetEnvironment(Arg.Any<string>()).Returns(env);

            // Even with StopOnFailure = false, destroy request MUST stop on first component failure and filter out non-Terraform components!
            var kafka = new ComponentApiModel { ComponentId = 1, ComponentName = "Infra-Kafka", IsEnabled = true, StopOnFailure = false, ComponentType = ComponentType.Terraform };
            var postgres = new ComponentApiModel { ComponentId = 2, ComponentName = "Infra-Postgres", IsEnabled = true, StopOnFailure = false, ComponentType = ComponentType.Terraform };
            var psComp = new ComponentApiModel { ComponentId = 3, ComponentName = "Ps-Hook", IsEnabled = true, StopOnFailure = false, ComponentType = ComponentType.PowerShell };
            SetupOrderedComponents(new List<ComponentApiModel> { postgres, kafka, psComp });

            // Infra-Kafka fails during destroy
            mockComponentProcessor.DeployComponent(
                kafka, Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<bool>(),
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IDictionary<string, VariableValue>>(), Arg.Any<CancellationToken>())
                .Returns(false);

            var dto = CreateRequest(new List<ComponentApiModel> { postgres, kafka, psComp });
            dto.Details.Properties = new List<PropertyPair>
            {
                new("TerraformOperation", "Destroy"),
                new("TeardownPolicy", "DeleteEnvironment")
            };

            sut.Execute(dto, CancellationToken.None, mockLoggerFactory);

            // Infra-Postgres and Ps-Hook were never deployed because Kafka failed first during destroy (and Ps-Hook is non-Terraform)
            mockComponentProcessor.DidNotReceive().DeployComponent(
                postgres, Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<bool>(),
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IDictionary<string, VariableValue>>(), Arg.Any<CancellationToken>());
            mockComponentProcessor.DidNotReceive().DeployComponent(
                psComp, Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<bool>(),
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IDictionary<string, VariableValue>>(), Arg.Any<CancellationToken>());
            mockEnvironmentsPersistentSource.Received(1).UpdateEphemeralState(51, EphemeralEnvironmentStates.TeardownFailed);
            mockEnvironmentsPersistentSource.DidNotReceiveWithAnyArgs().DeleteEnvironment(default!, default!);

            // Re-running Execute when env is already TeardownFailed does not reset state to TearingDown at request start
            env.EphemeralState = EphemeralEnvironmentStates.TeardownFailed;
            sut.Execute(dto, CancellationToken.None, mockLoggerFactory);
            mockEnvironmentsPersistentSource.DidNotReceive().UpdateEphemeralState(51, EphemeralEnvironmentStates.TearingDown);
        }

        [DataTestMethod]
        [DataRow("KeepRecord", false)]
        [DataRow("DeleteEnvironment", true)]
        public void Execute_EphemeralTeardownSuccess_TransitionsToTornDownAndAppliesTeardownPolicy(string policy, bool shouldDelete)
        {
            var env = new EnvironmentApiModel
            {
                EnvironmentId = 52,
                EnvironmentName = "eph-pr-52",
                IsEphemeral = true,
                EphemeralState = EphemeralEnvironmentStates.Ready
            };
            mockEnvironmentsPersistentSource.GetEnvironment(Arg.Any<string>()).Returns(env);
            mockEnvironmentsPersistentSource.DeleteEnvironmentInternal(52).Returns(true);

            var comp = new ComponentApiModel { ComponentId = 1, ComponentName = "Infra-Postgres", IsEnabled = true, ComponentType = ComponentType.Terraform };
            SetupOrderedComponents(new List<ComponentApiModel> { comp });
            mockComponentProcessor.DeployComponent(
                comp, Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<bool>(),
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IDictionary<string, VariableValue>>(), Arg.Any<CancellationToken>())
                .Returns(true);

            var dto = CreateRequest(new List<ComponentApiModel> { comp });
            dto.Details.Properties = new List<PropertyPair>
            {
                new("TerraformOperation", "Destroy"),
                new("TeardownPolicy", policy)
            };

            sut.Execute(dto, CancellationToken.None, mockLoggerFactory);

            if (shouldDelete)
            {
                mockEnvironmentsPersistentSource.Received(1).DeleteEnvironmentInternal(52);
            }
            else
            {
                mockEnvironmentsPersistentSource.DidNotReceive().DeleteEnvironmentInternal(Arg.Any<int>());
                mockEnvironmentsPersistentSource.Received(1).UpdateEphemeralState(52, EphemeralEnvironmentStates.TornDown);
            }
        }

        [TestMethod]
        public void UpdateEphemeralEnvironmentLifecycleState_HandlesEdgeCases()
        {
            sut.UpdateEphemeralEnvironmentLifecycleState(null, null, false, DeploymentRequestStatus.Completed);
            sut.UpdateEphemeralEnvironmentLifecycleState(
                new EnvironmentApiModel { EnvironmentId = 1, IsEphemeral = false }, null, false, DeploymentRequestStatus.Completed);

            var env = new EnvironmentApiModel
            {
                EnvironmentId = 60,
                EnvironmentName = "eph-60",
                IsEphemeral = true,
                EphemeralState = EphemeralEnvironmentStates.Provisioning
            };
            // WaitingConfirmation does not transition state yet (for both provision and destroy)
            sut.UpdateEphemeralEnvironmentLifecycleState(env, null, false, DeploymentRequestStatus.WaitingConfirmation);
            sut.UpdateEphemeralEnvironmentLifecycleState(env, null, true, DeploymentRequestStatus.WaitingConfirmation);
            // Non-lifecycle request with non-empty Properties does not transition ephemeral lifecycle state
            Assert.AreEqual(
                DeploymentRequestStatus.Completed,
                sut.UpdateEphemeralEnvironmentLifecycleState(
                    env,
                    new DeploymentRequestDetail
                    {
                        Properties = new List<PropertyPair> { new("OrdinaryProperty", "val") }
                    },
                    false,
                    DeploymentRequestStatus.Completed));
            mockEnvironmentsPersistentSource.DidNotReceive().UpdateEphemeralState(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<DateTime?>());

            // Destroy + Errored, Cancelled, or Abandoned transitions to TeardownFailed
            sut.UpdateEphemeralEnvironmentLifecycleState(env, null, true, DeploymentRequestStatus.Errored);
            sut.UpdateEphemeralEnvironmentLifecycleState(env, null, true, DeploymentRequestStatus.Cancelled);
            sut.UpdateEphemeralEnvironmentLifecycleState(env, null, true, DeploymentRequestStatus.Abandoned);
            mockEnvironmentsPersistentSource.Received(3).UpdateEphemeralState(60, EphemeralEnvironmentStates.TeardownFailed);

            // Provisioning failure transitions to ProvisionFailed; Testing failure transitions to ValidationFailed; Ready failure does not transition
            var lifecycleReadyDetail = new DeploymentRequestDetail
            {
                Properties = new List<PropertyPair>
                {
                    new(TerraformDestroyOperationConstants.EphemeralTargetStatePropertyName, EphemeralEnvironmentStates.Ready)
                }
            };
            env.EphemeralState = EphemeralEnvironmentStates.Provisioning;
            Assert.AreEqual(DeploymentRequestStatus.Failed, sut.UpdateEphemeralEnvironmentLifecycleState(env, lifecycleReadyDetail, false, DeploymentRequestStatus.Failed));
            Assert.AreEqual(EphemeralEnvironmentStates.ProvisionFailed, env.EphemeralState);

            env.EphemeralState = EphemeralEnvironmentStates.Testing;
            Assert.AreEqual(DeploymentRequestStatus.Cancelled, sut.UpdateEphemeralEnvironmentLifecycleState(env, lifecycleReadyDetail, false, DeploymentRequestStatus.Cancelled));
            Assert.AreEqual(EphemeralEnvironmentStates.ValidationFailed, env.EphemeralState);

            env.EphemeralState = EphemeralEnvironmentStates.Ready;
            Assert.AreEqual(DeploymentRequestStatus.Errored, sut.UpdateEphemeralEnvironmentLifecycleState(env, lifecycleReadyDetail, false, DeploymentRequestStatus.Errored));
            Assert.AreEqual(EphemeralEnvironmentStates.Ready, env.EphemeralState);

            env.EphemeralState = EphemeralEnvironmentStates.Provisioning;
            mockEnvironmentsPersistentSource.TryTransitionEphemeralState("eph-60", Arg.Any<IReadOnlyCollection<string>>(), EphemeralEnvironmentStates.ProvisionFailed)
                .Returns(false);
            Assert.AreEqual(DeploymentRequestStatus.Abandoned, sut.UpdateEphemeralEnvironmentLifecycleState(env, lifecycleReadyDetail, false, DeploymentRequestStatus.Abandoned));
            Assert.AreEqual(EphemeralEnvironmentStates.Provisioning, env.EphemeralState);

            env.EphemeralState = EphemeralEnvironmentStates.Testing;
            mockEnvironmentsPersistentSource.TryTransitionEphemeralState("eph-60", Arg.Any<IReadOnlyCollection<string>>(), EphemeralEnvironmentStates.ValidationFailed)
                .Returns(false);
            Assert.AreEqual(DeploymentRequestStatus.Abandoned, sut.UpdateEphemeralEnvironmentLifecycleState(env, lifecycleReadyDetail, false, DeploymentRequestStatus.Abandoned));
            Assert.AreEqual(EphemeralEnvironmentStates.Testing, env.EphemeralState);
            mockEnvironmentsPersistentSource.TryTransitionEphemeralState(Arg.Any<string>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<string>())
                .Returns(true);

            // Provisioning completed transitions to Ready
            env.EphemeralState = "";
            sut.UpdateEphemeralEnvironmentLifecycleState(env, lifecycleReadyDetail, false, DeploymentRequestStatus.Completed);
            Assert.AreEqual(EphemeralEnvironmentStates.Ready, env.EphemeralState);

            // Already Ready does not transition again when target is Ready
            sut.UpdateEphemeralEnvironmentLifecycleState(env, lifecycleReadyDetail, false, DeploymentRequestStatus.Completed);

            // Ready -> Testing transition when validation request completes (and false branch when TryTransitionEphemeralState returns false)
            var testingDetail = new DeploymentRequestDetail
            {
                Properties = new List<PropertyPair> { new("EphemeralTargetState", EphemeralEnvironmentStates.Testing) }
            };
            mockEnvironmentsPersistentSource.TryTransitionEphemeralState("eph-60", Arg.Any<IReadOnlyCollection<string>>(), EphemeralEnvironmentStates.Testing)
                .Returns(false);
            sut.UpdateEphemeralEnvironmentLifecycleState(env, testingDetail, false, DeploymentRequestStatus.Completed);
            Assert.AreEqual(EphemeralEnvironmentStates.Ready, env.EphemeralState);
            mockEnvironmentsPersistentSource.TryTransitionEphemeralState("eph-60", Arg.Any<IReadOnlyCollection<string>>(), EphemeralEnvironmentStates.Testing)
                .Returns(true);
            sut.UpdateEphemeralEnvironmentLifecycleState(env, testingDetail, false, DeploymentRequestStatus.Completed);
            Assert.AreEqual(EphemeralEnvironmentStates.Testing, env.EphemeralState);

            // Testing -> Ready transition when subsequent request completes (and false branch when TryTransitionEphemeralState returns false, e.g. TornDown/TearingDown)
            mockEnvironmentsPersistentSource.TryTransitionEphemeralState("eph-60", Arg.Any<IReadOnlyCollection<string>>(), EphemeralEnvironmentStates.Ready)
                .Returns(false);
            sut.UpdateEphemeralEnvironmentLifecycleState(env, lifecycleReadyDetail, false, DeploymentRequestStatus.Completed);
            Assert.AreEqual(EphemeralEnvironmentStates.Testing, env.EphemeralState);
            mockEnvironmentsPersistentSource.TryTransitionEphemeralState("eph-60", Arg.Any<IReadOnlyCollection<string>>(), EphemeralEnvironmentStates.Ready)
                .Returns(true);
            sut.UpdateEphemeralEnvironmentLifecycleState(env, lifecycleReadyDetail, false, DeploymentRequestStatus.Completed);
            Assert.AreEqual(EphemeralEnvironmentStates.Ready, env.EphemeralState);

            env.EphemeralState = EphemeralEnvironmentStates.Provisioning;
            sut.UpdateEphemeralEnvironmentLifecycleState(
                env,
                new DeploymentRequestDetail { Properties = null! },
                false,
                DeploymentRequestStatus.Completed);

            env.EphemeralState = EphemeralEnvironmentStates.Provisioning;
            sut.UpdateEphemeralEnvironmentLifecycleState(
                env,
                new DeploymentRequestDetail
                {
                    Properties = new List<PropertyPair>
                    {
                        new("RunValidationRequest", "false"),
                        new("EphemeralTargetState", EphemeralEnvironmentStates.Ready)
                    }
                },
                false,
                DeploymentRequestStatus.Completed);

            // Destroy completed with DeleteEnvironmentOnTeardown = false or TeardownPolicy = KeepRecord does not delete environment
            sut.UpdateEphemeralEnvironmentLifecycleState(
                env,
                new DeploymentRequestDetail { Properties = new List<PropertyPair> { new("DeleteEnvironmentOnTeardown", "false") } },
                true,
                DeploymentRequestStatus.Completed);
            mockEnvironmentsPersistentSource.DidNotReceive().DeleteEnvironmentInternal(Arg.Any<int>());

            // Destroy completed with null requestDetail / null Properties / DeleteEnvironmentOnTeardown = true defaults to DeleteEnvironmentInternal
            // First: DeleteEnvironmentInternal returns false -> transitions to TeardownFailed and returns DeploymentRequestStatus.Failed (so Execute marks request Failed)
            mockEnvironmentsPersistentSource.DeleteEnvironmentInternal(60).Returns(false);
            Assert.AreEqual(DeploymentRequestStatus.Failed, sut.UpdateEphemeralEnvironmentLifecycleState(env, null, true, DeploymentRequestStatus.Completed));
            mockEnvironmentsPersistentSource.Received(1).DeleteEnvironmentInternal(60);
            Assert.AreEqual(EphemeralEnvironmentStates.TeardownFailed, env.EphemeralState);

            // Also verify Execute marks request as Failed when UpdateEphemeralEnvironmentLifecycleState returns Failed during Destroy
            env.EphemeralState = EphemeralEnvironmentStates.TearingDown;
            var delFailComp = new ComponentApiModel { ComponentId = 1, ComponentName = "Infra-Postgres", IsEnabled = true, ComponentType = ComponentType.Terraform };
            SetupOrderedComponents(new List<ComponentApiModel> { delFailComp });
            mockComponentProcessor.DeployComponent(
                delFailComp, Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<bool>(),
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IDictionary<string, VariableValue>>(), Arg.Any<CancellationToken>())
                .Returns(true);
            mockEnvironmentsPersistentSource.GetEnvironment("TestEnv").Returns(env);
            var delFailDto = CreateRequest(new List<ComponentApiModel> { delFailComp });
            delFailDto.Details.Properties = new List<PropertyPair> { new("TerraformOperation", "Destroy") };
            // Also inject reserved lifecycle properties into LoadAllPropertiesIntoCache to verify GetCommonProperties strips them before merging request properties
            mockPropertyValuesPersistentSource.LoadAllPropertiesIntoCache().Returns(new Dictionary<string, PropertyValueDto>
            {
                [TerraformDestroyOperationConstants.IsDestroyPropertyName] = new() { Value = "false" },
                [TerraformDestroyOperationConstants.PropertyName] = new() { Value = "Plan" },
                ["DOrcIsEphemeral"] = new() { Value = "false" }
            });
            mockPropertyValuesPersistentSource.IsCachedPropertySecure(Arg.Any<string>()).Returns(true);
            sut.Execute(delFailDto, CancellationToken.None, mockLoggerFactory);
            mockRequestsPersistentSource.Received().SetRequestCompletionStatus(
                100,
                DeploymentRequestStatus.Failed,
                Arg.Any<DateTimeOffset>());

            // Second: DeleteEnvironmentInternal returns true
            mockEnvironmentsPersistentSource.DeleteEnvironmentInternal(60).Returns(true);
            Assert.AreEqual(DeploymentRequestStatus.Completed, sut.UpdateEphemeralEnvironmentLifecycleState(env, new DeploymentRequestDetail { Properties = null! }, true, DeploymentRequestStatus.Completed));
            var deleteDetail = new DeploymentRequestDetail
            {
                Properties = new List<PropertyPair> { new("DeleteEnvironmentOnTeardown", "true") }
            };
            Assert.AreEqual(DeploymentRequestStatus.Completed, sut.UpdateEphemeralEnvironmentLifecycleState(env, deleteDetail, true, DeploymentRequestStatus.Completed));
            mockEnvironmentsPersistentSource.Received(4).DeleteEnvironmentInternal(60);

            // Third: DeleteEnvironmentInternal throws -> transitions to TeardownFailed and returns DeploymentRequestStatus.Failed
            mockEnvironmentsPersistentSource
                .When(x => x.DeleteEnvironmentInternal(61))
                .Do(_ => throw new InvalidOperationException("DB delete failed"));
            var envThrow = new EnvironmentApiModel { EnvironmentId = 61, EnvironmentName = "eph-61", IsEphemeral = true };
            Assert.AreEqual(DeploymentRequestStatus.Failed, sut.UpdateEphemeralEnvironmentLifecycleState(envThrow, null, true, DeploymentRequestStatus.Completed));
            mockEnvironmentsPersistentSource.Received(1).UpdateEphemeralState(61, EphemeralEnvironmentStates.TeardownFailed);

            // Outer catch TeardownFailed transition when Execute throws during a TearingDown/Destroy request
            var tearingDownEnv = new EnvironmentApiModel
            {
                EnvironmentId = 62,
                EnvironmentName = "eph-62",
                IsEphemeral = true,
                EphemeralState = EphemeralEnvironmentStates.TearingDown
            };
            mockEnvironmentsPersistentSource.GetEnvironment("eph-62").Returns(tearingDownEnv);
            mockRequestsPersistentSource
                .When(x => x.GetSubmittedComponents(Arg.Any<int>()))
                .Do(ci =>
                {
                    if (ci.Arg<int>() == 999)
                    {
                        throw new InvalidOperationException("Simulated outer failure");
                    }
                });

            var crashDto = new RequestToProcessDto(
                new DeploymentRequestApiModel { Id = 999, EnvironmentName = "eph-62", Status = DeploymentRequestStatus.Pending.ToString() },
                new DeploymentRequestDetail { EnvironmentName = "eph-62", Components = new List<string> { "Comp1" } });
            sut.Execute(crashDto, CancellationToken.None, mockLoggerFactory);
            mockEnvironmentsPersistentSource.Received(1).UpdateEphemeralState(62, EphemeralEnvironmentStates.TeardownFailed);

            // Also cover when targetEnv.EnvironmentId <= 0 in TryMarkEphemeralTeardownFailedOnException
            sut.TryMarkEphemeralTeardownFailedOnException(
                new EnvironmentApiModel { EnvironmentId = 0, EnvironmentName = "eph-0", IsEphemeral = true, EphemeralState = EphemeralEnvironmentStates.TearingDown },
                true,
                "eph-0");
            mockEnvironmentsPersistentSource.Received(1).UpdateEphemeralState("eph-0", EphemeralEnvironmentStates.TeardownFailed);

            // Also cover UpdateEphemeralEnvironmentLifecycleState when expectedEnvironmentId is present (mismatch, EnvironmentId <= 0, and match)
            sut.UpdateEphemeralEnvironmentLifecycleState(
                new EnvironmentApiModel { EnvironmentId = 64, EnvironmentName = "eph-ready", IsEphemeral = true, EphemeralState = EphemeralEnvironmentStates.Provisioning },
                new DeploymentRequestDetail
                {
                    Properties = new List<PropertyPair>
                    {
                        new(TerraformDestroyOperationConstants.EphemeralEnvironmentIdPropertyName, "999"),
                        new(TerraformDestroyOperationConstants.EphemeralTargetStatePropertyName, EphemeralEnvironmentStates.Ready)
                    }
                },
                false,
                DeploymentRequestStatus.Completed);
            sut.UpdateEphemeralEnvironmentLifecycleState(
                new EnvironmentApiModel { EnvironmentId = 0, EnvironmentName = "eph-ready", IsEphemeral = true, EphemeralState = EphemeralEnvironmentStates.Provisioning },
                new DeploymentRequestDetail
                {
                    Properties = new List<PropertyPair>
                    {
                        new(TerraformDestroyOperationConstants.EphemeralEnvironmentIdPropertyName, "64")
                    }
                },
                false,
                DeploymentRequestStatus.Completed);
            sut.UpdateEphemeralEnvironmentLifecycleState(
                new EnvironmentApiModel { EnvironmentId = 64, EnvironmentName = "eph-ready", IsEphemeral = true, EphemeralState = EphemeralEnvironmentStates.Provisioning },
                new DeploymentRequestDetail
                {
                    Properties = new List<PropertyPair>
                    {
                        new(TerraformDestroyOperationConstants.EphemeralEnvironmentIdPropertyName, "64")
                    }
                },
                false,
                DeploymentRequestStatus.Completed);

            // Also cover when isDestroyRequest is true (with EphemeralState = Ready) and UpdateEphemeralState throws inside TryMarkEphemeralTeardownFailedOnException
            tearingDownEnv.EphemeralState = EphemeralEnvironmentStates.Ready;
            crashDto.Details.Properties = new List<PropertyPair>
            {
                new(TerraformDestroyOperationConstants.PropertyName, TerraformDestroyOperationConstants.DestroyValue)
            };
            mockEnvironmentsPersistentSource
                .When(x => x.UpdateEphemeralState(62, EphemeralEnvironmentStates.TeardownFailed))
                .Do(_ => throw new InvalidOperationException("Secondary failure"));
            sut.Execute(crashDto, CancellationToken.None, mockLoggerFactory);

            // Also cover TryMarkEphemeralTeardownFailedOnException when environment is not ephemeral, or is null and fallbackEnvironmentName is looked up or blank
            tearingDownEnv.IsEphemeral = false;
            sut.Execute(crashDto, CancellationToken.None, mockLoggerFactory);

            var crashBeforeEnvLoadDto = new RequestToProcessDto(
                new DeploymentRequestApiModel { Id = 998, EnvironmentName = "eph-62", Status = DeploymentRequestStatus.Pending.ToString() },
                null!);
            sut.Execute(crashBeforeEnvLoadDto, CancellationToken.None, mockLoggerFactory);

            var crashBlankEnvDto = new RequestToProcessDto(
                new DeploymentRequestApiModel { Id = 997, EnvironmentName = " ", Status = DeploymentRequestStatus.Pending.ToString() },
                null!);
            sut.Execute(crashBlankEnvDto, CancellationToken.None, mockLoggerFactory);

            // Direct edge cases on TryMarkEphemeralTeardownFailedOnException
            sut.TryMarkEphemeralTeardownFailedOnException(
                new EnvironmentApiModel { EnvironmentId = 64, EnvironmentName = "eph-ready", IsEphemeral = true, EphemeralState = EphemeralEnvironmentStates.Ready },
                false,
                "eph-ready",
                new DeploymentRequestDetail
                {
                    Properties = new List<PropertyPair>
                    {
                        new(TerraformDestroyOperationConstants.EphemeralEnvironmentIdPropertyName, "999")
                    }
                });
            sut.TryMarkEphemeralTeardownFailedOnException(
                new EnvironmentApiModel { EnvironmentId = 0, EnvironmentName = "eph-ready", IsEphemeral = true, EphemeralState = EphemeralEnvironmentStates.Ready },
                false,
                "eph-ready",
                new DeploymentRequestDetail
                {
                    Properties = new List<PropertyPair>
                    {
                        new(TerraformDestroyOperationConstants.EphemeralEnvironmentIdPropertyName, "64")
                    }
                });
            sut.TryMarkEphemeralTeardownFailedOnException(
                new EnvironmentApiModel { EnvironmentId = 64, EnvironmentName = "eph-ready", IsEphemeral = true, EphemeralState = EphemeralEnvironmentStates.Ready },
                false,
                "eph-ready",
                new DeploymentRequestDetail
                {
                    Properties = new List<PropertyPair>
                    {
                        new(TerraformDestroyOperationConstants.EphemeralEnvironmentIdPropertyName, "64")
                    }
                });
            sut.TryMarkEphemeralTeardownFailedOnException(
                new EnvironmentApiModel { EnvironmentId = 64, EnvironmentName = "eph-ready", IsEphemeral = true, EphemeralState = EphemeralEnvironmentStates.Ready },
                false,
                "eph-ready",
                lifecycleReadyDetail);
            sut.TryMarkEphemeralTeardownFailedOnException(
                new EnvironmentApiModel { EnvironmentId = 64, EnvironmentName = "eph-prov", IsEphemeral = true, EphemeralState = EphemeralEnvironmentStates.Provisioning },
                false,
                "eph-prov",
                null);
            sut.TryMarkEphemeralTeardownFailedOnException(
                new EnvironmentApiModel { EnvironmentId = 64, EnvironmentName = "eph-prov", IsEphemeral = true, EphemeralState = EphemeralEnvironmentStates.Provisioning },
                false,
                "eph-prov",
                lifecycleReadyDetail);
            sut.TryMarkEphemeralTeardownFailedOnException(
                new EnvironmentApiModel { EnvironmentId = 64, EnvironmentName = "eph-test", IsEphemeral = true, EphemeralState = EphemeralEnvironmentStates.Testing },
                false,
                "eph-test",
                null);
            sut.TryMarkEphemeralTeardownFailedOnException(
                new EnvironmentApiModel { EnvironmentId = 64, EnvironmentName = "eph-test", IsEphemeral = true, EphemeralState = EphemeralEnvironmentStates.Testing },
                false,
                "eph-test",
                lifecycleReadyDetail);

            // Also cover when DeployComponent returns false with deploymentResult.Status == Cancelled, pre-existing Failed result during destroy, and exception during destroy
            env.EphemeralState = EphemeralEnvironmentStates.Ready;
            var cancelResultComp = new ComponentApiModel { ComponentId = 502, ComponentName = "CancelComp", IsEnabled = true, StopOnFailure = false, ComponentType = ComponentType.Terraform };
            SetupOrderedComponents(new List<ComponentApiModel> { cancelResultComp });
            mockRequestsPersistentSource.GetDeploymentResultsForRequest(100).Returns(new List<DeploymentResultApiModel>
            {
                new() { Id = 502, ComponentId = 502, RequestId = 100, Status = DeploymentResultStatus.Pending.ToString() }
            });
            mockComponentProcessor.DeployComponent(
                cancelResultComp,
                Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<bool>(),
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IDictionary<string, VariableValue>>(), Arg.Any<CancellationToken>())
                .Returns(ci =>
                {
                    ci.Arg<DeploymentResultApiModel>().Status = DeploymentResultStatus.Cancelled.ToString();
                    return false;
                });
            sut.Execute(CreateRequest(new List<ComponentApiModel> { cancelResultComp }), CancellationToken.None, mockLoggerFactory);

            // Cover pre-existing Failed result on non-destroy (StopOnFailure = true and false) and destroy (line 359)
            env.EphemeralState = EphemeralEnvironmentStates.Ready;
            var preFailStopComp = new ComponentApiModel { ComponentId = 503, ComponentName = "PreFailStop", IsEnabled = true, StopOnFailure = true, ComponentType = ComponentType.Terraform };
            var preFailContComp = new ComponentApiModel { ComponentId = 504, ComponentName = "PreFailCont", IsEnabled = true, StopOnFailure = false, ComponentType = ComponentType.Terraform };
            SetupOrderedComponents(new List<ComponentApiModel> { preFailContComp, preFailStopComp });
            mockRequestsPersistentSource.GetDeploymentResultsForRequest(100).Returns(new List<DeploymentResultApiModel>
            {
                new() { Id = 504, ComponentId = 504, RequestId = 100, Status = DeploymentResultStatus.Failed.ToString() },
                new() { Id = 503, ComponentId = 503, RequestId = 100, Status = DeploymentResultStatus.Failed.ToString() }
            });
            sut.Execute(CreateRequest(new List<ComponentApiModel> { preFailContComp, preFailStopComp }), CancellationToken.None, mockLoggerFactory);

            var destroyPreFailDto = CreateRequest(new List<ComponentApiModel> { preFailContComp });
            destroyPreFailDto.Details.Properties = new List<PropertyPair> { new("TerraformOperation", "Destroy") };
            sut.Execute(destroyPreFailDto, CancellationToken.None, mockLoggerFactory);

            // Cover exception thrown by DeployComponent on non-destroy (StopOnFailure = false and true) and destroy (line 420)
            env.EphemeralState = EphemeralEnvironmentStates.Ready;
            mockRequestsPersistentSource.GetDeploymentResultsForRequest(100).Returns(_ => new List<DeploymentResultApiModel>
            {
                new() { Id = 504, ComponentId = 504, RequestId = 100, Status = DeploymentResultStatus.Pending.ToString() },
                new() { Id = 503, ComponentId = 503, RequestId = 100, Status = DeploymentResultStatus.Pending.ToString() }
            });
            mockComponentProcessor.DeployComponent(
                Arg.Is<ComponentApiModel>(c => c.ComponentId == 504 || c.ComponentId == 503),
                Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<bool>(),
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IDictionary<string, VariableValue>>(), Arg.Any<CancellationToken>())
                .Returns(_ => throw new InvalidOperationException("Component exploded"));
            sut.Execute(CreateRequest(new List<ComponentApiModel> { preFailContComp, preFailStopComp }), CancellationToken.None, mockLoggerFactory);

            var destroyThrowDto = CreateRequest(new List<ComponentApiModel> { preFailContComp });
            destroyThrowDto.Details.Properties = new List<PropertyPair> { new("TerraformOperation", "Destroy") };
            sut.Execute(destroyThrowDto, CancellationToken.None, mockLoggerFactory);

            // Also cover when all submitted components are disabled (orderedEnabledNonSkippedComponents is empty) and when EphemeralEnvironmentId mismatches/matches
            var disabledCompEnv = new EnvironmentApiModel
            {
                EnvironmentId = 65,
                EnvironmentName = "eph-disabled-comp",
                IsEphemeral = true,
                EphemeralState = EphemeralEnvironmentStates.Provisioning
            };
            mockEnvironmentsPersistentSource.GetEnvironment(Arg.Any<string>()).Returns(disabledCompEnv);
            var disabledComp = new ComponentApiModel { ComponentId = 501, ComponentName = "DisabledComp", IsEnabled = false };
            SetupOrderedComponents(new List<ComponentApiModel> { disabledComp });
            var mismatchIdDto = CreateRequest(new List<ComponentApiModel> { disabledComp });
            mismatchIdDto.Details.Properties = new List<PropertyPair>
            {
                new(TerraformDestroyOperationConstants.EphemeralEnvironmentIdPropertyName, "9999")
            };
            sut.Execute(mismatchIdDto, CancellationToken.None, mockLoggerFactory);
            Assert.AreEqual(DeploymentRequestStatus.Cancelled.ToString(), mismatchIdDto.Request.Status);

            var zeroEnvIdDto = CreateRequest(new List<ComponentApiModel> { disabledComp });
            zeroEnvIdDto.Details.Properties = new List<PropertyPair>
            {
                new(TerraformDestroyOperationConstants.EphemeralEnvironmentIdPropertyName, "0")
            };
            disabledCompEnv.EnvironmentId = 0;
            sut.Execute(zeroEnvIdDto, CancellationToken.None, mockLoggerFactory);
            disabledCompEnv.EnvironmentId = 65;

            var disabledCompDto = CreateRequest(new List<ComponentApiModel> { disabledComp });
            disabledCompDto.Details.Properties = new List<PropertyPair>
            {
                new(TerraformDestroyOperationConstants.EphemeralEnvironmentIdPropertyName, "65"),
                new(TerraformDestroyOperationConstants.EphemeralTargetStatePropertyName, EphemeralEnvironmentStates.Ready)
            };
            sut.Execute(disabledCompDto, CancellationToken.None, mockLoggerFactory);
            Assert.AreEqual(EphemeralEnvironmentStates.Ready, disabledCompEnv.EphemeralState);

            // Also cover non-destroy request cancelled when environment is already TearingDown, and inner catch when GetSubmittedComponents throws during an active TearingDown destroy request
            var innerCatchEnv = new EnvironmentApiModel
            {
                EnvironmentId = 66,
                EnvironmentName = "eph-inner-catch",
                IsEphemeral = true,
                EphemeralState = EphemeralEnvironmentStates.TearingDown
            };
            mockEnvironmentsPersistentSource.GetEnvironment(Arg.Any<string>()).Returns(innerCatchEnv);
            var nonDestroyTearingDownDto = CreateRequest(new List<ComponentApiModel> { disabledComp });
            sut.Execute(nonDestroyTearingDownDto, CancellationToken.None, mockLoggerFactory);

            var innerCatchDto = CreateRequest(new List<ComponentApiModel> { disabledComp });
            innerCatchDto.Details.Properties = new List<PropertyPair>
            {
                new(TerraformDestroyOperationConstants.PropertyName, TerraformDestroyOperationConstants.DestroyValue)
            };
            innerCatchDto.Request.Id = 999; // triggers throw in GetSubmittedComponents(999) inside inner try
            sut.Execute(innerCatchDto, CancellationToken.None, mockLoggerFactory);
            Assert.AreEqual(EphemeralEnvironmentStates.TeardownFailed, innerCatchEnv.EphemeralState);
        }
    }
}
