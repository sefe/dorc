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

        /// <summary>
        /// Creates a standard Change Request in ServiceNow (AutoCR).
        /// Uses the same APIM gateway + AAD auth as validation.
        /// </summary>
        Task<CreateChangeRequestResult> CreateChangeRequestAsync(CreateChangeRequestInput input);
    }

    /// <summary>
    /// Input model for automatic CR creation.
    /// Maps to the fields required by ServiceNow's /change_request POST endpoint,
    /// matching the service-now-cli's ChangeRequestModel.
    /// </summary>
    public class CreateChangeRequestInput
    {
        /// <summary>Short description of the change (e.g., "[SEFE] Release MyApp to Production")</summary>
        public string ShortDescription { get; set; } = string.Empty;
        /// <summary>The DOrc project name</summary>
        public string ProjectName { get; set; } = string.Empty;
        /// <summary>The target environment name</summary>
        public string Environment { get; set; } = string.Empty;
        /// <summary>The build number being deployed</summary>
        public string BuildNumber { get; set; } = string.Empty;
        /// <summary>The user requesting the deployment</summary>
        public string RequestedBy { get; set; } = string.Empty;
    }

    public class CreateChangeRequestResult
    {
        public bool Success { get; set; }
        public string CrNumber { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
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
