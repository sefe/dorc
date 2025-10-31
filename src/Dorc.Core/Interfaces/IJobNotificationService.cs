namespace Dorc.Core.Interfaces
{
    /// <summary>
    /// Service for sending job completion notifications to users
    /// </summary>
    public interface IJobNotificationService
    {
        /// <summary>
        /// Sends a job completion notification to the user
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
