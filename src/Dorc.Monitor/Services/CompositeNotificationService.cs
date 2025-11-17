using Dorc.Core.Interfaces;
using Dorc.Core.Notifications;
using log4net;

namespace Dorc.Monitor.Services
{
    /// <summary>
    /// Composite notification service that sends notifications through multiple providers
    /// </summary>
    public class CompositeNotificationService : IJobNotificationService
    {
        private readonly ILog _logger;
        private readonly IEnumerable<IUserNotificationService> _notificationServices;

        public CompositeNotificationService(
            ILog logger,
            IEnumerable<IUserNotificationService> notificationServices)
        {
            _logger = logger;
            _notificationServices = notificationServices;
        }

        public async Task NotifyJobCompletionAsync(
            string userName,
            int requestId,
            string status,
            string environment,
            string project,
            string buildNumber)
        {
            var notification = new JobCompletionNotification
            {
                UserIdentifier = userName,
                RequestId = requestId,
                Status = status,
                Environment = environment,
                Project = project,
                BuildNumber = buildNumber,
                Timestamp = DateTimeOffset.UtcNow
            };

            var tasks = new List<Task>();

            foreach (var service in _notificationServices)
            {
                if (!service.IsConfigured)
                {
                    _logger.Debug($"Notification service '{service.ProviderName}' is not configured - skipping");
                    continue;
                }

                _logger.Debug($"Sending notification via '{service.ProviderName}' for request {requestId}");
                
                // Send notifications in parallel but don't let one failure affect others
                tasks.Add(SendNotificationSafelyAsync(service, notification));
            }

            if (tasks.Count > 0)
            {
                await Task.WhenAll(tasks);
            }
            else
            {
                _logger.Debug($"No notification services configured - skipping notification for request {requestId}");
            }
        }

        private async Task SendNotificationSafelyAsync(
            IUserNotificationService service,
            JobCompletionNotification notification)
        {
            try
            {
                await service.SendJobCompletionNotificationAsync(notification);
            }
            catch (Exception ex)
            {
                if (IsFatalException(ex))
                {
                    throw;
                }
                _logger.Error($"Failed to send notification via '{service.ProviderName}' for request {notification.RequestId}", ex);
            }
        }

        private static bool IsFatalException(Exception ex)
        {
            return ex is OutOfMemoryException
                || ex is StackOverflowException
                || ex is ThreadAbortException;
        }
    }
}
