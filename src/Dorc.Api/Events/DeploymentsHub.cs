using Dorc.Core.Events;
using Dorc.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Dorc.Api.Events
{
    /// <summary>
    /// SignalR hub for deployment events
    /// </summary>
    /// <remarks>
    /// You can generate Typescript client for that SignalR hub using the following command:
    /// <code>dotnet tsrts --project Dorc.Core/Dorc.Core.csproj --output Dorc-web/src/services/DeploymentEventsGenerated</code>
    /// </remarks>
    [Authorize]
    public sealed class DeploymentsHub : Hub<IDeploymentsEventsClient>, IDeploymentEventsPublisher
    {
        public async Task PublishNewRequestAsync(DeploymentEventData eventData)
            => await Clients.All.OnDeploymentRequestStarted(eventData);

        public async Task PublishRequestStatusChangedAsync(DeploymentEventData eventData)
            => await Clients.All.OnDeploymentRequestStatusChanged(eventData);
    }
}


