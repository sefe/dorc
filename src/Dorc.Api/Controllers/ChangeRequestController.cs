using Dorc.Api.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Dorc.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    [Route("api/[controller]")]
    public class ChangeRequestController : ControllerBase
    {
        private readonly IServiceNowService _serviceNowService;
        private readonly ILogger<ChangeRequestController> _logger;

        public ChangeRequestController(
            IServiceNowService serviceNowService,
            ILogger<ChangeRequestController> logger)
        {
            _serviceNowService = serviceNowService;
            _logger = logger;
        }

        /// <summary>
        /// Validates a Change Request number against ServiceNow
        /// </summary>
        /// <param name="crNumber">The Change Request number to validate (e.g., CHG0012345)</param>
        /// <returns>Validation result indicating if the CR is valid and in Implement state</returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(ChangeRequestValidationResult))]
        [SwaggerResponse(StatusCodes.Status400BadRequest, Type = typeof(string))]
        [HttpGet("validate")]
        public async Task<IActionResult> ValidateChangeRequest([FromQuery] string crNumber)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(crNumber))
                {
                    return BadRequest("Change Request number is required");
                }

                var safeCrNumber = crNumber
                    .Replace("\r", string.Empty)
                    .Replace("\n", string.Empty);

                _logger.LogInformation("Validating CR {CrNumber} for user {User}",
                    safeCrNumber, User.Identity?.Name ?? "Unknown");

                var result = await _serviceNowService.ValidateChangeRequestAsync(crNumber);

                return Ok(result);
            }
            catch (Exception ex)
            {
                var safeCrNumber = crNumber?
                    .Replace("\r", string.Empty)
                    .Replace("\n", string.Empty);
                _logger.LogError(ex, "Error validating CR {CrNumber}: {Message}", safeCrNumber, ex.Message);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    $"An error occurred while validating the Change Request: {ex.Message}");
            }
        }

        /// <summary>
        /// Creates a standard Change Request in ServiceNow automatically (AutoCR).
        /// Used by the web UI "Auto-create CR" button and CLI /autocr flag.
        /// </summary>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(CreateChangeRequestResult))]
        [SwaggerResponse(StatusCodes.Status400BadRequest, Type = typeof(string))]
        [HttpPost("create")]
        public async Task<IActionResult> CreateChangeRequest([FromBody] CreateChangeRequestInput input)
        {
            try
            {
                if (input == null)
                {
                    return BadRequest("Request body is required");
                }

                // Fill in the requesting user from the auth context if not provided
                if (string.IsNullOrEmpty(input.RequestedBy))
                {
                    input.RequestedBy = User.Identity?.Name ?? "Unknown";
                }

                _logger.LogInformation("AutoCR requested by {User} for project {Project} to {Environment}",
                    User.Identity?.Name ?? "Unknown", input.ProjectName, input.Environment);

                var result = await _serviceNowService.CreateChangeRequestAsync(input);

                if (!result.Success)
                {
                    return BadRequest(result);
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating AutoCR");
                return StatusCode(StatusCodes.Status500InternalServerError,
                    $"An error occurred while creating the Change Request: {ex.Message}");
            }
        }
    }
}
