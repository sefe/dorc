using Dorc.ApiModel;
using Dorc.Core;
using Dorc.Core.HighAvailability;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Dorc.Monitor.Tests
{
    [TestClass]
    public class EphemeralEnvironmentReaperServiceTests
    {
        private ILogger<EphemeralEnvironmentReaperService> _logger = null!;
        private IEnvironmentsPersistentSource _environmentsPersistentSource = null!;
        private IEnvironmentTemplatesPersistentSource _environmentTemplatesPersistentSource = null!;
        private IProjectsPersistentSource _projectsPersistentSource = null!;
        private IRequestsPersistentSource _requestsPersistentSource = null!;
        private IDistributedLockService _distributedLockService = null!;

        [TestInitialize]
        public void Setup()
        {
            _logger = Substitute.For<ILogger<EphemeralEnvironmentReaperService>>();
            _environmentsPersistentSource = Substitute.For<IEnvironmentsPersistentSource>();
            _environmentTemplatesPersistentSource = Substitute.For<IEnvironmentTemplatesPersistentSource>();
            _projectsPersistentSource = Substitute.For<IProjectsPersistentSource>();
            _requestsPersistentSource = Substitute.For<IRequestsPersistentSource>();
            _distributedLockService = Substitute.For<IDistributedLockService>();

            _environmentsPersistentSource.GetExpiredEphemeralEnvironments(Arg.Any<DateTime>(), Arg.Any<bool>())
                .Returns(Array.Empty<EnvironmentApiModel>());
            _environmentsPersistentSource.GetEphemeralEnvironments(Arg.Any<int?>(), Arg.Any<string?>())
                .Returns(Array.Empty<EnvironmentApiModel>());
            _environmentsPersistentSource.TryClaimExpiredEphemeralEnvironmentForTeardown(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<bool>())
                .Returns(true);
            _environmentsPersistentSource.DeleteEnvironmentInternal(Arg.Any<int>())
                .Returns(true);
            _environmentsPersistentSource.GetEnvironmentComponentStatuses(Arg.Any<int>())
                .Returns(Array.Empty<EnvironmentComponentStatusModel>());
            _requestsPersistentSource.GetTerraformInventoryForEnvironment(Arg.Any<int>())
                .Returns(Array.Empty<TerraformInventoryProvenanceApiModel>());
            _requestsPersistentSource.GetRequestsWithStatus(
                Arg.Any<DeploymentRequestStatus>(),
                Arg.Any<DeploymentRequestStatus>(),
                Arg.Any<DeploymentRequestStatus>(),
                Arg.Any<DeploymentRequestStatus>(),
                Arg.Any<bool>())
                .Returns(Array.Empty<DeploymentRequestApiModel>());
            _requestsPersistentSource.GetRequestsWithStatus(
                Arg.Any<DeploymentRequestStatus>(),
                Arg.Any<DeploymentRequestStatus>(),
                Arg.Any<bool>())
                .Returns(Array.Empty<DeploymentRequestApiModel>());
        }

        [TestMethod]
        public async Task ReapExpiredEnvironmentsAsync_WhenDistributedLockEnabledAndNotAcquired_SkipsTick()
        {
            _distributedLockService.IsEnabled.Returns(true);
            _distributedLockService.TryAcquireLockAsync(
                EphemeralEnvironmentReaperService.ReaperLockKey,
                EphemeralEnvironmentReaperService.ReaperLockLeaseTimeMs,
                Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IDistributedLock?>(null));

            using var sut = new EphemeralEnvironmentReaperService(
                _logger,
                _environmentsPersistentSource,
                _environmentTemplatesPersistentSource,
                _projectsPersistentSource,
                _requestsPersistentSource,
                _distributedLockService);

            await sut.ReapExpiredEnvironmentsAsync(DateTime.UtcNow, CancellationToken.None);

            _environmentsPersistentSource.DidNotReceive().GetExpiredEphemeralEnvironments(Arg.Any<DateTime>(), Arg.Any<bool>());
        }

        [TestMethod]
        public async Task ReapExpiredEnvironmentsAsync_CoversLockAcquisitionCancellationReverseDestroyNoComponentsAndAlerting()
        {
            var lockHandle = Substitute.For<IDistributedLock>();
            _distributedLockService.IsEnabled.Returns(true);
            _distributedLockService.TryAcquireLockAsync(
                EphemeralEnvironmentReaperService.ReaperLockKey,
                EphemeralEnvironmentReaperService.ReaperLockLeaseTimeMs,
                Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IDistributedLock?>(lockHandle));

            var expiredEnvironments = new[]
            {
                // 1) Non-ephemeral safety skip
                new EnvironmentApiModel { EnvironmentId = 1, EnvironmentName = "non-eph", IsEphemeral = false, EnvironmentIsProd = false },
                // 2) Prod safety skip
                new EnvironmentApiModel { EnvironmentId = 2, EnvironmentName = "prod-eph", IsEphemeral = true, EnvironmentIsProd = true },
                // 3) Environment with template and Terraform components -> submits reverse destroy
                new EnvironmentApiModel { EnvironmentId = 10, EnvironmentName = "eph-with-template", IsEphemeral = true, EnvironmentIsProd = false, TemplateId = 3 },
                // 4) Environment without template and no mapped project -> transitions to TornDown
                new EnvironmentApiModel { EnvironmentId = 11, EnvironmentName = "eph-no-project", IsEphemeral = true, EnvironmentIsProd = false, TemplateId = null },
                // 5) Environment without template, mapped project, all enabled components -> SubmitRequest returns 0 -> TeardownFailed + Alert
                new EnvironmentApiModel { EnvironmentId = 12, EnvironmentName = "eph-submit-zero", IsEphemeral = true, EnvironmentIsProd = false, TemplateId = null },
                // 6) Stranded TearingDown with active destroy request -> skipped without reclaiming
                new EnvironmentApiModel { EnvironmentId = 13, EnvironmentName = "eph-active-destroy", IsEphemeral = true, EnvironmentIsProd = false, EphemeralState = EphemeralEnvironmentStates.TearingDown },
                // 7) Stranded TearingDown without active destroy request -> reclaimed and torn down
                new EnvironmentApiModel { EnvironmentId = 14, EnvironmentName = "eph-stranded-tearingdown", IsEphemeral = true, EnvironmentIsProd = false, EphemeralState = EphemeralEnvironmentStates.TearingDown }
            };
            _environmentsPersistentSource.GetExpiredEphemeralEnvironments(Arg.Any<DateTime>(), true)
                .Returns(expiredEnvironments);

            var serializer = new DeploymentRequestDetailSerializer();
            var applyDetails = serializer.Serialize(new DeploymentRequestDetail
            {
                EnvironmentName = "eph-with-template",
                Components = new List<string> { "CompA" }
            });
            var destroyDetails = serializer.Serialize(new DeploymentRequestDetail
            {
                EnvironmentName = "eph-with-template",
                Components = new List<string> { "CompA" },
                Properties = new List<PropertyPair>
                {
                    new(TerraformDestroyOperationConstants.PropertyName, TerraformDestroyOperationConstants.DestroyValue)
                }
            });
            var partialDestroyXml = "<PropertyName>TerraformOperation</PropertyName>";

            _requestsPersistentSource.GetRequestsWithStatus(
                DeploymentRequestStatus.Pending,
                DeploymentRequestStatus.Running,
                DeploymentRequestStatus.Requesting,
                DeploymentRequestStatus.WaitingConfirmation,
                false)
                .Returns(new[]
                {
                    new DeploymentRequestApiModel { Id = 101, EnvironmentName = "other-env", Status = "Pending", RequestDetails = applyDetails },
                    new DeploymentRequestApiModel { Id = 102, EnvironmentName = "eph-with-template", Status = "Running", RequestDetails = destroyDetails },
                    new DeploymentRequestApiModel { Id = 103, EnvironmentName = "eph-with-template", Status = "Running", RequestDetails = applyDetails },
                    new DeploymentRequestApiModel { Id = 104, EnvironmentName = "eph-with-template", Status = "Requesting", RequestDetails = partialDestroyXml },
                    new DeploymentRequestApiModel { Id = 105, EnvironmentName = "eph-with-template", Status = "Pending", RequestDetails = "" },
                    new DeploymentRequestApiModel { Id = 106, EnvironmentName = "eph-active-destroy", Status = "Running", RequestDetails = destroyDetails }
                });
            _requestsPersistentSource.GetRequestsWithStatus(
                DeploymentRequestStatus.Confirmed,
                DeploymentRequestStatus.Paused,
                false)
                .Returns(new[]
                {
                    new DeploymentRequestApiModel { Id = 107, EnvironmentName = "eph-with-template", Status = "Confirmed", RequestDetails = applyDetails },
                    new DeploymentRequestApiModel { Id = 108, EnvironmentName = "eph-with-template", Status = "Paused", RequestDetails = applyDetails }
                });

            _environmentTemplatesPersistentSource.GetTemplateById(3).Returns(new EnvironmentTemplateApiModel
            {
                Id = 3,
                ProjectId = 20,
                ProvisionComponentIds = new List<int> { 1, 2, 999 }
            });
            _projectsPersistentSource.GetProject(20).Returns(new ProjectApiModel { ProjectId = 20, ProjectName = "Traveler" });
            _projectsPersistentSource.GetComponentsForProject(20).Returns(new[]
            {
                new ComponentApiModel { ComponentId = 1, ComponentName = "Postgres", ComponentType = ComponentType.Terraform, TerraformSourceType = TerraformSourceType.Catalog, IsEnabled = true },
                new ComponentApiModel { ComponentId = 2, ComponentName = "Redis", ComponentType = ComponentType.Terraform, TerraformSourceType = TerraformSourceType.Catalog, IsEnabled = true },
                new ComponentApiModel { ComponentId = null, ComponentName = "NoId", ComponentType = ComponentType.Terraform, TerraformSourceType = TerraformSourceType.Catalog, IsEnabled = true }
            });

            _environmentsPersistentSource.GetMappedProjects("eph-no-project").Returns(Array.Empty<ProjectApiModel>());
            _environmentsPersistentSource.GetMappedProjects("eph-submit-zero")
                .Returns(new[] { new ProjectApiModel { ProjectId = 30, ProjectName = "ZeroProj" } });
            _requestsPersistentSource.GetProvisionedComponentIdsForEnvironment(12).Returns((IReadOnlyList<int>)null!);
            _projectsPersistentSource.GetComponentsForProject(30).Returns(new[]
            {
                new ComponentApiModel { ComponentId = 10, ComponentName = "TfComp", ComponentType = ComponentType.Terraform, IsEnabled = true },
                new ComponentApiModel { ComponentId = 11, ComponentName = "DisabledComp", ComponentType = ComponentType.Terraform, IsEnabled = false }
            });

            DeploymentRequest? capturedSubmittedRequest = null;
            _requestsPersistentSource.SubmitRequest(Arg.Do<DeploymentRequest>(r =>
            {
                if (r.Environment == "eph-with-template")
                {
                    capturedSubmittedRequest = r;
                }
            })).Returns(ci =>
            {
                var req = ci.Arg<DeploymentRequest>();
                return req.Environment == "eph-with-template" ? 5001 : 0;
            });

            // Also return a TeardownFailed environment from GetEphemeralEnvironments(state: TeardownFailed)
            var externalFailedEnv = new EnvironmentApiModel
            {
                EnvironmentId = 88,
                EnvironmentName = "eph-external-failed",
                IsEphemeral = true,
                EphemeralState = EphemeralEnvironmentStates.TeardownFailed
            };
            _environmentsPersistentSource.GetEphemeralEnvironments(null, EphemeralEnvironmentStates.TeardownFailed)
                .Returns(new[]
                {
                    new EnvironmentApiModel { EnvironmentId = 12, EnvironmentName = "eph-submit-zero", IsEphemeral = true, EphemeralState = EphemeralEnvironmentStates.TeardownFailed },
                    externalFailedEnv
                });

            var alerts = new List<(int Id, string Name, string Reason)>();
            using var sut = new EphemeralEnvironmentReaperService(
                _logger,
                _environmentsPersistentSource,
                _environmentTemplatesPersistentSource,
                _projectsPersistentSource,
                _requestsPersistentSource,
                _distributedLockService)
            {
                OnTeardownFailedAlert = (env, reason) => alerts.Add((env.EnvironmentId, env.EnvironmentName, reason))
            };

            await sut.ReapExpiredEnvironmentsAsync(DateTime.UtcNow, CancellationToken.None);

            // Verify request cancellations
            _requestsPersistentSource.Received(1).UpdateRequestStatus(103, DeploymentRequestStatus.Cancelling, EphemeralEnvironmentReaperService.ReaperActor, Arg.Any<DateTimeOffset>());
            _requestsPersistentSource.Received(1).UpdateRequestStatus(104, DeploymentRequestStatus.Cancelling, EphemeralEnvironmentReaperService.ReaperActor, Arg.Any<DateTimeOffset>());
            _requestsPersistentSource.Received(1).UpdateRequestStatus(105, DeploymentRequestStatus.Cancelled, EphemeralEnvironmentReaperService.ReaperActor, Arg.Any<DateTimeOffset>());

            // Verify reverse-order destroy submission for eph-with-template (including durable components)
            Assert.IsNotNull(capturedSubmittedRequest);
            Assert.AreEqual("Redis|Postgres", capturedSubmittedRequest!.Components);

            // Verify eph-no-project deleted metadata directly via DeleteEnvironmentInternal
            _environmentsPersistentSource.Received(1).DeleteEnvironmentInternal(11);

            // Verify eph-submit-zero transitioned to TeardownFailed and alerted ONCE (not twice on same tick)
            _environmentsPersistentSource.Received(1).UpdateEphemeralState(12, EphemeralEnvironmentStates.TeardownFailed);
            Assert.AreEqual(2, alerts.Count);
            CollectionAssert.AreEquivalent(new[] { 12, 88 }, alerts.Select(a => a.Id).ToList());

            // Second tick with no expired envs and only env 88 still in TeardownFailed: should NOT re-alert 88, and should clear 12 from alerted set
            _environmentsPersistentSource.GetExpiredEphemeralEnvironments(Arg.Any<DateTime>(), true)
                .Returns(Array.Empty<EnvironmentApiModel>());
            _environmentsPersistentSource.GetEphemeralEnvironments(null, EphemeralEnvironmentStates.TeardownFailed)
                .Returns(new[] { externalFailedEnv });
            _distributedLockService.IsEnabled.Returns(false);

            await sut.ReapExpiredEnvironmentsAsync(DateTime.UtcNow, CancellationToken.None);
            Assert.AreEqual(2, alerts.Count);

            // Third tick where env 12 re-enters TeardownFailed after recovery: should alert again for 12
            _environmentsPersistentSource.GetEphemeralEnvironments(null, EphemeralEnvironmentStates.TeardownFailed)
                .Returns(new[]
                {
                    externalFailedEnv,
                    new EnvironmentApiModel { EnvironmentId = 12, EnvironmentName = "eph-submit-zero", IsEphemeral = true, EphemeralState = EphemeralEnvironmentStates.TeardownFailed }
                });
            await sut.ReapExpiredEnvironmentsAsync(DateTime.UtcNow, CancellationToken.None);
            Assert.AreEqual(3, alerts.Count);

            var envWithOwner = new EnvironmentApiModel
            {
                EnvironmentId = 89,
                EnvironmentName = "eph-owner",
                EnvironmentTier = "",
                EphemeralState = "",
                Details = new EnvironmentDetailsApiModel { EnvironmentOwner = "owner@example.com" }
            };
            var envWithExplicitTierAndState = new EnvironmentApiModel
            {
                EnvironmentId = 90,
                EnvironmentName = "eph-owner-2",
                EnvironmentTier = "ephemeral",
                EphemeralState = EphemeralEnvironmentStates.TeardownFailed,
                Details = new EnvironmentDetailsApiModel { EnvironmentOwner = @"SEFE\owner@example.com" }
            };

            // Also cover LoggingTeardownFailureAlertPublisher.SendTeamsTeardownAlertAsync (null/disabled args, missing owner, missing Pid, success with/without DorcUiBaseUrl/tier/state, exception)
            var teamsClient = Substitute.For<Dorc.Monitor.Notifications.Teams.ITeamsConversationClient>();
            var adSearcher = Substitute.For<Dorc.Core.Interfaces.IActiveDirectorySearcher>();
            var teamsOpts = new Dorc.Monitor.Notifications.Teams.TeamsBotOptions { Enabled = true, DorcUiBaseUrl = "https://dorc.example.com/" };
            var teamsBotFallback = new Dorc.Monitor.Notifications.Teams.TeamsBotNotificationSink(
                Microsoft.Extensions.Options.Options.Create(teamsOpts),
                adSearcher,
                teamsClient,
                _requestsPersistentSource,
                new Dorc.Monitor.Notifications.Teams.DeploymentCompletionCardBuilder(
                    Microsoft.Extensions.Options.Options.Create(teamsOpts),
                    Substitute.For<IMonitorConfiguration>()),
                Substitute.For<ILoggerFactory>());
            Assert.IsFalse(await LoggingTeardownFailureAlertPublisher.SendTeamsTeardownAlertAsync(null, adSearcher, teamsOpts, externalFailedEnv, "r", CancellationToken.None, teamsBotFallback));
            Assert.IsFalse(await LoggingTeardownFailureAlertPublisher.SendTeamsTeardownAlertAsync(teamsClient, null, teamsOpts, externalFailedEnv, "r"));
            Assert.IsFalse(await LoggingTeardownFailureAlertPublisher.SendTeamsTeardownAlertAsync(teamsClient, adSearcher, null, externalFailedEnv, "r"));
            Assert.IsFalse(await LoggingTeardownFailureAlertPublisher.SendTeamsTeardownAlertAsync(teamsClient, adSearcher, new Dorc.Monitor.Notifications.Teams.TeamsBotOptions { Enabled = false }, externalFailedEnv, "r"));
            Assert.IsFalse(await LoggingTeardownFailureAlertPublisher.SendTeamsTeardownAlertAsync(teamsClient, adSearcher, teamsOpts, externalFailedEnv, "r"));
            var notifySvc = Substitute.For<Dorc.Monitor.Notifications.IDeploymentNotificationSink>();
            adSearcher.Search("throw@example.com").Returns<List<UserElementApiModel>>(_ => throw new InvalidOperationException("AD down"));
            Assert.IsFalse(await LoggingTeardownFailureAlertPublisher.SendTeamsTeardownAlertAsync(
                teamsClient,
                adSearcher,
                teamsOpts,
                new EnvironmentApiModel { EnvironmentId = 95, EnvironmentName = "eph-ad-throw", Details = new EnvironmentDetailsApiModel { EnvironmentOwner = "throw@example.com" } },
                "r"));
            Assert.IsTrue(await LoggingTeardownFailureAlertPublisher.SendTeamsTeardownAlertAsync(
                teamsClient,
                adSearcher,
                teamsOpts,
                new EnvironmentApiModel { EnvironmentId = 95, EnvironmentName = "eph-ad-throw", Details = new EnvironmentDetailsApiModel { EnvironmentOwner = "throw@example.com" } },
                "r",
                CancellationToken.None,
                notifySvc));
            adSearcher.Search("owner@example.com").Returns(
                (List<UserElementApiModel>)null!,
                new List<UserElementApiModel>
                {
                    new() { IsGroup = true, Username = "owner@example.com", Pid = "grp" },
                    new() { IsGroup = false, Username = "other", Email = "other@example.com", DisplayName = "Other", Pid = "other" }
                },
                new List<UserElementApiModel> { new() { IsGroup = false, Username = "owner@example.com", Pid = "S-1-5-21-1" } },
                new List<UserElementApiModel> { new() { IsGroup = false, Email = "owner@example.com", Pid = "S-1-5-21-1" } },
                new List<UserElementApiModel> { new() { IsGroup = false, DisplayName = "owner@example.com", Pid = "S-1-5-21-1" } });
            adSearcher.GetUserData("owner@example.com").Returns(
                (UserElementApiModel)null!,
                new UserElementApiModel { Pid = "" },
                new UserElementApiModel { Pid = "S-1-5-21-1" },
                new UserElementApiModel { Pid = "S-1-5-21-1" },
                new UserElementApiModel { Pid = "S-1-5-21-1" });
            Assert.IsFalse(await LoggingTeardownFailureAlertPublisher.SendTeamsTeardownAlertAsync(teamsClient, adSearcher, teamsOpts, envWithOwner, "r"));
            Assert.IsFalse(await LoggingTeardownFailureAlertPublisher.SendTeamsTeardownAlertAsync(teamsClient, adSearcher, teamsOpts, envWithOwner, "r"));
            teamsClient.CreateConversationAsync("S-1-5-21-1", Arg.Any<CancellationToken>()).Returns("conv-1");
            Assert.IsTrue(await LoggingTeardownFailureAlertPublisher.SendTeamsTeardownAlertAsync(teamsClient, adSearcher, teamsOpts, envWithOwner, "failed reason"));
            Assert.IsTrue(await LoggingTeardownFailureAlertPublisher.SendTeamsTeardownAlertAsync(teamsClient, adSearcher, new Dorc.Monitor.Notifications.Teams.TeamsBotOptions { Enabled = true, DorcUiBaseUrl = "" }, envWithExplicitTierAndState, "failed reason"));
            teamsClient.CreateConversationAsync("S-1-5-21-1", Arg.Any<CancellationToken>())
                .Returns<Task<string>>(_ => throw new InvalidOperationException("Teams API error"));
            Assert.IsFalse(await LoggingTeardownFailureAlertPublisher.SendTeamsTeardownAlertAsync(teamsClient, adSearcher, teamsOpts, envWithExplicitTierAndState, "failed reason"));

            // Also cover LoggingTeardownFailureAlertPublisher.SendNotificationAlertAsync (null service, NoOp service with/without eventsPublisher, missing recipient, success, exception)
            var eventsPub = Substitute.For<Dorc.Core.Interfaces.IDeploymentEventsPublisher>();
            Assert.IsFalse(await LoggingTeardownFailureAlertPublisher.SendNotificationAlertAsync(null, externalFailedEnv, "r"));
            Assert.IsFalse(await LoggingTeardownFailureAlertPublisher.SendNotificationAlertAsync(null, externalFailedEnv, "r", eventsPub));
            await eventsPub.Received(1).PublishRequestStatusChangedAsync(Arg.Any<Dorc.Core.Events.DeploymentRequestEventData>());
            Assert.IsFalse(await LoggingTeardownFailureAlertPublisher.SendNotificationAlertAsync(new Dorc.Monitor.Notifications.NoOpDeploymentNotificationSink(Substitute.For<ILoggerFactory>()), externalFailedEnv, "r"));
            Assert.IsFalse(await LoggingTeardownFailureAlertPublisher.SendNotificationAlertAsync(notifySvc, externalFailedEnv, "r"));
            Assert.IsTrue(await LoggingTeardownFailureAlertPublisher.SendNotificationAlertAsync(notifySvc, envWithOwner, "failed reason"));
            Assert.IsTrue(await LoggingTeardownFailureAlertPublisher.SendNotificationAlertAsync(notifySvc, envWithExplicitTierAndState, "failed reason"));
            notifySvc.NotifyRequestCompletedAsync(
                Arg.Any<DeploymentRequestApiModel>(),
                Arg.Any<string>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>())
                .Returns<Task>(_ => throw new InvalidOperationException("Teams bot down"));
            Assert.IsFalse(await LoggingTeardownFailureAlertPublisher.SendNotificationAlertAsync(notifySvc, envWithOwner, "failed reason"));

            // Also cover OnTeardownFailedAlert == null branch, LoggingTeardownFailureAlertPublisher (with and without external sender), failed alert retry, and throwing alert publisher
            var loggingPublisher = new LoggingTeardownFailureAlertPublisher(Substitute.For<ILogger<LoggingTeardownFailureAlertPublisher>>());
            var sutWithLoggingPublisher = new EphemeralEnvironmentReaperService(
                _logger,
                _environmentsPersistentSource,
                _environmentTemplatesPersistentSource,
                _projectsPersistentSource,
                _requestsPersistentSource,
                _distributedLockService,
                TimeSpan.FromMilliseconds(5),
                loggingPublisher);
            await sutWithLoggingPublisher.ReapExpiredEnvironmentsAsync(DateTime.UtcNow, CancellationToken.None);

            // External sender returning true and false, and throwing
            var externalTruePublisher = new LoggingTeardownFailureAlertPublisher(
                Substitute.For<ILogger<LoggingTeardownFailureAlertPublisher>>(),
                (_, _, _) => Task.FromResult(true));
            Assert.IsTrue(await externalTruePublisher.PublishTeardownFailureAlertAsync(externalFailedEnv, "reason"));

            var externalFalsePublisher = new LoggingTeardownFailureAlertPublisher(
                Substitute.For<ILogger<LoggingTeardownFailureAlertPublisher>>(),
                (_, _, _) => Task.FromResult(false));
            Assert.IsFalse(await externalFalsePublisher.PublishTeardownFailureAlertAsync(externalFailedEnv, "reason"));

            var externalThrowPublisher = new LoggingTeardownFailureAlertPublisher(
                Substitute.For<ILogger<LoggingTeardownFailureAlertPublisher>>(),
                (_, _, _) => throw new InvalidOperationException("Teams webhook down"));
            Assert.IsFalse(await externalThrowPublisher.PublishTeardownFailureAlertAsync(externalFailedEnv, "reason"));

            // When PublishTeardownFailureAlertAsync returns false, the environment is NOT added to _alertedEnvironmentIds so next tick retries
            var retryCount = 0;
            var retryPublisher = Substitute.For<ITeardownFailureAlertPublisher>();
            retryPublisher.PublishTeardownFailureAlertAsync(Arg.Any<EnvironmentApiModel>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    retryCount++;
                    return Task.FromResult(retryCount >= 2);
                });
            _environmentsPersistentSource.GetEphemeralEnvironments(null, EphemeralEnvironmentStates.TeardownFailed)
                .Returns(new[] { externalFailedEnv });
            var sutWithRetryPublisher = new EphemeralEnvironmentReaperService(
                _logger,
                _environmentsPersistentSource,
                _environmentTemplatesPersistentSource,
                _projectsPersistentSource,
                _requestsPersistentSource,
                _distributedLockService,
                TimeSpan.FromMilliseconds(5),
                retryPublisher);
            await sutWithRetryPublisher.ReapExpiredEnvironmentsAsync(DateTime.UtcNow, CancellationToken.None);
            await sutWithRetryPublisher.ReapExpiredEnvironmentsAsync(DateTime.UtcNow, CancellationToken.None);
            await sutWithRetryPublisher.ReapExpiredEnvironmentsAsync(DateTime.UtcNow, CancellationToken.None);
            Assert.AreEqual(2, retryCount); // Tick 1 failed (false), Tick 2 succeeded (true), Tick 3 skipped because already alerted

            var throwingPublisher = Substitute.For<ITeardownFailureAlertPublisher>();
            throwingPublisher
                .PublishTeardownFailureAlertAsync(Arg.Any<EnvironmentApiModel>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns<Task<bool>>(_ => throw new InvalidOperationException("Alert failure"));
            var sutWithThrowingPublisher = new EphemeralEnvironmentReaperService(
                _logger,
                _environmentsPersistentSource,
                _environmentTemplatesPersistentSource,
                _projectsPersistentSource,
                _requestsPersistentSource,
                _distributedLockService,
                TimeSpan.FromMilliseconds(5),
                throwingPublisher);
            await sutWithThrowingPublisher.ReapExpiredEnvironmentsAsync(DateTime.UtcNow, CancellationToken.None);
        }

        [TestMethod]
        public async Task ReapExpiredEnvironmentsAsync_CoversDurableDiscoveryClaimSkipDeleteFallbackAndLockLoss()
        {
            using var lockLostCts = new CancellationTokenSource();
            var lockHandle = Substitute.For<IDistributedLock>();
            lockHandle.LockLostToken.Returns(_ => lockLostCts.Token);
            _distributedLockService.IsEnabled.Returns(true);
            _distributedLockService.TryAcquireLockAsync(
                EphemeralEnvironmentReaperService.ReaperLockKey,
                EphemeralEnvironmentReaperService.ReaperLockLeaseTimeMs,
                Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IDistributedLock?>(lockHandle));

            // Env 20: unclaimed (TryClaim returns false) -> skipped
            // Env 21: no components, DeleteEnvironmentInternal returns false -> falls back to UpdateEphemeralState(TornDown)
            // Env 22: no components, DeleteEnvironmentInternal throws -> transitions to TeardownFailed and alerts
            // Env 23: template has component 1, durable records have components 2, 3, 4 (inventory, component statuses, catalog inputs) + one throwing status lookup on env 24
            // Env 25: after lockLostCts is cancelled during Env 24, Env 25 is skipped due to linkedCts cancellation
            var expired = new[]
            {
                new EnvironmentApiModel { EnvironmentId = 20, EnvironmentName = "eph-unclaimed", IsEphemeral = true },
                new EnvironmentApiModel { EnvironmentId = 21, EnvironmentName = "eph-del-false", IsEphemeral = true },
                new EnvironmentApiModel { EnvironmentId = 30, EnvironmentName = "eph-retain-metadata", IsEphemeral = true, ExpiresAtUtc = DateTime.UnixEpoch },
                new EnvironmentApiModel { EnvironmentId = 29, EnvironmentName = "eph-cancelling-defer", IsEphemeral = true },
                new EnvironmentApiModel { EnvironmentId = 32, EnvironmentName = "eph-cancelling-defer-retain", IsEphemeral = true, ExpiresAtUtc = DateTime.UnixEpoch },
                new EnvironmentApiModel { EnvironmentId = 22, EnvironmentName = "eph-del-throws", IsEphemeral = true },
                new EnvironmentApiModel { EnvironmentId = 23, EnvironmentName = "eph-durable", IsEphemeral = true, TemplateId = 9, ExpiresAtUtc = DateTime.UnixEpoch },
                new EnvironmentApiModel { EnvironmentId = 28, EnvironmentName = "eph-durable-no-template", IsEphemeral = true, TemplateId = null },
                new EnvironmentApiModel { EnvironmentId = 26, EnvironmentName = "eph-unresolved-durable", IsEphemeral = true, TemplateId = 9 },
                new EnvironmentApiModel { EnvironmentId = 27, EnvironmentName = "eph-unresolved-project", IsEphemeral = true, TemplateId = 9 },
                new EnvironmentApiModel { EnvironmentId = 31, EnvironmentName = "eph-provisioning-unprovisioned", IsEphemeral = true, TemplateId = 9, EphemeralState = EphemeralEnvironmentStates.Provisioning },
                new EnvironmentApiModel { EnvironmentId = 35, EnvironmentName = "eph-no-template-unprovisioned", IsEphemeral = true, TemplateId = null, EphemeralState = EphemeralEnvironmentStates.Provisioning },
                new EnvironmentApiModel { EnvironmentId = 33, EnvironmentName = "eph-provfailed-unprovisioned", IsEphemeral = true, TemplateId = 9, EphemeralState = EphemeralEnvironmentStates.ProvisionFailed },
                new EnvironmentApiModel { EnvironmentId = 34, EnvironmentName = "eph-deferred-unprovisioned", IsEphemeral = true, TemplateId = 9, EphemeralState = EphemeralEnvironmentStates.TearingDown, ExpiresAtUtc = DateTime.UnixEpoch.AddSeconds(1) },
                new EnvironmentApiModel { EnvironmentId = 24, EnvironmentName = "eph-throw-discovery", IsEphemeral = true, TemplateId = 9 },
                new EnvironmentApiModel { EnvironmentId = 25, EnvironmentName = "eph-after-lock-lost", IsEphemeral = true }
            };
            _environmentsPersistentSource.GetExpiredEphemeralEnvironments(Arg.Any<DateTime>(), true).Returns(expired);
            _requestsPersistentSource.GetRequestsWithStatus(DeploymentRequestStatus.Cancelling, isProd: false).Returns(new[]
            {
                new DeploymentRequestApiModel
                {
                    Id = 777,
                    EnvironmentName = "eph-cancelling-defer",
                    Status = DeploymentRequestStatus.Cancelling.ToString(),
                    RequestDetails = "<DeploymentRequestDetail />"
                },
                new DeploymentRequestApiModel
                {
                    Id = 778,
                    EnvironmentName = "eph-cancelling-defer-retain",
                    Status = DeploymentRequestStatus.Cancelling.ToString(),
                    RequestDetails = "<DeploymentRequestDetail />"
                }
            });

            _environmentsPersistentSource.TryClaimExpiredEphemeralEnvironmentForTeardown(20, Arg.Any<DateTime>(), Arg.Any<bool>()).Returns(false);
            _environmentsPersistentSource.DeleteEnvironmentInternal(21).Returns(false);
            _environmentsPersistentSource
                .When(x => x.DeleteEnvironmentInternal(22))
                .Do(_ => throw new InvalidOperationException("DB delete failure"));

            _environmentTemplatesPersistentSource.GetTemplateById(9).Returns(new EnvironmentTemplateApiModel
            {
                Id = 9,
                ProjectId = 40,
                ProvisionComponentIds = new List<int> { 1 }
            });
            _projectsPersistentSource.GetProject(40).Returns(new ProjectApiModel { ProjectId = 40, ProjectName = "DurableProj" });
            _environmentsPersistentSource.GetMappedProjects("eph-durable").Returns(new[]
            {
                new ProjectApiModel { ProjectId = 40, ProjectName = "DurableProj" },
                new ProjectApiModel { ProjectId = 41, ProjectName = "SecondaryProj" }
            });
            _environmentsPersistentSource.GetMappedProjects("eph-durable-no-template").Returns(new[]
            {
                new ProjectApiModel { ProjectId = 40, ProjectName = "DurableProj" },
                new ProjectApiModel { ProjectId = 41, ProjectName = "SecondaryProj" }
            });
            _requestsPersistentSource.GetProvisionedComponentIdsForEnvironment(28).Returns(new[] { 1, 11, 1, 111 });
            _requestsPersistentSource.GetProvisionedComponentDefinitionsForEnvironment(28).Returns(new (ProjectApiModel, ComponentApiModel)[]
            {
                (new ProjectApiModel { ProjectId = 40, ProjectName = "DurableProj" }, new ComponentApiModel { ComponentId = null, ComponentName = "NoIdComp", ComponentType = ComponentType.Terraform }),
                (new ProjectApiModel { ProjectId = 40, ProjectName = "DurableProj" }, new ComponentApiModel { ComponentId = 1, ComponentName = "Comp1", ComponentType = ComponentType.Terraform }),
                (new ProjectApiModel { ProjectId = 41, ProjectName = "SecondaryProj" }, new ComponentApiModel { ComponentId = 111, ComponentName = "PinnedComp111", ComponentType = ComponentType.Terraform, Children = new List<ComponentApiModel> { new() { ComponentId = 112, ComponentName = "NestedChild", ComponentType = ComponentType.Terraform } } })
            });
            _requestsPersistentSource.GetProvisionedProjectsForComponent(28, 1).Returns(new[] { "DurableProj", "SecondaryProj" });
            _requestsPersistentSource.GetProvisionedComponentIdsForEnvironment(23).Returns(new[] { 1, 10, 11, 12 });
            _requestsPersistentSource.GetProvisionedProjectsForComponent(23, 1).Returns(new[] { "DurableProj" });
            _requestsPersistentSource.GetProvisionedProjectsForComponent(23, 11).Returns(new[] { "SecondaryProj" });
            _requestsPersistentSource.GetLatestAppliedRequestForEnvironment("eph-durable", "DurableProj", 4).Returns(new DeploymentRequestApiModel
            {
                Project = "DurableProj",
                BuildUri = "vstfs:///Build/Build/99",
                DropLocation = @"\\drop\share",
                BuildNumber = "20260401.9"
            });
            _requestsPersistentSource.GetLatestAppliedRequestForEnvironment("eph-durable", "DurableProj", 1).Returns(new DeploymentRequestApiModel
            {
                Project = "DurableProj",
                BuildUri = "vstfs:///Build/Build/99",
                DropLocation = @"\\drop\share",
                BuildNumber = "20260401.9"
            });
            _requestsPersistentSource.GetLatestAppliedRequestForEnvironment("eph-durable", "DurableProj", 10).Returns(new DeploymentRequestApiModel
            {
                Project = "DurableProj",
                BuildUri = "vstfs:///Build/Build/99",
                DropLocation = @"\\drop\share",
                BuildNumber = "20260401.9"
            });
            _requestsPersistentSource.GetLatestAppliedRequestForEnvironment("eph-durable", "DurableProj", 2).Returns(new DeploymentRequestApiModel
            {
                Project = "DurableProj",
                BuildUri = "vstfs:///Build/Build/100",
                DropLocation = @"\\drop\share100",
                BuildNumber = "20260401.10"
            });
            _requestsPersistentSource.GetProvisionedComponentIdsForEnvironment(26).Returns(new[] { 999 });
            _requestsPersistentSource.GetProvisionedComponentIdsForEnvironment(27).Returns(new[] { 1 });
            _requestsPersistentSource.GetProvisionedProjectsForComponent(27, 1).Returns(new[] { "DurableProj", "RemovedProject" });
            _projectsPersistentSource.GetComponentsForProject(40).Returns(new[]
            {
                new ComponentApiModel { ComponentId = 1, ComponentName = "Comp1", ComponentType = ComponentType.Terraform, TerraformSourceType = TerraformSourceType.AzureArtifact, IsEnabled = true },
                new ComponentApiModel { ComponentId = 2, ComponentName = "Comp2", ComponentType = ComponentType.Terraform, TerraformSourceType = TerraformSourceType.AzureArtifact, IsEnabled = true },
                new ComponentApiModel { ComponentId = 3, ComponentName = "Comp3", ComponentType = ComponentType.Terraform, TerraformSourceType = TerraformSourceType.Catalog, IsEnabled = true },
                new ComponentApiModel { ComponentId = 4, ComponentName = "Comp4", ComponentType = ComponentType.Terraform, TerraformSourceType = TerraformSourceType.AzureArtifact, IsEnabled = true },
                new ComponentApiModel { ComponentId = 13, ComponentName = "SeededOnlyComp", ComponentType = ComponentType.Terraform, TerraformSourceType = TerraformSourceType.Catalog, IsEnabled = true },
                new ComponentApiModel { ComponentId = 5, ComponentName = "PsComp", ComponentType = ComponentType.PowerShell, IsEnabled = true },
                new ComponentApiModel
                {
                    ComponentId = 6,
                    ComponentName = "ParentContainer",
                    ComponentType = ComponentType.Terraform,
                    IsEnabled = true,
                    Children = new List<ComponentApiModel> { new() { ComponentId = 10, ComponentName = "Comp10", ComponentType = ComponentType.Terraform, IsEnabled = true } }
                },
                new ComponentApiModel { ComponentId = 10, ComponentName = "Comp10", ComponentType = ComponentType.Terraform, TerraformSourceType = TerraformSourceType.AzureArtifact, IsEnabled = true },
                new ComponentApiModel { ComponentId = 10, ComponentName = "Comp10", ComponentType = ComponentType.Terraform, TerraformSourceType = TerraformSourceType.AzureArtifact, IsEnabled = true }
            });
            _projectsPersistentSource.GetComponentsForProject(41).Returns(new[]
            {
                new ComponentApiModel { ComponentId = 1, ComponentName = "Comp1", ComponentType = ComponentType.Terraform, TerraformSourceType = TerraformSourceType.AzureArtifact, IsEnabled = true },
                new ComponentApiModel { ComponentId = 11, ComponentName = "Comp11", ComponentType = ComponentType.Terraform, TerraformSourceType = TerraformSourceType.SharedFolder, IsEnabled = true },
                new ComponentApiModel { ComponentId = 12, ComponentName = "Comp12", ComponentType = ComponentType.Terraform, TerraformSourceType = TerraformSourceType.SharedFolder, IsEnabled = true }
            });

            _requestsPersistentSource.GetTerraformInventoryForEnvironment(23).Returns(new[]
            {
                new TerraformInventoryProvenanceApiModel { ComponentId = 2 }
            });
            _environmentsPersistentSource.GetEnvironmentComponentStatuses(23).Returns(new List<EnvironmentComponentStatusModel>
            {
                new() { Component = "Comp3", Status = "Completed" },
                new() { Component = "Comp4", Status = "Failed" },
                new() { Component = "SeededOnlyComp", Status = "Cancelled" },
                new() { Component = "" }
            });
            _requestsPersistentSource.GetCatalogEnvironmentInputs(13, 23).Returns(new CatalogEnvironmentInputs
            {
                ComponentId = 13,
                EnvironmentId = 23,
                InputsJson = "{}"
            });

            _requestsPersistentSource.GetTerraformInventoryForEnvironment(24)
                .Returns((IEnumerable<TerraformInventoryProvenanceApiModel>)null!);
            _environmentsPersistentSource.GetEnvironmentComponentStatuses(24)
                .Returns(_ =>
                {
                    lockLostCts.Cancel();
                    return (IEnumerable<EnvironmentComponentStatusModel>)null!;
                });

            var submitted = new List<DeploymentRequest>();
            _requestsPersistentSource.SubmitRequest(Arg.Do<DeploymentRequest>(r => submitted.Add(r))).Returns(9001);

            using var sut = new EphemeralEnvironmentReaperService(
                _logger,
                _environmentsPersistentSource,
                _environmentTemplatesPersistentSource,
                _projectsPersistentSource,
                _requestsPersistentSource,
                _distributedLockService);

            await sut.ReapExpiredEnvironmentsAsync(DateTime.UtcNow, CancellationToken.None);

            // Env 20 was not processed because TryClaim returned false
            _environmentsPersistentSource.DidNotReceive().DeleteEnvironmentInternal(20);
            // Env 21 fell back to UpdateEphemeralState(TornDown)
            _environmentsPersistentSource.Received(1).UpdateEphemeralState(21, EphemeralEnvironmentStates.TornDown);
            // Env 22 transitioned to TeardownFailed when DeleteEnvironmentInternal threw
            _environmentsPersistentSource.Received(1).UpdateEphemeralState(22, EphemeralEnvironmentStates.TeardownFailed);
            // Env 26 transitioned to TeardownFailed because provisioned component 999 could not be resolved in any project
            _environmentsPersistentSource.Received(1).UpdateEphemeralState(26, EphemeralEnvironmentStates.TeardownFailed);
            // Env 23 discovered Comp1 (template) + Comp10 & Comp11 (provisioned across multi-projects, excluding ParentContainer) + Comp2 (inventory) + Comp3/Comp4 (status), excluding SeededOnlyComp (which only had seeded CatalogEnvironmentInputs), reversed and batched contiguously per project
            var env23Reqs = submitted.Where(r => r.Environment == "eph-durable").ToList();
            Assert.AreEqual(4, env23Reqs.Count);
            Assert.AreEqual("DurableProj", env23Reqs[0].Project);
            Assert.AreEqual("Comp4|Comp3", env23Reqs[0].Components);
            Assert.AreEqual("20260401.9", env23Reqs[0].BuildNumber);
            Assert.AreEqual("vstfs:///Build/Build/99", env23Reqs[0].BuildUri);
            Assert.AreEqual(@"\\drop\share", env23Reqs[0].DropLocation);
            Assert.AreEqual("DurableProj", env23Reqs[1].Project);
            Assert.AreEqual("Comp2", env23Reqs[1].Components);
            Assert.AreEqual("20260401.10", env23Reqs[1].BuildNumber);
            Assert.AreEqual("SecondaryProj", env23Reqs[2].Project);
            Assert.AreEqual("Comp12|Comp11", env23Reqs[2].Components);
            Assert.AreEqual("ephemeral-ttl-destroy", env23Reqs[2].BuildNumber);
            Assert.AreEqual("DurableProj", env23Reqs[3].Project);
            Assert.AreEqual("Comp10|Comp1", env23Reqs[3].Components);
            // Env 24 had lockLostCts cancelled during component discovery, so pre-submit check skipped SubmitRequest for Env 24
            Assert.IsFalse(submitted.Any(r => r.Environment == "eph-throw-discovery"));
            // Env 25 was skipped because lockLostCts was cancelled during Env 24
            _environmentsPersistentSource.DidNotReceive().TryClaimExpiredEphemeralEnvironmentForTeardown(25, Arg.Any<DateTime>(), Arg.Any<bool>());
        }

        [TestMethod]
        public async Task ExecuteAsync_HandlesNormalLoopExceptionAndCancellation()
        {
            var callCount = 0;
            using var cts = new CancellationTokenSource();

            _environmentsPersistentSource.GetExpiredEphemeralEnvironments(Arg.Any<DateTime>(), Arg.Any<bool>())
                .Returns(_ =>
                {
                    callCount++;
                    if (callCount == 1)
                    {
                        throw new InvalidOperationException("Simulated transient DB failure");
                    }
                    if (callCount >= 3)
                    {
                        cts.Cancel();
                    }

                    return Array.Empty<EnvironmentApiModel>();
                });

            using var sut = new EphemeralEnvironmentReaperService(
                _logger,
                _environmentsPersistentSource,
                _environmentTemplatesPersistentSource,
                _projectsPersistentSource,
                _requestsPersistentSource,
                _distributedLockService,
                TimeSpan.FromMilliseconds(20));

            await sut.StartAsync(cts.Token);
            await Task.Delay(60);
            await sut.StopAsync(CancellationToken.None);

            Assert.IsTrue(callCount >= 2);
        }
    }
}
