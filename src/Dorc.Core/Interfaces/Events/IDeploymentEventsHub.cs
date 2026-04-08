using TypedSignalR.Client;

namespace Dorc.Core.Events
{
    [Hub]
    public interface IDeploymentEventsHub
    {
        Task BroadcastNewRequestAsync(DeploymentRequestEventData eventData);
        Task BroadcastRequestStatusChangedAsync(DeploymentRequestEventData eventData);
        Task BroadcastResultStatusChangedAsync(DeploymentResultEventData eventData);
        Task JoinRequestGroup(int requestId);
        Task LeaveRequestGroup(int requestId);
    }
}