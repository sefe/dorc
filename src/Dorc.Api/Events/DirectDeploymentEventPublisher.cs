using Dorc.Core.Events;
using Dorc.Core.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace Dorc.Api.Events
{
    public class DirectDeploymentEventPublisher: IDeploymentEventsPublisher
    {
        private readonly IHubContext<DeploymentsHub, IDeploymentsEventsClient> _hub;

        public DirectDeploymentEventPublisher(IHubContext<DeploymentsHub, IDeploymentsEventsClient> hub) => _hub = hub;

        public async Task PublishNewRequestAsync(DeploymentEventData eventData, CancellationToken ct = default)
            => await _hub.Clients.All.OnDeploymentRequestStarted(eventData);

        public async Task PublishRequestStatusChangedAsync(DeploymentEventData eventData, CancellationToken ct = default)
            => await _hub.Clients.All.OnDeploymentRequestStatusChanged(eventData);
    }
}
