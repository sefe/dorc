using Dorc.ApiModel;

namespace Dorc.Monitor.Notifications
{
    internal interface IDeploymentNotificationSink
    {
        Task NotifyRequestCompletedAsync(
            DeploymentRequestApiModel request,
            string finalStatus,
            DateTimeOffset startedTime,
            DateTimeOffset completedTime);

        /// <summary>
        /// Notifies about a set of requests that all reached <paramref name="finalStatus"/> together -
        /// a monitor restart sweep or a bulk cancel, where the whole batch is already in hand.
        /// Implementations group by requester so one person gets one message rather than one per request.
        /// </summary>
        Task NotifyRequestsCompletedAsync(
            IReadOnlyCollection<DeploymentRequestApiModel> requests,
            string finalStatus,
            DateTimeOffset completedTime);
    }
}
