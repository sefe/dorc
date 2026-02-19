namespace Dorc.Api.Interfaces
{
    /// <summary>
    /// Service for sending email notifications
    /// </summary>
    public interface IEmailNotificationService
    {
        /// <summary>
        /// Sends notification when a user overrides the CR requirement for production deployment
        /// </summary>
        /// <param name="username">The user who initiated the deployment</param>
        /// <param name="environment">The target environment</param>
        /// <param name="project">The project being deployed</param>
        /// <param name="buildNumber">The build number being deployed</param>
        /// <returns>Task representing the async operation</returns>
        Task SendCrOverrideNotificationAsync(string username, string environment, string project, string buildNumber);
    }
}
