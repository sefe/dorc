using Dorc.ApiModel;
using Microsoft.Extensions.Logging;

namespace Dorc.Monitor.Notifications
{
    // Single owner of the fire-and-forget swallow policy: notification delivery must never
    // block or fail the calling request processor.
    internal static class DeploymentNotificationDispatch
    {
        public static void FireAndForget(
            IDeploymentNotificationSink sink,
            ILogger logger,
            DeploymentRequestApiModel request,
            string finalStatus,
            DateTimeOffset startedTime,
            DateTimeOffset completedTime)
        {
            try
            {
                var notifyTask = sink.NotifyRequestCompletedAsync(request, finalStatus, startedTime, completedTime);
                _ = notifyTask.ContinueWith(
                    t => logger.LogError(t.Exception, "Notification failed for request {RequestId}.", request.Id),
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default);
            }
            catch (Exception ex) when (!FatalExceptions.Is(ex))
            {
                logger.LogError(ex, "Notification failed synchronously for request {RequestId}.", request.Id);
            }
        }

        public static void FireAndForgetBatch(
            IDeploymentNotificationSink sink,
            ILogger logger,
            IReadOnlyCollection<DeploymentRequestApiModel> requests,
            string finalStatus,
            DateTimeOffset completedTime)
        {
            if (requests.Count == 0)
                return;

            try
            {
                var notifyTask = sink.NotifyRequestsCompletedAsync(requests, finalStatus, completedTime);
                _ = notifyTask.ContinueWith(
                    t => logger.LogError(t.Exception, "Batch notification failed for requests [{RequestIds}].", Describe(requests)),
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default);
            }
            catch (Exception ex) when (!FatalExceptions.Is(ex))
            {
                logger.LogError(ex, "Batch notification failed synchronously for requests [{RequestIds}].", Describe(requests));
            }
        }

        private static string Describe(IReadOnlyCollection<DeploymentRequestApiModel> requests) =>
            string.Join(",", requests.Select(r => r.Id));
    }
}
