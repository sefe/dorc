namespace Dorc.Api.Interfaces
{
    /// <summary>
    /// Service for interacting with ServiceNow API
    /// </summary>
    public interface IServiceNowService
    {
        /// <summary>
        /// Validates that a Change Request exists and is in Implement state
        /// </summary>
        /// <param name="crNumber">The Change Request number (e.g., CHG0012345)</param>
        /// <returns>Validation result with status and message</returns>
        Task<ChangeRequestValidationResult> ValidateChangeRequestAsync(string crNumber);
    }

    public class ChangeRequestValidationResult
    {
        public bool IsValid { get; set; }
        public string Message { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string CrNumber { get; set; } = string.Empty;

        /// <summary>
        /// Short description of the change request
        /// </summary>
        public string ShortDescription { get; set; } = string.Empty;

        /// <summary>
        /// Start of the approved change window
        /// </summary>
        public string StartDate { get; set; } = string.Empty;

        /// <summary>
        /// End of the approved change window
        /// </summary>
        public string EndDate { get; set; } = string.Empty;
    }
}
