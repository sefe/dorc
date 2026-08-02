using Dorc.ApiModel;
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

        [TestInitialize]
        public void Setup()
        {
            mockSearcher = Substitute.For<IActiveDirectorySearcher>();
            mockConversationClient = Substitute.For<ITeamsConversationClient>();

            mockSearcher.Search(UserName).Returns(new List<UserElementApiModel>
            {
                new() { Username = "some.group", DisplayName = "Some Group", IsGroup = true, Pid = "group-pid" },
                new() { Username = UserName, DisplayName = "Jane Doe", Email = UserName, IsGroup = false, Pid = AadObjectId }
            });

            mockConversationClient.CreateConversationAsync(AadObjectId).Returns(ConversationId);
            mockConversationClient.SendCardAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(Task.CompletedTask);
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
                new DeploymentCompletionCardBuilder(options),
                NullLoggerFactory.Instance)
            {
                RetryDelayProvider = _ => TimeSpan.Zero
            };
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
            await mockConversationClient.DidNotReceiveWithAnyArgs().CreateConversationAsync(default!);
        }

        [TestMethod]
        public async Task Notify_StatusNotInNotifyOnStatuses_DoesNothing()
        {
            var sink = CreateSink();

            await sink.NotifyRequestCompletedAsync(CreateRequest(), "Cancelled", StartedTime, CompletedTime);

            mockSearcher.DidNotReceiveWithAnyArgs().Search(default!);
            await mockConversationClient.DidNotReceiveWithAnyArgs().CreateConversationAsync(default!);
        }

        [TestMethod]
        public async Task Notify_CustomNotifyOnStatuses_IsHonoured()
        {
            var sink = CreateSink(notifyOnStatuses: "Cancelled");

            await sink.NotifyRequestCompletedAsync(CreateRequest(), "Completed", StartedTime, CompletedTime);
            await mockConversationClient.DidNotReceiveWithAnyArgs().CreateConversationAsync(default!);

            await sink.NotifyRequestCompletedAsync(CreateRequest(), "Cancelled", StartedTime, CompletedTime);
            await mockConversationClient.Received(1).CreateConversationAsync(AadObjectId);
        }

        [TestMethod]
        public async Task Notify_BlankNotifyOnStatuses_FallsBackToDefaults()
        {
            var sink = CreateSink(notifyOnStatuses: "");

            await sink.NotifyRequestCompletedAsync(CreateRequest(), "Completed", StartedTime, CompletedTime);

            await mockConversationClient.Received(1).CreateConversationAsync(AadObjectId);
        }

        [TestMethod]
        public async Task Notify_EmptyUserName_DoesNothing()
        {
            var sink = CreateSink();

            await sink.NotifyRequestCompletedAsync(CreateRequest(userName: null), "Completed", StartedTime, CompletedTime);

            mockSearcher.DidNotReceiveWithAnyArgs().Search(default!);
            await mockConversationClient.DidNotReceiveWithAnyArgs().CreateConversationAsync(default!);
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

            await mockConversationClient.DidNotReceiveWithAnyArgs().CreateConversationAsync(default!);
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

            await mockConversationClient.DidNotReceiveWithAnyArgs().CreateConversationAsync(default!);
        }

        [TestMethod]
        public async Task Notify_SearcherThrows_IsSwallowedAndDoesNotDispatch()
        {
            mockSearcher.Search(UserName).Throws(new InvalidOperationException("Graph unavailable"));
            var sink = CreateSink();

            await sink.NotifyRequestCompletedAsync(CreateRequest(), "Completed", StartedTime, CompletedTime);

            await mockConversationClient.DidNotReceiveWithAnyArgs().CreateConversationAsync(default!);
        }

        [TestMethod]
        public async Task Notify_HappyPath_CreatesConversationAndSendsCard()
        {
            var sink = CreateSink();

            await sink.NotifyRequestCompletedAsync(CreateRequest(), "Completed", StartedTime, CompletedTime);

            await mockConversationClient.Received(1).CreateConversationAsync(AadObjectId);
            await mockConversationClient.Received(1).SendCardAsync(
                ConversationId,
                Arg.Is<string>(json => json.Contains("AdaptiveCard") && json.Contains("42")));
        }

        [TestMethod]
        public async Task Notify_TransientDispatchFailure_IsRetriedThenSucceeds()
        {
            var attempts = 0;
            mockConversationClient.CreateConversationAsync(AadObjectId).Returns(_ =>
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
            await mockConversationClient.Received(1).SendCardAsync(ConversationId, Arg.Any<string>());
        }

        [TestMethod]
        public async Task Notify_PersistentDispatchFailure_IsSwallowedAfterRetries()
        {
            mockConversationClient.CreateConversationAsync(AadObjectId)
                .Throws(new InvalidOperationException("permanent"));
            var sink = CreateSink();

            await sink.NotifyRequestCompletedAsync(CreateRequest(), "Completed", StartedTime, CompletedTime);

            // 1 initial attempt + 3 retries, then the failure is logged and swallowed
            await mockConversationClient.Received(4).CreateConversationAsync(AadObjectId);
            await mockConversationClient.DidNotReceiveWithAnyArgs().SendCardAsync(default!, default!);
        }

        [TestMethod]
        public async Task Notify_ArgumentException_IsNotRetried()
        {
            mockConversationClient.CreateConversationAsync(AadObjectId)
                .Throws(new ArgumentException("bad input"));
            var sink = CreateSink();

            await sink.NotifyRequestCompletedAsync(CreateRequest(), "Completed", StartedTime, CompletedTime);

            await mockConversationClient.Received(1).CreateConversationAsync(AadObjectId);
        }
    }
}
