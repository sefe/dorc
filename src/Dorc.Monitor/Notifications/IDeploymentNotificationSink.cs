using Dorc.ApiModel;

namespace Dorc.Monitor.Teams
{
    internal interface IDeploymentNotificationSink
    {
        Task NotifyRequestCompletedAsync(
            DeploymentRequestApiModel request,
            string finalStatus,
            DateTimeOffset startedTime,
            DateTimeOffset completedTime);
    }
}