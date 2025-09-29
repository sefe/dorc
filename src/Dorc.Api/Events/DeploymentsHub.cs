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
        private readonly IDeploymentSubscriptionsGroupTracker _tracker;

        public DeploymentsHub(IDeploymentSubscriptionsGroupTracker tracker)
        {
            _tracker = tracker;
        }

        public static string GetGroupName(int requestId) => $"req:{requestId}";

        public override Task OnConnectedAsync()
        {
            _tracker.RegisterConnection(Context.ConnectionId);
            return base.OnConnectedAsync();
        }

        public override Task OnDisconnectedAsync(Exception? exception)
        {
            _tracker.UnregisterConnection(Context.ConnectionId);
            return base.OnDisconnectedAsync(exception);
        }

        // Group management: clients call these to scope updates to a request
        public async Task JoinRequestGroup(int requestId)
        {
            var groupName = _tracker.GetGroupName(requestId);
            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
            _tracker.JoinGroup(Context.ConnectionId, requestId);
        }

        public async Task LeaveRequestGroup(int requestId)
        {
            var groupName = _tracker.GetGroupName(requestId);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
            _tracker.LeaveGroup(Context.ConnectionId, requestId);
        }

        public async Task BroadcastNewRequestAsync(DeploymentRequestEventData eventData)
            => await Clients.Others.OnDeploymentRequestStarted(eventData);

        public async Task BroadcastRequestStatusChangedAsync(DeploymentRequestEventData eventData)
        {
            var groupName = _tracker.GetGroupName(eventData.RequestId);
            var callerId = Context.ConnectionId;

            var unsubscribed = _tracker.GetUnsubscribedConnections(callerId);

            var tasks = new List<Task>
            {
                Clients.GroupExcept(groupName, new[] { callerId }).OnDeploymentRequestStatusChanged(eventData)
            };

            if (unsubscribed.Count > 0)
            {
                tasks.Add(Clients.Clients(unsubscribed).OnDeploymentRequestStatusChanged(eventData));
            }

            await Task.WhenAll(tasks);
        }

        public async Task BroadcastResultStatusChangedAsync(DeploymentResultEventData eventData)
            => await Clients.OthersInGroup(_tracker.GetGroupName(eventData.RequestId)).OnDeploymentResultStatusChanged(eventData);
    }
}


