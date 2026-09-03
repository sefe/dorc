using Dorc.ApiModel;
using Microsoft.Extensions.Logging;

namespace Dorc.Monitor.Notifications
{
    internal sealed class NoOpDeploymentNotificationSink : IDeploymentNotificationSink
    {
        private readonly ILogger<NoOpDeploymentNotificationSink> _logger;

        public NoOpDeploymentNotificationSink(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<NoOpDeploymentNotificationSink>();
        }

        public Task NotifyRequestCompletedAsync(
            DeploymentRequestApiModel request,
            string finalStatus,
            DateTimeOffset startedTime,
            DateTimeOffset completedTime)
        {
            _logger.LogDebug("Teams notifications are disabled; skipping notification for request {RequestId}.", request.Id);
            return Task.CompletedTask;
        }

        public Task NotifyRequestsCompletedAsync(
            IReadOnlyCollection<DeploymentRequestApiModel> requests,
            string finalStatus,
            DateTimeOffset completedTime)
        {
            _logger.LogDebug(
                "Teams notifications are disabled; skipping batch notification for {Count} request(s).",
                requests.Count);
            return Task.CompletedTask;
        }
    }
}
