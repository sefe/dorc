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
        private readonly ICrInputsProvider _crInputsProvider;
        private readonly ILogger<ChangeRequestController> _logger;

        public ChangeRequestController(
            IServiceNowService serviceNowService,
            ICrInputsProvider crInputsProvider,
            ILogger<ChangeRequestController> logger)
        {
            _serviceNowService = serviceNowService;
            _crInputsProvider = crInputsProvider;
            _logger = logger;
        }

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

                var safeProjectName = (input.ProjectName ?? string.Empty)
                    .Replace("\r", string.Empty)
                    .Replace("\n", string.Empty);
                var safeEnvironment = (input.Environment ?? string.Empty)
                    .Replace("\r", string.Empty)
                    .Replace("\n", string.Empty);

                _logger.LogInformation("AutoCR requested by {User} for project {Project} to {Environment}",
                    User.Identity?.Name ?? "Unknown", safeProjectName, safeEnvironment);

                // Auto-fetch cr-inputs.json from the project's Azure DevOps repo
                if (!string.IsNullOrEmpty(input.ProjectName))
                {
                    try
                    {
                        var crInputs = await _crInputsProvider.GetCrInputsAsync(input.ProjectName);
                        if (crInputs != null)
                        {
                            MergeCrInputs(input, crInputs);
                            input.CrInputsFetched = true;
                            _logger.LogInformation("Auto-fetched cr-inputs.json for project '{Project}': " +
                                "assignment_group='{Group}', business_service='{Service}'",
                                safeProjectName, input.AssignmentGroup, input.BusinessService);
                        }
                        else
                        {
                            _logger.LogInformation("cr-inputs.json not found for project '{Project}'. " +
                                "Will use global config and hardcoded defaults.", safeProjectName);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to auto-fetch cr-inputs.json for project '{Project}'. " +
                            "Will use global config and hardcoded defaults.", safeProjectName);
                    }
                }

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

        private static void MergeCrInputs(CreateChangeRequestInput input, CrInputsModel crInputs)
        {
            if (string.IsNullOrEmpty(input.AssignmentGroup) && !string.IsNullOrEmpty(crInputs.AssignmentGroup))
                input.AssignmentGroup = crInputs.AssignmentGroup;
            if (string.IsNullOrEmpty(input.BusinessService) && !string.IsNullOrEmpty(crInputs.BusinessService))
                input.BusinessService = crInputs.BusinessService;
            if (string.IsNullOrEmpty(input.ChgModel) && !string.IsNullOrEmpty(crInputs.ChgModel))
                input.ChgModel = crInputs.ChgModel;
            if (string.IsNullOrEmpty(input.Type) && !string.IsNullOrEmpty(crInputs.Type))
                input.Type = crInputs.Type;
            if (string.IsNullOrEmpty(input.ShortDescription) && !string.IsNullOrEmpty(crInputs.ShortDescription))
                input.ShortDescription = crInputs.ShortDescription;
            if (string.IsNullOrEmpty(input.BackoutPlan) && !string.IsNullOrEmpty(crInputs.BackoutPlan))
                input.BackoutPlan = crInputs.BackoutPlan;
            if (string.IsNullOrEmpty(input.ImplementationPlan) && !string.IsNullOrEmpty(crInputs.ImplementationPlan))
                input.ImplementationPlan = crInputs.ImplementationPlan;
            if (string.IsNullOrEmpty(input.Justification) && !string.IsNullOrEmpty(crInputs.Justification))
                input.Justification = crInputs.Justification;
            if (string.IsNullOrEmpty(input.TestPlan) && !string.IsNullOrEmpty(crInputs.TestPlan))
                input.TestPlan = crInputs.TestPlan;
            if (string.IsNullOrEmpty(input.RiskImpactAnalysis) && !string.IsNullOrEmpty(crInputs.RiskImpactAnalysis))
                input.RiskImpactAnalysis = crInputs.RiskImpactAnalysis;
            if (string.IsNullOrEmpty(input.WorkNotes) && !string.IsNullOrEmpty(crInputs.WorkNotes))
                input.WorkNotes = crInputs.WorkNotes;
            if (string.IsNullOrEmpty(input.Category) && !string.IsNullOrEmpty(crInputs.Category))
                input.Category = crInputs.Category;
            if (string.IsNullOrEmpty(input.CorrelationId) && !string.IsNullOrEmpty(crInputs.CorrelationId))
                input.CorrelationId = crInputs.CorrelationId;
            if (string.IsNullOrEmpty(input.Impact) && !string.IsNullOrEmpty(crInputs.Impact))
                input.Impact = crInputs.Impact;
            if (string.IsNullOrEmpty(input.Priority) && !string.IsNullOrEmpty(crInputs.Priority))
                input.Priority = crInputs.Priority;
            if (string.IsNullOrEmpty(input.Urgency) && !string.IsNullOrEmpty(crInputs.Urgency))
                input.Urgency = crInputs.Urgency;
        }
    }
}