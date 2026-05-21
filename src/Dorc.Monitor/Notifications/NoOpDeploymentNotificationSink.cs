using Dorc.ApiModel;
using Microsoft.Extensions.Logging;

namespace Dorc.Monitor.Teams
{
    internal sealed class NoOpDeploymentNotificationSink : IDeploymentNotificationSink
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<NoOpDeploymentNotificationSink> _logger;

        public NoOpDeploymentNotificationSink(
            ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<NoOpDeploymentNotificationSink>();
        }

        public async Task NotifyRequestCompletedAsync(
            DeploymentRequestApiModel request,
            string finalStatus,
            DateTimeOffset startedTime,
            DateTimeOffset completedTime)
        {
            _logger.LogInformation("Notifications have not been enabled");
        }
    }
}