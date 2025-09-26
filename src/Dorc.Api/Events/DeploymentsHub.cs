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
    /// <code>dotnet tsrts --project Dorc.Core/Dorc.Core.csproj --output Dorc-web/src/services/ServerEvents/DeploymentEventsGenerated</code>
    /// </remarks>
    [Authorize]
    public sealed class DeploymentsHub : Hub<IDeploymentsEventsClient>, IDeploymentEventsHub
    {
        public static string GetGroupName(int requestId) => $"req:{requestId}";

        // Group management: clients call these to scope updates to a request
        public async Task JoinRequestGroup(int requestId)
            => await Groups.AddToGroupAsync(Context.ConnectionId, GetGroupName(requestId));

        public async Task LeaveRequestGroup(int requestId)
            => await Groups.RemoveFromGroupAsync(Context.ConnectionId, GetGroupName(requestId));

        // Forward hub calls to publisher so server-side coalescing/scoping applies uniformly
        public async Task BroadcastNewRequestAsync(DeploymentRequestEventData eventData)
            => await Clients.Others.OnDeploymentRequestStarted(eventData);

        public async Task BroadcastRequestStatusChangedAsync(DeploymentRequestEventData eventData)
            => await Clients.Others.OnDeploymentRequestStatusChanged(eventData);

        public async Task BroadcastResultStatusChangedAsync(DeploymentResultEventData eventData)
            => await Clients.OthersInGroup(GetGroupName(eventData.RequestId)).OnDeploymentResultStatusChanged(eventData);
    }
}


