using Dorc.Core.Notifications;

namespace Dorc.Core.Interfaces
{
    /// <summary>
    /// Generic interface for sending notifications to users via any messaging system
    /// </summary>
    public interface IUserNotificationService
    {
        /// <summary>
        /// Sends a job completion notification to a user
        /// </summary>
        /// <param name="notification">The notification data to send</param>
        /// <returns>Task representing the async operation</returns>
        Task SendJobCompletionNotificationAsync(JobCompletionNotification notification);

        /// <summary>
        /// Gets the name of the notification provider (e.g., "Teams", "Slack", "Email")
        /// </summary>
        string ProviderName { get; }

        /// <summary>
        /// Checks if the service is properly configured and ready to send notifications
        /// </summary>
        bool IsConfigured { get; }
    }
}
