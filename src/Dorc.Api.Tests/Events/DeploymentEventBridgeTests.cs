using Dorc.Api.Events;
using Dorc.Core.Events;
using Dorc.Core.Interfaces;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;

namespace Dorc.Api.Tests.Events
{
    /// <summary>
    /// Covers the two thin API-side bridges between the Kafka substrate and
    /// the SignalR pipeline: the Kafka→SignalR result projection
    /// (<see cref="SignalRDeploymentResultBroadcaster"/>) and the
    /// dual-publish fallback adapter
    /// (<see cref="FallbackDeploymentEventPublisher"/>).
    /// </summary>
    [TestClass]
    public class DeploymentEventBridgeTests
    {
        private static DeploymentResultEventData ResultEvent(int requestId = 42) => new(
            ResultId: 7, RequestId: requestId, ComponentId: 1, Status: "Running",
            StartedTime: null, CompletedTime: null, Timestamp: DateTimeOffset.UtcNow);

        private static DeploymentRequestEventData RequestEvent(int requestId = 42) => new(
            RequestId: requestId, Status: "Pending",
            StartedTime: DateTimeOffset.UtcNow, CompletedTime: null, Timestamp: DateTimeOffset.UtcNow);

        private static (IHubContext<DeploymentsHub, IDeploymentsEventsClient> hub,
                        IDeploymentsEventsClient groupClient,
                        IDeploymentSubscriptionsGroupTracker tracker) HubFixture(string groupName)
        {
            var hub = Substitute.For<IHubContext<DeploymentsHub, IDeploymentsEventsClient>>();
            var clients = Substitute.For<IHubClients<IDeploymentsEventsClient>>();
            var groupClient = Substitute.For<IDeploymentsEventsClient>();
            hub.Clients.Returns(clients);
            clients.Group(groupName).Returns(groupClient);
            var tracker = Substitute.For<IDeploymentSubscriptionsGroupTracker>();
            return (hub, groupClient, tracker);
        }

        [TestMethod]
        public async Task Broadcaster_DispatchesToTheRequestSubscriptionGroup()
        {
            var (hub, groupClient, tracker) = HubFixture("deployments-42");
            tracker.GetGroupName(42).Returns("deployments-42");
            var sut = new SignalRDeploymentResultBroadcaster(hub, tracker);
            var ev = ResultEvent(42);

            await sut.BroadcastAsync(ev, CancellationToken.None);

            await groupClient.Received(1).OnDeploymentResultStatusChanged(ev);
        }

        [TestMethod]
        public async Task Broadcaster_UsesTrackerGroupNamePerRequestId()
        {
            var (hub, groupClient, tracker) = HubFixture("deployments-7");
            tracker.GetGroupName(7).Returns("deployments-7");
            var sut = new SignalRDeploymentResultBroadcaster(hub, tracker);

            await sut.BroadcastAsync(ResultEvent(7), CancellationToken.None);

            tracker.Received(1).GetGroupName(7);
            await groupClient.Received(1).OnDeploymentResultStatusChanged(
                Arg.Is<DeploymentResultEventData>(e => e.RequestId == 7));
        }

        [TestMethod]
        public async Task Fallback_NewRequest_ForwardsToDirectPublisher()
        {
            var hub = Substitute.For<IHubContext<DeploymentsHub, IDeploymentsEventsClient>>();
            var clients = Substitute.For<IHubClients<IDeploymentsEventsClient>>();
            var all = Substitute.For<IDeploymentsEventsClient>();
            hub.Clients.Returns(clients);
            clients.All.Returns(all);
            var tracker = Substitute.For<IDeploymentSubscriptionsGroupTracker>();
            var sut = new FallbackDeploymentEventPublisher(new DirectDeploymentEventPublisher(hub, tracker));
            var ev = RequestEvent();

            await sut.PublishNewRequestAsync(ev);

            await all.Received(1).OnDeploymentRequestStarted(ev);
        }

        [TestMethod]
        public async Task Fallback_RequestStatusChanged_ForwardsToSubscriptionGroup()
        {
            var (hub, groupClient, tracker) = HubFixture("deployments-42");
            tracker.GetGroupName(42).Returns("deployments-42");
            tracker.GetUnsubscribedConnections().Returns(new List<string>());
            var sut = new FallbackDeploymentEventPublisher(new DirectDeploymentEventPublisher(hub, tracker));
            var ev = RequestEvent(42);

            await sut.PublishRequestStatusChangedAsync(ev);

            await groupClient.Received(1).OnDeploymentRequestStatusChanged(ev);
        }

        [TestMethod]
        public async Task Fallback_ResultStatusChanged_ForwardsToDirectPublisher()
        {
            var hub = Substitute.For<IHubContext<DeploymentsHub, IDeploymentsEventsClient>>();
            var clients = Substitute.For<IHubClients<IDeploymentsEventsClient>>();
            var others = Substitute.For<IDeploymentsEventsClient>();
            hub.Clients.Returns(clients);
            var tracker = Substitute.For<IDeploymentSubscriptionsGroupTracker>();
            tracker.GetGroupName(42).Returns("deployments-42");
            clients.Group("deployments-42").Returns(others);
            var sut = new FallbackDeploymentEventPublisher(new DirectDeploymentEventPublisher(hub, tracker));
            var ev = ResultEvent(42);

            await sut.PublishResultStatusChangedAsync(ev);

            await others.Received(1).OnDeploymentResultStatusChanged(ev);
        }
    }
}
