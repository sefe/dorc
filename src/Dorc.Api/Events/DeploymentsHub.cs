using Dorc.Core.Events;
using Dorc.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Dorc.Api.Events
{
    [Authorize]
    public sealed class DeploymentsHub : Hub<IDeploymentsEventsClient>
    {
        //public async Task PublishNewRequestAsync(DeploymentEventData eventData, CancellationToken ct = default)
        //    => await Clients.All.OnDeploymentRequestStarted(eventData);

        //public async Task PublishRequestStatusChangedAsync(DeploymentEventData eventData, CancellationToken ct = default)
        //    => await Clients.All.OnDeploymentRequestStatusChanged(eventData);
    }
}


