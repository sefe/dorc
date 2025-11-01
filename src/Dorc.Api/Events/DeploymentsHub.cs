using Dorc.Core.Events;
using Dorc.Core.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Dorc.Api.Events
{
    /// <summary>
    /// SignalR hub for deployment events
    /// </summary>
    /// <remarks>
    /// You can generate Typescript client for that SignalR hub using the following command:
    /// <code>dotnet tsrts --project Dorc.Core/Dorc.Core.csproj --output Dorc-web/src/services/ServerEvents/DeploymentEventsGenerated</code>
    /// </remarks>
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public sealed class DeploymentsHub : Hub<IDeploymentsEventsClient>, IDeploymentEventsHub
    {
        private readonly IDeploymentSubscriptionsGroupTracker _tracker;
        private readonly ILogger<DeploymentsHub> _logger;

        public DeploymentsHub(
            IDeploymentSubscriptionsGroupTracker tracker,
            ILogger<DeploymentsHub> logger)
        {
            _tracker = tracker;
            _logger = logger;
        }

        public static string GetGroupName(int requestId) => $"req:{requestId}";

        public override Task OnConnectedAsync()
        {
            _tracker.RegisterConnection(Context.ConnectionId);
            return base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            // Get all groups this connection was part of and remove from Azure SignalR
            var groupsForConnection = _tracker.GetGroupsForConnection(Context.ConnectionId);
                
            if (groupsForConnection.Count > 0)
            {
                // Explicitly remove current connection from all groups in Azure SignalR
                foreach (var groupName in groupsForConnection)
                {
                    try
                    {
                        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, 
                            "Failed to remove connection {ConnectionId} from group {GroupName}", 
                            Context.ConnectionId, groupName);
                    }
                }
            }
                
            _tracker.UnregisterConnection(Context.ConnectionId);
            
            await base.OnDisconnectedAsync(exception);
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
        {
            if (!Context.User.IsInRole("Admin"))
            {
                throw new HubException("Not authorized");
            }

            await Clients.Others.OnDeploymentRequestStarted(eventData);
        }

        public async Task BroadcastRequestStatusChangedAsync(DeploymentRequestEventData eventData)
        {
            if (!this.Context.User.IsInRole("Admin"))
            {
                throw new HubException("Not authorized");
            }

            var groupName = _tracker.GetGroupName(eventData.RequestId);
            var callerId = Context.ConnectionId;

            var unsubscribed = _tracker.GetUnsubscribedConnections(callerId);

            if (unsubscribed.Count > 0)
            {
                await Clients.Clients(unsubscribed).OnDeploymentRequestStatusChanged(eventData);
            }

            // since it's possible that group might not exist, update them in second order to do not fail updating usual clients
            await Clients.GroupExcept(groupName, new[] { callerId }).OnDeploymentRequestStatusChanged(eventData);
        }

        public async Task BroadcastResultStatusChangedAsync(DeploymentResultEventData eventData)
        {
            if (!this.Context.User.IsInRole("Admin"))
            {
                throw new HubException("Not authorized");
            }

            await Clients.OthersInGroup(_tracker.GetGroupName(eventData.RequestId)).OnDeploymentResultStatusChanged(eventData);
        }
    }
}


