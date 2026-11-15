using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core;
using Dorc.Core.Events;
using Dorc.Core.Interfaces;
using Dorc.Core.VariableResolution;
using Dorc.Monitor.Notifications;
using Dorc.Monitor.RequestProcessors;
using Dorc.Core.Security;
using Dorc.PersistentData.Security;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Configuration;
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
        private IScriptScopeConfigValues mockScriptScopeConfigValues = null!;
        private ILogger mockLogger = null!;

        private PendingRequestProcessor sut = null!;

        [TestInitialize]
        public void Setup()
        {
            mockLoggerFactory = Substitute.For<ILoggerFactory>();
            mockLogger = Substitute.For<ILogger>();
            mockLoggerFactory.CreateLogger(Arg.Any<string>()).Returns(mockLogger);

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
            mockScriptScopeConfigValues = new ScriptScopeConfigValues(new ConfigurationBuilder().Build());
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
                Substitute.For<ISourceHostAllowList>(),
                mockScriptScopeConfigValues);
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

        private List<string> ExecutionIdentityAdoptionReports()
        {
            return mockLogger.ReceivedCalls()
                .Where(call => call.GetMethodInfo().Name == nameof(ILogger.Log))
                .Select(call => call.GetArguments()[2]?.ToString())
                .Where(message => message?.Contains("Execution identity adoption for request") == true)
                .Cast<string>()
                .ToList();
        }

        [TestMethod]
        public void Execute_SuccessWithoutCredentialResolution_ReportsAdoptionOnce()
        {
            var dto = CreateRequest([]);
            SetupOrderedComponents([]);

            sut.Execute(dto, CancellationToken.None, mockLoggerFactory);

            var report = ExecutionIdentityAdoptionReports().Single();
            StringAssert.Contains(report, "0 resolution(s)");
            StringAssert.Contains(report, "0 fell back");
        }

        [TestMethod]
        public void Execute_ReportsCountsAccumulatedByTheDispatchPath()
        {
            var component = new ComponentApiModel
            {
                ComponentId = 1,
                ComponentName = "Comp1",
                IsEnabled = true
            };
            var dto = CreateRequest([component]);
            SetupOrderedComponents([component]);

            var credentialSource = Substitute.For<IDeploymentCredentialSource>();
            credentialSource.Resolve(DeploymentTier.NonProduction, null)
                .Returns(_ => DeploymentCredential.FromPlainText("shared", "password"));
            credentialSource.Resolve(DeploymentTier.NonProduction, "payments")
                .Returns(_ => DeploymentCredential.FromPlainText("payments", "password"));

            mockComponentProcessor.DeployComponent(
                    component,
                    Arg.Any<DeploymentResultApiModel>(),
                    Arg.Any<int>(),
                    Arg.Any<bool>(),
                    Arg.Any<int>(),
                    Arg.Any<bool>(),
                    Arg.Any<string>(),
                    Arg.Any<string?>(),
                    Arg.Do<RequestExecutionIdentityAdoption>(adoption =>
                    {
                        adoption.Resolve(credentialSource, DeploymentTier.NonProduction, null);
                        adoption.Resolve(credentialSource, DeploymentTier.NonProduction, "payments");
                    }),
                    Arg.Any<string>(),
                    Arg.Any<IDictionary<string, VariableValue>>(),
                    Arg.Any<CancellationToken>())
                .Returns(true);

            sut.Execute(dto, CancellationToken.None, mockLoggerFactory);

            var report = ExecutionIdentityAdoptionReports().Single();
            StringAssert.Contains(report, "1 resolution(s)");
            StringAssert.Contains(report, "1 fell back");
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
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<RequestExecutionIdentityAdoption>(), Arg.Any<string>(),
                Arg.Any<IDictionary<string, VariableValue>>(), Arg.Any<CancellationToken>())
                .Returns(false);

            // comp2 should never be called
            mockComponentProcessor.DeployComponent(
                comp2, Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<bool>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<RequestExecutionIdentityAdoption>(), Arg.Any<string>(),
                Arg.Any<IDictionary<string, VariableValue>>(), Arg.Any<CancellationToken>())
                .Returns(true);

            // Act
            sut.Execute(dto, CancellationToken.None, mockLoggerFactory);

            // Assert - comp2 was never deployed because comp1 had StopOnFailure=true
            mockComponentProcessor.DidNotReceive().DeployComponent(
                comp2, Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<bool>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<RequestExecutionIdentityAdoption>(), Arg.Any<string>(),
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
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<RequestExecutionIdentityAdoption>(), Arg.Any<string>(),
                Arg.Any<IDictionary<string, VariableValue>>(), Arg.Any<CancellationToken>())
                .Returns(false);

            mockComponentProcessor.DeployComponent(
                comp2, Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<bool>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<RequestExecutionIdentityAdoption>(), Arg.Any<string>(),
                Arg.Any<IDictionary<string, VariableValue>>(), Arg.Any<CancellationToken>())
                .Returns(true);

            // Act
            sut.Execute(dto, CancellationToken.None, mockLoggerFactory);

            // Assert - comp2 WAS deployed because StopOnFailure=false
            mockComponentProcessor.Received().DeployComponent(
                comp2, Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<bool>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<RequestExecutionIdentityAdoption>(), Arg.Any<string>(),
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
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<RequestExecutionIdentityAdoption>(), Arg.Any<string>(),
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
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<RequestExecutionIdentityAdoption>(), Arg.Any<string>(),
                Arg.Any<IDictionary<string, VariableValue>>(), Arg.Any<CancellationToken>())
                .Returns(x => throw new InvalidOperationException("Script failed"));

            // Act
            sut.Execute(dto, CancellationToken.None, mockLoggerFactory);

            // Assert - comp2 was never deployed
            mockComponentProcessor.DidNotReceive().DeployComponent(
                comp2, Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<bool>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<RequestExecutionIdentityAdoption>(), Arg.Any<string>(),
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
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<RequestExecutionIdentityAdoption>(), Arg.Any<string>(),
                Arg.Any<IDictionary<string, VariableValue>>(), Arg.Any<CancellationToken>()).Returns(true);

            sut.Execute(CreateRequest(components), CancellationToken.None, mockLoggerFactory);

            mockComponentProcessor.Received(1).DeployComponent(
                components[0], Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<bool>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<RequestExecutionIdentityAdoption>(), Arg.Any<string>(),
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
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<RequestExecutionIdentityAdoption>(), Arg.Any<string>(),
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
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<RequestExecutionIdentityAdoption>(), Arg.Any<string>(),
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
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<RequestExecutionIdentityAdoption>(), Arg.Any<string>(),
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
        public void Execute_WithholdsBuiltInAndOperatorConfiguredConfigKeysFromComponentProperties()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"{ScriptScopeConfigValues.WithheldKeysSetting}:0"] = "DORC_ProdDeployPassword",
                    [$"{ScriptScopeConfigValues.WithheldKeysSetting}:1"] = "CustomOperatorSecret"
                })
                .Build();
            var processor = new PendingRequestProcessor(
                mockLoggerFactory,
                mockComponentProcessor,
                mockVariableScopeOptionsResolver,
                mockRequestsPersistentSource,
                mockPropertyValuesPersistentSource,
                mockEnvironmentsPersistentSource,
                mockManageProjectsPersistentSource,
                mockConfigValuesPersistentSource,
                new PropertyEvaluator(),
                mockEventsPublisher,
                mockNotificationSink,
                mockGitHubArtifactDownloader,
                Substitute.For<ISourceHostAllowList>(),
                new ScriptScopeConfigValues(config));

            mockConfigValuesPersistentSource.GetAllConfigValues(true).Returns(new[]
            {
                new ConfigValueApiModel { Key = "DORC_ProdDeployPassword", Value = "prod-deploy-secret", Secure = true, VisibleToScripts = true },
                new ConfigValueApiModel { Key = "CustomOperatorSecret", Value = "operator-secret", Secure = true, VisibleToScripts = true },
                new ConfigValueApiModel { Key = "HiddenByClassification", Value = "classified-secret", Secure = true, VisibleToScripts = false },
                new ConfigValueApiModel { Key = "DORC_NonProdDeployPassword", Value = "still-published-secret", Secure = true, VisibleToScripts = true },
                new ConfigValueApiModel { Key = "ScriptRoot", Value = @"C:\Scripts", Secure = false, VisibleToScripts = true }
            });

            var comp1 = new ComponentApiModel { ComponentId = 1, ComponentName = "Comp1", IsEnabled = true };
            var components = new List<ComponentApiModel> { comp1 };
            SetupOrderedComponents(components);
            IDictionary<string, VariableValue>? capturedProperties = null;
            mockComponentProcessor.DeployComponent(
                comp1, Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<bool>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<RequestExecutionIdentityAdoption>(), Arg.Any<string>(),
                Arg.Do<IDictionary<string, VariableValue>>(props => capturedProperties = new Dictionary<string, VariableValue>(props)),
                Arg.Any<CancellationToken>())
                .Returns(true);

            processor.Execute(CreateRequest(components), CancellationToken.None, mockLoggerFactory);

            Assert.IsNotNull(capturedProperties);
            Assert.IsFalse(capturedProperties.ContainsKey("DORC_ProdDeployPassword"));
            Assert.IsFalse(capturedProperties.ContainsKey("CustomOperatorSecret"));
            Assert.IsFalse(capturedProperties.ContainsKey("HiddenByClassification"));
            Assert.IsTrue(capturedProperties.ContainsKey("DORC_NonProdDeployPassword"));
            Assert.IsTrue(capturedProperties.ContainsKey("ScriptRoot"));
        }

        [TestMethod]
        public void Execute_BlocksTokenInterpolationOfWithheldPropertyAndConfigValues()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [ScriptScopeConfigValues.WithheldKeysSetting] = "CustomWithheldKey"
                })
                .Build();
            var processor = new PendingRequestProcessor(
                mockLoggerFactory,
                mockComponentProcessor,
                mockVariableScopeOptionsResolver,
                mockRequestsPersistentSource,
                mockPropertyValuesPersistentSource,
                mockEnvironmentsPersistentSource,
                mockManageProjectsPersistentSource,
                mockConfigValuesPersistentSource,
                new PropertyEvaluator(),
                mockEventsPublisher,
                mockNotificationSink,
                mockGitHubArtifactDownloader,
                Substitute.For<ISourceHostAllowList>(),
                new ScriptScopeConfigValues(config));

            mockConfigValuesPersistentSource.GetAllConfigValues(true).Returns(new[]
            {
                new ConfigValueApiModel { Key = "DORC_ProdDeployPassword", Value = "prod-config-secret", Secure = true, VisibleToScripts = true },
                new ConfigValueApiModel { Key = "ScriptRoot", Value = @"C:\Scripts", Secure = false, VisibleToScripts = true }
            });

            var cachedWithheld = new PropertyValueDto
            {
                Value = "withheld-property-secret",
                Property = new PropertyApiModel { Name = "CustomWithheldKey", Secure = true }
            };
            var cachedBuiltIn = new PropertyValueDto
            {
                Value = "prod-property-secret",
                Property = new PropertyApiModel { Name = "DORC_ProdDeployPassword", Secure = true }
            };
            mockPropertyValuesPersistentSource.LoadAllPropertiesIntoCache().Returns(new Dictionary<string, PropertyValueDto>
            {
                ["CustomWithheldKey"] = cachedWithheld,
                ["DORC_ProdDeployPassword"] = cachedBuiltIn,
                ["EnvExfil"] = new PropertyValueDto
                {
                    Value = "$CustomWithheldKey$-from-env",
                    Property = new PropertyApiModel { Name = "EnvExfil", Secure = false }
                }
            });
            mockPropertyValuesPersistentSource.GetCachedPropertyValue("CustomWithheldKey").Returns(cachedWithheld);
            mockPropertyValuesPersistentSource.GetCachedPropertyValue("DORC_ProdDeployPassword").Returns(cachedBuiltIn);

            var comp1 = new ComponentApiModel { ComponentId = 1, ComponentName = "Comp1", IsEnabled = true };
            var components = new List<ComponentApiModel> { comp1 };
            SetupOrderedComponents(components);

            IDictionary<string, VariableValue>? capturedProperties = null;
            mockComponentProcessor.DeployComponent(
                comp1, Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<bool>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<RequestExecutionIdentityAdoption>(), Arg.Any<string>(),
                Arg.Do<IDictionary<string, VariableValue>>(props => capturedProperties = new Dictionary<string, VariableValue>(props)),
                Arg.Any<CancellationToken>())
                .Returns(true);

            var dto = CreateRequest(components);
            dto.Details.Properties = new List<PropertyPair>
            {
                new() { Name = "Exfil", Value = "$CustomWithheldKey$" },
                new() { Name = "BuiltInExfil", Value = "$DORC_ProdDeployPassword$" },
                new() { Name = "DORC_ProdDeployPassword", Value = "supplied-override" }
            };

            processor.Execute(dto, CancellationToken.None, mockLoggerFactory);

            Assert.IsNotNull(capturedProperties);
            Assert.IsFalse(capturedProperties.ContainsKey("CustomWithheldKey"));
            Assert.IsFalse(capturedProperties.ContainsKey("DORC_ProdDeployPassword"));
            Assert.AreEqual("$CustomWithheldKey$", capturedProperties["Exfil"].Value);
            Assert.AreEqual("$DORC_ProdDeployPassword$", capturedProperties["BuiltInExfil"].Value);
            Assert.AreEqual("$CustomWithheldKey$-from-env", capturedProperties["EnvExfil"].Value);
        }

        [TestMethod]
        public void Execute_FlowsEnvironmentExecutionIdentityReferenceIntoComponentProcessor()
        {
            mockEnvironmentsPersistentSource.GetEnvironment("TestEnv")
                .Returns(new EnvironmentApiModel
                {
                    EnvironmentId = 17,
                    EnvironmentName = "TestEnv",
                    EnvironmentIsProd = true,
                    EnvironmentSecure = true,
                    ExecutionIdentityReference = "payments-prod"
                });

            var component = new ComponentApiModel { ComponentId = 1, ComponentName = "Comp1", IsEnabled = true };
            SetupOrderedComponents([component]);
            mockComponentProcessor.DeployComponent(
                component, Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<bool>(),
                Arg.Any<string>(), "payments-prod", Arg.Any<RequestExecutionIdentityAdoption>(), Arg.Any<string>(),
                Arg.Any<IDictionary<string, VariableValue>>(), Arg.Any<CancellationToken>())
                .Returns(true);

            sut.Execute(CreateRequest([component]), CancellationToken.None, mockLoggerFactory);

            mockComponentProcessor.Received(1).DeployComponent(
                component, Arg.Any<DeploymentResultApiModel>(),
                100, false, 17, true,
                "TestEnv", "payments-prod", Arg.Any<RequestExecutionIdentityAdoption>(), "C:\\Scripts",
                Arg.Any<IDictionary<string, VariableValue>>(), Arg.Any<CancellationToken>());
        }
    }
}
