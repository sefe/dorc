using Dorc.Api.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Dorc.Api.Controllers
{
    //TODO: Re-enable [Authorize] once LDAP/AD is available in dev environment
    //[Authorize]
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

                _logger.LogInformation("Validating CR {CrNumber} for user {User}",
                    crNumber, User.Identity?.Name ?? "Unknown");

                var result = await _serviceNowService.ValidateChangeRequestAsync(crNumber);

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating CR {CrNumber}: {Message}", crNumber, ex.Message);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    $"An error occurred while validating the Change Request: {ex.Message}");
            }
        }
    }
}
