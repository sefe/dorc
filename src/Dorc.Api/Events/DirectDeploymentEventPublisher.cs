using Dorc.Core.Events;
using Dorc.Core.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace Dorc.Api.Events
{
    public class DirectDeploymentEventPublisher: IDeploymentEventsPublisher
    {
        private readonly IHubContext<DeploymentsHub, IDeploymentsEventsClient> _hub;
        private readonly IDeploymentSubscriptionsGroupTracker _tracker;

        public DirectDeploymentEventPublisher(
            IHubContext<DeploymentsHub, IDeploymentsEventsClient> hub,
            IDeploymentSubscriptionsGroupTracker tracker)
        {
            _hub = hub;
            _tracker = tracker;
        }

        public async Task PublishNewRequestAsync(DeploymentRequestEventData eventData)
            => await _hub.Clients.All.OnDeploymentRequestStarted(eventData);

        public async Task PublishRequestStatusChangedAsync(DeploymentRequestEventData eventData)
        {
            var groupName = _tracker.GetGroupName(eventData.RequestId);
            var unsubscribed = _tracker.GetUnsubscribedConnections();

            var tasks = new List<Task>
            {
                _hub.Clients.Group(groupName).OnDeploymentRequestStatusChanged(eventData)
            };

            if (unsubscribed.Count > 0)
            {
                tasks.Add(_hub.Clients.Clients(unsubscribed).OnDeploymentRequestStatusChanged(eventData));
            }

            await Task.WhenAll(tasks);
        }

        public async Task PublishResultStatusChangedAsync(DeploymentResultEventData eventData)
            => await _hub.Clients.Group(_tracker.GetGroupName(eventData.RequestId))
                .OnDeploymentResultStatusChanged(eventData);
    }
}
