namespace Dorc.Core.Interfaces
{
    /// <summary>
    /// Service for sending notifications to Microsoft Teams
    /// </summary>
    public interface ITeamsNotificationService
    {
        /// <summary>
        /// Sends a job completion notification to the user via MS Teams
        /// </summary>
        /// <param name="userName">The username or email of the user to notify</param>
        /// <param name="requestId">The deployment request ID</param>
        /// <param name="status">The final status of the deployment</param>
        /// <param name="environment">The environment name</param>
        /// <param name="project">The project name</param>
        /// <param name="buildNumber">The build number</param>
        /// <returns>Task representing the async operation</returns>
        Task NotifyJobCompletionAsync(
            string userName,
            int requestId,
            string status,
            string environment,
            string project,
            string buildNumber);
    }
}
