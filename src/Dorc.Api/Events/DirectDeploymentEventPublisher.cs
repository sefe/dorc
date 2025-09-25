using Dorc.Core.Events;
using Dorc.Core.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace Dorc.Api.Events
{
    public class DirectDeploymentEventPublisher: IDeploymentEventsPublisher
    {
        private readonly IHubContext<DeploymentsHub, IDeploymentsEventsClient> _hub;

        public DirectDeploymentEventPublisher(IHubContext<DeploymentsHub, IDeploymentsEventsClient> hub) => _hub = hub;

        public async Task PublishNewRequestAsync(DeploymentRequestEventData eventData)
            => await _hub.Clients.All.OnDeploymentRequestStarted(eventData);

        public async Task PublishRequestStatusChangedAsync(DeploymentRequestEventData eventData)
            => await _hub.Clients.All.OnDeploymentRequestStatusChanged(eventData);

        public async Task PublishResultStatusChangedAsync(DeploymentResultEventData eventData)
            => await _hub.Clients.All.OnDeploymentResultStatusChanged(eventData);
    }
}
