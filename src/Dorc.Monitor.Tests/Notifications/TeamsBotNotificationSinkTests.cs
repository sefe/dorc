using Dorc.ApiModel;
using Dorc.Core;
using Dorc.Monitor;
using Dorc.PersistentData.Sources.Interfaces;
using Dorc.Core.Interfaces;
using Dorc.Monitor.Notifications.Teams;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Dorc.Monitor.Tests.Notifications
{
    [TestClass]
    public class TeamsBotNotificationSinkTests
    {
        private const string UserName = "jane.doe@example.com";
        private const string AadObjectId = "aad-object-id-1";
        private const string ConversationId = "conversation-1";

        private static readonly DateTimeOffset StartedTime = new(2026, 8, 1, 10, 0, 0, TimeSpan.Zero);
        private static readonly DateTimeOffset CompletedTime = StartedTime.AddMinutes(1);

        private IActiveDirectorySearcher mockSearcher = null!;
        private ITeamsConversationClient mockConversationClient = null!;
        private IRequestsPersistentSource mockRequestsPersistentSource = null!;

        [TestInitialize]
        public void Setup()
        {
            mockSearcher = Substitute.For<IActiveDirectorySearcher>();
            mockConversationClient = Substitute.For<ITeamsConversationClient>();
            mockRequestsPersistentSource = Substitute.For<IRequestsPersistentSource>();

            mockSearcher.Search(UserName).Returns(new List<UserElementApiModel>
            {
                new() { Username = "some.group", DisplayName = "Some Group", IsGroup = true, Pid = "group-pid" },
                new() { Username = UserName, DisplayName = "Jane Doe", Email = UserName, IsGroup = false, Pid = AadObjectId }
            });

            mockConversationClient.CreateConversationAsync(AadObjectId, Arg.Any<CancellationToken>()).Returns(ConversationId);
            mockConversationClient.SendCardAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        }

        private TeamsBotNotificationSink CreateSink(bool enabled = true, string? notifyOnStatuses = null)
        {
            var options = Options.Create(new TeamsBotOptions
            {
                Enabled = enabled,
                NotifyOnStatuses = notifyOnStatuses ?? "Completed,Failed,Errored"
            });

            return new TeamsBotNotificationSink(
                options,
                mockSearcher,
                mockConversationClient,
                mockRequestsPersistentSource,
                new DeploymentCompletionCardBuilder(options, CreateProductionMonitorConfiguration()),
                NullLoggerFactory.Instance)
            {
                RetryDelayProvider = _ => TimeSpan.Zero
            };
        }

        private static IMonitorConfiguration CreateProductionMonitorConfiguration()
        {
            // Production, so the card carries no non-prod instance banner and these
            // assertions stay about notification behaviour rather than card chrome.
            var monitorConfiguration = Substitute.For<IMonitorConfiguration>();
            monitorConfiguration.EnvironmentIsProduction.Returns(true);
            monitorConfiguration.Environment.Returns("UT2");
            return monitorConfiguration;
        }

        private static DeploymentRequestApiModel CreateRequest(string? userName = UserName)
        {
            return new DeploymentRequestApiModel
            {
                Id = 42,
                UserName = userName,
                Project = "TestProject",
                EnvironmentName = "TestEnv",
                BuildNumber = "1.2.3"
            };
        }

        [TestMethod]
        public async Task Notify_WhenDisabled_DoesNothing()
        {
            var sink = CreateSink(enabled: false);

            await sink.NotifyRequestCompletedAsync(CreateRequest(), "Completed", StartedTime, CompletedTime);

            mockSearcher.DidNotReceiveWithAnyArgs().Search(default!);
            await mockConversationClient.DidNotReceiveWithAnyArgs().CreateConversationAsync(default!, default);
        }

        [TestMethod]
        public async Task Notify_StatusNotInNotifyOnStatuses_DoesNothing()
        {
            var sink = CreateSink();

            await sink.NotifyRequestCompletedAsync(CreateRequest(), "Cancelled", StartedTime, CompletedTime);

            mockSearcher.DidNotReceiveWithAnyArgs().Search(default!);
            await mockConversationClient.DidNotReceiveWithAnyArgs().CreateConversationAsync(default!, default);
        }

        [TestMethod]
        public async Task Notify_WaitingConfirmationAndPending_AreNotNotifiedByDefault()
        {
            var sink = CreateSink();

            await sink.NotifyRequestCompletedAsync(CreateRequest(), "WaitingConfirmation", StartedTime, CompletedTime);
            await sink.NotifyRequestCompletedAsync(CreateRequest(), "Pending", StartedTime, CompletedTime);

            await mockConversationClient.DidNotReceiveWithAnyArgs().CreateConversationAsync(default!, default);
        }

        [TestMethod]
        public async Task Notify_CustomNotifyOnStatuses_IsHonoured()
        {
            var sink = CreateSink(notifyOnStatuses: "Cancelled");

            await sink.NotifyRequestCompletedAsync(CreateRequest(), "Completed", StartedTime, CompletedTime);
            await mockConversationClient.DidNotReceiveWithAnyArgs().CreateConversationAsync(default!, default);

            await sink.NotifyRequestCompletedAsync(CreateRequest(), "Cancelled", StartedTime, CompletedTime);
            await mockConversationClient.Received(1).CreateConversationAsync(AadObjectId, Arg.Any<CancellationToken>());
        }

        [TestMethod]
        public async Task Notify_BlankNotifyOnStatuses_FallsBackToDefaults()
        {
            var sink = CreateSink(notifyOnStatuses: "");

            await sink.NotifyRequestCompletedAsync(CreateRequest(), "Completed", StartedTime, CompletedTime);

            await mockConversationClient.Received(1).CreateConversationAsync(AadObjectId, Arg.Any<CancellationToken>());
        }

        [TestMethod]
        public async Task Notify_EmptyUserName_DoesNothing()
        {
            var sink = CreateSink();

            await sink.NotifyRequestCompletedAsync(CreateRequest(userName: null), "Completed", StartedTime, CompletedTime);

            mockSearcher.DidNotReceiveWithAnyArgs().Search(default!);
            await mockConversationClient.DidNotReceiveWithAnyArgs().CreateConversationAsync(default!, default);
        }

        [TestMethod]
        public async Task Notify_NoDirectoryMatch_DoesNotDispatch()
        {
            mockSearcher.Search(UserName).Returns(new List<UserElementApiModel>
            {
                new() { Username = "someone.else@example.com", IsGroup = false, Pid = "other-pid" }
            });
            var sink = CreateSink();

            await sink.NotifyRequestCompletedAsync(CreateRequest(), "Completed", StartedTime, CompletedTime);

            await mockConversationClient.DidNotReceiveWithAnyArgs().CreateConversationAsync(default!, default);
        }

        [TestMethod]
        public async Task Notify_GroupOnlyMatch_DoesNotDispatch()
        {
            mockSearcher.Search(UserName).Returns(new List<UserElementApiModel>
            {
                new() { Username = UserName, IsGroup = true, Pid = "group-pid" }
            });
            var sink = CreateSink();

            await sink.NotifyRequestCompletedAsync(CreateRequest(), "Completed", StartedTime, CompletedTime);

            await mockConversationClient.DidNotReceiveWithAnyArgs().CreateConversationAsync(default!, default);
        }

        [TestMethod]
        public async Task Notify_SearcherThrows_IsSwallowedAndDoesNotDispatch()
        {
            mockSearcher.Search(UserName).Throws(new InvalidOperationException("Graph unavailable"));
            var sink = CreateSink();

            await sink.NotifyRequestCompletedAsync(CreateRequest(), "Completed", StartedTime, CompletedTime);

            await mockConversationClient.DidNotReceiveWithAnyArgs().CreateConversationAsync(default!, default);
        }

        [TestMethod]
        public async Task Notify_HappyPath_CreatesConversationAndSendsCard()
        {
            var sink = CreateSink();

            await sink.NotifyRequestCompletedAsync(CreateRequest(), "Completed", StartedTime, CompletedTime);

            await mockConversationClient.Received(1).CreateConversationAsync(AadObjectId, Arg.Any<CancellationToken>());
            await mockConversationClient.Received(1).SendCardAsync(
                ConversationId,
                Arg.Is<string>(json => json.Contains("AdaptiveCard") && json.Contains("42")),
                Arg.Any<CancellationToken>());
        }

        [TestMethod]
        public async Task Notify_TransientDispatchFailure_IsRetriedThenSucceeds()
        {
            var attempts = 0;
            mockConversationClient.CreateConversationAsync(AadObjectId, Arg.Any<CancellationToken>()).Returns(_ =>
            {
                attempts++;
                if (attempts < 3)
                {
                    throw new HttpRequestException("transient");
                }
                return ConversationId;
            });
            var sink = CreateSink();

            await sink.NotifyRequestCompletedAsync(CreateRequest(), "Completed", StartedTime, CompletedTime);

            Assert.AreEqual(3, attempts);
            await mockConversationClient.Received(1).SendCardAsync(ConversationId, Arg.Any<string>(), Arg.Any<CancellationToken>());
        }

        [TestMethod]
        public async Task Notify_PersistentDispatchFailure_IsSwallowedAfterRetries()
        {
            mockConversationClient.CreateConversationAsync(AadObjectId, Arg.Any<CancellationToken>())
                .Throws(new InvalidOperationException("permanent"));
            var sink = CreateSink();

            await sink.NotifyRequestCompletedAsync(CreateRequest(), "Completed", StartedTime, CompletedTime);

            // 1 initial attempt + 3 retries, then the failure is logged and swallowed
            await mockConversationClient.Received(4).CreateConversationAsync(AadObjectId, Arg.Any<CancellationToken>());
            await mockConversationClient.DidNotReceiveWithAnyArgs().SendCardAsync(default!, default!, default);
        }

        [TestMethod]
        public async Task Notify_ArgumentException_IsNotRetried()
        {
            mockConversationClient.CreateConversationAsync(AadObjectId, Arg.Any<CancellationToken>())
                .Throws(new ArgumentException("bad input"));
            var sink = CreateSink();

            await sink.NotifyRequestCompletedAsync(CreateRequest(), "Completed", StartedTime, CompletedTime);

            await mockConversationClient.Received(1).CreateConversationAsync(AadObjectId, Arg.Any<CancellationToken>());
        }
        // ---- Batch (bulk sweep) grouping ----

        private const string OtherUserName = "john.smith@example.com";
        private const string OtherAadObjectId = "aad-object-id-2";
        private const string OtherConversationId = "conversation-2";

        private void ArrangeOtherUser()
        {
            mockSearcher.Search(OtherUserName).Returns(new List<UserElementApiModel>
            {
                new() { Username = OtherUserName, DisplayName = "John Smith", Email = OtherUserName, IsGroup = false, Pid = OtherAadObjectId }
            });
            mockConversationClient.CreateConversationAsync(OtherAadObjectId, Arg.Any<CancellationToken>()).Returns(OtherConversationId);
        }

        private static DeploymentRequestApiModel CreateRequest(int id, string? userName)
        {
            return new DeploymentRequestApiModel
            {
                Id = id,
                UserName = userName,
                Project = "TestProject",
                EnvironmentName = "TestEnv",
                BuildNumber = "1.2.3"
            };
        }

        [TestMethod]
        public async Task NotifyBatch_SameRequester_SendsOneCardNotOnePerRequest()
        {
            var sink = CreateSink();
            var requests = new[] { CreateRequest(1, UserName), CreateRequest(2, UserName), CreateRequest(3, UserName) };

            await sink.NotifyRequestsCompletedAsync(requests, "Errored", CompletedTime);

            // One conversation, one card - not three of each.
            await mockConversationClient.Received(1).CreateConversationAsync(AadObjectId, Arg.Any<CancellationToken>());
            await mockConversationClient.Received(1).SendCardAsync(ConversationId, Arg.Any<string>(), Arg.Any<CancellationToken>());
        }

        [TestMethod]
        public async Task NotifyBatch_SameRequester_CardSummarisesEveryRequest()
        {
            var sink = CreateSink();
            string? sent = null;
            await mockConversationClient.SendCardAsync(
                Arg.Any<string>(),
                Arg.Do<string>(json => sent = json),
                Arg.Any<CancellationToken>());

            var requests = new[] { CreateRequest(11, UserName), CreateRequest(12, UserName) };
            await sink.NotifyRequestsCompletedAsync(requests, "Errored", CompletedTime);

            Assert.IsNotNull(sent);
            StringAssert.Contains(sent, "2 deployments Errored");
            StringAssert.Contains(sent, "#11");
            StringAssert.Contains(sent, "#12");
        }

        [TestMethod]
        public async Task NotifyBatch_DifferentRequesters_SendsOneCardEach()
        {
            ArrangeOtherUser();
            var sink = CreateSink();
            var requests = new[]
            {
                CreateRequest(1, UserName),
                CreateRequest(2, UserName),
                CreateRequest(3, OtherUserName)
            };

            await sink.NotifyRequestsCompletedAsync(requests, "Errored", CompletedTime);

            await mockConversationClient.Received(1).CreateConversationAsync(AadObjectId, Arg.Any<CancellationToken>());
            await mockConversationClient.Received(1).CreateConversationAsync(OtherAadObjectId, Arg.Any<CancellationToken>());
            await mockConversationClient.Received(1).SendCardAsync(ConversationId, Arg.Any<string>(), Arg.Any<CancellationToken>());
            await mockConversationClient.Received(1).SendCardAsync(OtherConversationId, Arg.Any<string>(), Arg.Any<CancellationToken>());
        }

        [TestMethod]
        public async Task NotifyBatch_RequesterCasingDiffers_StillGroupsAsOnePerson()
        {
            var sink = CreateSink();
            mockSearcher.Search(Arg.Any<string>()).Returns(new List<UserElementApiModel>
            {
                new() { Username = UserName, Email = UserName, IsGroup = false, Pid = AadObjectId }
            });

            var requests = new[] { CreateRequest(1, UserName), CreateRequest(2, UserName.ToUpperInvariant()) };
            await sink.NotifyRequestsCompletedAsync(requests, "Errored", CompletedTime);

            await mockConversationClient.Received(1).CreateConversationAsync(AadObjectId, Arg.Any<CancellationToken>());
        }

        [TestMethod]
        public async Task NotifyBatch_SingleRequest_UsesTheNormalCard()
        {
            var sink = CreateSink();
            string? sent = null;
            await mockConversationClient.SendCardAsync(
                Arg.Any<string>(),
                Arg.Do<string>(json => sent = json),
                Arg.Any<CancellationToken>());

            await sink.NotifyRequestsCompletedAsync(new[] { CreateRequest(7, UserName) }, "Errored", CompletedTime);

            Assert.IsNotNull(sent);
            // The single-request card, not the "N deployments" summary.
            StringAssert.Contains(sent, "Request #7");
            Assert.IsFalse(sent!.Contains("1 deployments"), "A batch of one should not degrade to a summary card.");
        }

        [TestMethod]
        public async Task NotifyBatch_WhenDisabled_DoesNothing()
        {
            var sink = CreateSink(enabled: false);

            await sink.NotifyRequestsCompletedAsync(new[] { CreateRequest(1, UserName) }, "Errored", CompletedTime);

            mockSearcher.DidNotReceiveWithAnyArgs().Search(default!);
            await mockConversationClient.DidNotReceiveWithAnyArgs().CreateConversationAsync(default!, default);
        }

        [TestMethod]
        public async Task NotifyBatch_StatusNotInNotifyOnStatuses_DoesNothing()
        {
            var sink = CreateSink();

            await sink.NotifyRequestsCompletedAsync(new[] { CreateRequest(1, UserName) }, "Cancelled", CompletedTime);

            mockSearcher.DidNotReceiveWithAnyArgs().Search(default!);
            await mockConversationClient.DidNotReceiveWithAnyArgs().CreateConversationAsync(default!, default);
        }

        [TestMethod]
        public async Task NotifyBatch_EmptyBatch_DoesNothing()
        {
            var sink = CreateSink();

            await sink.NotifyRequestsCompletedAsync(Array.Empty<DeploymentRequestApiModel>(), "Errored", CompletedTime);

            mockSearcher.DidNotReceiveWithAnyArgs().Search(default!);
            await mockConversationClient.DidNotReceiveWithAnyArgs().CreateConversationAsync(default!, default);
        }

        [TestMethod]
        public async Task NotifyBatch_OneRequesterUnresolvable_StillNotifiesTheOthers()
        {
            ArrangeOtherUser();
            mockSearcher.Search("ghost@example.com").Returns(new List<UserElementApiModel>());

            var sink = CreateSink();
            var requests = new[]
            {
                CreateRequest(1, "ghost@example.com"),
                CreateRequest(2, OtherUserName)
            };

            await sink.NotifyRequestsCompletedAsync(requests, "Errored", CompletedTime);

            await mockConversationClient.Received(1).SendCardAsync(OtherConversationId, Arg.Any<string>(), Arg.Any<CancellationToken>());
            await mockConversationClient.Received(1).CreateConversationAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        }

        [TestMethod]
        public async Task NotifyBatch_OneRequesterDispatchThrows_StillNotifiesTheOthers()
        {
            ArrangeOtherUser();
            mockConversationClient
                .CreateConversationAsync(AadObjectId, Arg.Any<CancellationToken>())
                .Throws(new ArgumentException("no conversation for this user"));

            var sink = CreateSink();
            var requests = new[] { CreateRequest(1, UserName), CreateRequest(2, OtherUserName) };

            await sink.NotifyRequestsCompletedAsync(requests, "Errored", CompletedTime);

            // The failing recipient must not stop the other person being told.
            await mockConversationClient.Received(1).SendCardAsync(OtherConversationId, Arg.Any<string>(), Arg.Any<CancellationToken>());
        }

        [TestMethod]
        public async Task NotifyBatch_RequestWithoutUserName_IsSkippedButOthersAreSent()
        {
            var sink = CreateSink();
            var requests = new[] { CreateRequest(1, null), CreateRequest(2, UserName) };

            await sink.NotifyRequestsCompletedAsync(requests, "Errored", CompletedTime);

            await mockConversationClient.Received(1).CreateConversationAsync(AadObjectId, Arg.Any<CancellationToken>());
            await mockConversationClient.Received(1).SendCardAsync(ConversationId, Arg.Any<string>(), Arg.Any<CancellationToken>());
        }

        // ---- Bundle notifications ----

        private static string CreateBundledRequestDetails(params int[] requestIds)
        {
            var serializer = new DeploymentRequestDetailSerializer();
            return serializer.Serialize(new DeploymentRequestDetail
            {
                Properties = new List<PropertyPair>
                {
                    new("AllRequestIds", string.Join(",", requestIds))
                }
            });
        }

        private static DeploymentRequestApiModel CreateBundledRequest(
            int id,
            string status,
            DateTimeOffset? completedTime,
            params int[] bundleIds)
        {
            return new DeploymentRequestApiModel
            {
                Id = id,
                UserName = UserName,
                Project = "TestProject",
                EnvironmentName = "TestEnv",
                BuildNumber = "1.2.3",
                Status = status,
                CompletedTime = completedTime,
                RequestDetails = CreateBundledRequestDetails(bundleIds)
            };
        }

        [TestMethod]
        public async Task NotifyBundle_WhenSiblingStillRunning_DefersAndSendsNothing()
        {
            var req1 = CreateBundledRequest(101, "Completed", CompletedTime, 101, 102);
            var req2 = CreateBundledRequest(102, "Running", null, 101, 102);
            mockRequestsPersistentSource.GetRequest(101).Returns(req1);
            mockRequestsPersistentSource.GetRequest(102).Returns(req2);

            var sink = CreateSink();
            await sink.NotifyRequestCompletedAsync(req1, "Completed", StartedTime, CompletedTime);

            await mockConversationClient.DidNotReceiveWithAnyArgs().CreateConversationAsync(default!, default);
            await mockConversationClient.DidNotReceiveWithAnyArgs().SendCardAsync(default!, default!, default);
        }

        [TestMethod]
        public async Task NotifyBundle_WhenLastRequestCompletes_SendsSingleBundleCard()
        {
            var t1 = CompletedTime;
            var t2 = CompletedTime.AddMinutes(1);
            var req1 = CreateBundledRequest(101, "Completed", t1, 101, 102);
            var req2 = CreateBundledRequest(102, "Failed", t2, 101, 102);
            mockRequestsPersistentSource.GetRequest(101).Returns(req1);
            mockRequestsPersistentSource.GetRequest(102).Returns(req2);

            var sink = CreateSink();
            string? sentCard = null;
            await mockConversationClient.SendCardAsync(
                Arg.Any<string>(),
                Arg.Do<string>(json => sentCard = json),
                Arg.Any<CancellationToken>());

            await sink.NotifyRequestCompletedAsync(req2, "Failed", StartedTime, t2);

            await mockConversationClient.Received(1).CreateConversationAsync(AadObjectId, Arg.Any<CancellationToken>());
            await mockConversationClient.Received(1).SendCardAsync(ConversationId, Arg.Any<string>(), Arg.Any<CancellationToken>());
            Assert.IsNotNull(sentCard);
            StringAssert.Contains(sentCard, "Bundle deployment update (2 requests)");
        }

        [TestMethod]
        public async Task NotifyBundle_DualMonitorHaRace_OnlyLatestCompletedRequestNotifies()
        {
            // Simulate two Monitors (two separate sink instances with no shared memory)
            // finishing the last two requests in a bundle concurrently so both observe all
            // siblings in a terminal state when querying the DB.
            var t1 = CompletedTime;
            var t2 = CompletedTime.AddSeconds(5);
            var req1 = CreateBundledRequest(101, "Completed", t1, 101, 102);
            var req2 = CreateBundledRequest(102, "Completed", t2, 101, 102);
            mockRequestsPersistentSource.GetRequest(101).Returns(req1);
            mockRequestsPersistentSource.GetRequest(102).Returns(req2);

            var monitorASink = CreateSink();
            var monitorBSink = CreateSink();

            await monitorASink.NotifyRequestCompletedAsync(req1, "Completed", StartedTime, t1);
            await monitorBSink.NotifyRequestCompletedAsync(req2, "Completed", StartedTime, t2);

            // Exactly one bundle card is dispatched across both Monitors.
            await mockConversationClient.Received(1).CreateConversationAsync(AadObjectId, Arg.Any<CancellationToken>());
            await mockConversationClient.Received(1).SendCardAsync(ConversationId, Arg.Any<string>(), Arg.Any<CancellationToken>());
        }

        [TestMethod]
        public async Task NotifyBundle_DualMonitorSameTimestampTieBreak_OnlyHighestRequestIdNotifies()
        {
            var req1 = CreateBundledRequest(101, "Completed", CompletedTime, 101, 102);
            var req2 = CreateBundledRequest(102, "Completed", CompletedTime, 101, 102);
            mockRequestsPersistentSource.GetRequest(101).Returns(req1);
            mockRequestsPersistentSource.GetRequest(102).Returns(req2);

            var monitorASink = CreateSink();
            var monitorBSink = CreateSink();

            await monitorASink.NotifyRequestCompletedAsync(req1, "Completed", StartedTime, CompletedTime);
            await monitorBSink.NotifyRequestCompletedAsync(req2, "Completed", StartedTime, CompletedTime);

            await mockConversationClient.Received(1).CreateConversationAsync(AadObjectId, Arg.Any<CancellationToken>());
            await mockConversationClient.Received(1).SendCardAsync(ConversationId, Arg.Any<string>(), Arg.Any<CancellationToken>());
        }

        [TestMethod]
        public async Task NotifyBundle_WhenSiblingMissingFromDb_FallsBackToSingleRequestCard()
        {
            var req1 = CreateBundledRequest(101, "Completed", CompletedTime, 101, 999);
            mockRequestsPersistentSource.GetRequest(101).Returns(req1);
            mockRequestsPersistentSource.GetRequest(999).Returns((DeploymentRequestApiModel)null!);

            var sink = CreateSink();
            string? sentCard = null;
            await mockConversationClient.SendCardAsync(
                Arg.Any<string>(),
                Arg.Do<string>(json => sentCard = json),
                Arg.Any<CancellationToken>());

            await sink.NotifyRequestCompletedAsync(req1, "Completed", StartedTime, CompletedTime);

            // Instead of deferring forever when a sibling is missing, falls through to the single-request card.
            await mockConversationClient.Received(1).SendCardAsync(ConversationId, Arg.Any<string>(), Arg.Any<CancellationToken>());
            Assert.IsNotNull(sentCard);
            StringAssert.Contains(sentCard, "Request #101");
        }

        [TestMethod]
        public async Task NotifyBatch_CompletedBundleInBatchSweep_SendsOneBundleCard()
        {
            var req1 = CreateBundledRequest(101, "Cancelled", CompletedTime, 101, 102);
            var req2 = CreateBundledRequest(102, "Cancelled", CompletedTime, 101, 102);
            mockRequestsPersistentSource.GetRequest(101).Returns(req1);
            mockRequestsPersistentSource.GetRequest(102).Returns(req2);

            var sink = CreateSink(notifyOnStatuses: "Cancelled");
            string? sentCard = null;
            await mockConversationClient.SendCardAsync(
                Arg.Any<string>(),
                Arg.Do<string>(json => sentCard = json),
                Arg.Any<CancellationToken>());

            await sink.NotifyRequestsCompletedAsync(new[] { req1, req2 }, "Cancelled", CompletedTime);

            await mockConversationClient.Received(1).SendCardAsync(ConversationId, Arg.Any<string>(), Arg.Any<CancellationToken>());
            Assert.IsNotNull(sentCard);
            StringAssert.Contains(sentCard, "Bundle deployment update (2 requests)");
        }
    }
}
