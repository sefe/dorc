using Dorc.Api.Interfaces;
using Dorc.Api.Models;
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

        private static string Sanitize(string? value) =>
            (value ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);

        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(ChangeRequestValidationResult))]
        [SwaggerResponse(StatusCodes.Status400BadRequest, Type = typeof(string))]
        [HttpGet("validate")]
        public async Task<IActionResult> ValidateChangeRequest([FromQuery] string crNumber)
        {
            if (string.IsNullOrWhiteSpace(crNumber))
                return BadRequest("Change Request number is required");

            _logger.LogInformation("Validating CR {CrNumber} for user {User}",
                Sanitize(crNumber), User.Identity?.Name ?? "Unknown");

            return Ok(await _serviceNowService.ValidateChangeRequestAsync(crNumber));
        }

        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(CreateChangeRequestResult))]
        [SwaggerResponse(StatusCodes.Status400BadRequest, Type = typeof(string))]
        [HttpPost("create")]
        public async Task<IActionResult> CreateChangeRequest([FromBody] CreateChangeRequestInput input)
        {
            if (input == null)
                return BadRequest("Request body is required");

            if (string.IsNullOrEmpty(input.RequestedBy))
                input.RequestedBy = User.Identity?.Name ?? "Unknown";

            _logger.LogInformation("AutoCR requested by {User} for project {Project} to {Environment}",
                User.Identity?.Name ?? "Unknown", Sanitize(input.ProjectName), Sanitize(input.Environment));

            await TryMergeCrInputsAsync(input);

            var result = await _serviceNowService.CreateChangeRequestAsync(input);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        private async Task TryMergeCrInputsAsync(CreateChangeRequestInput input)
        {
            if (string.IsNullOrEmpty(input.ProjectName)) return;

            var crInputs = await _crInputsProvider.GetCrInputsAsync(input.ProjectName);
            if (crInputs == null)
            {
                _logger.LogInformation("cr-inputs.json not found for project '{Project}'. Using defaults.",
                    Sanitize(input.ProjectName));
                return;
            }

            MergeCrInputs(input, crInputs);
            input.CrInputsFetched = true;
            _logger.LogInformation("Auto-fetched cr-inputs.json for project '{Project}': group='{Group}', service='{Service}'",
                Sanitize(input.ProjectName), Sanitize(input.AssignmentGroup), Sanitize(input.BusinessService));
        }

        private static void MergeCrInputs(CreateChangeRequestInput input, CrInputsModel crInputs)
        {
            static string Pick(string current, string fallback) =>
                string.IsNullOrEmpty(current) && !string.IsNullOrEmpty(fallback) ? fallback : current;

            input.AssignmentGroup = Pick(input.AssignmentGroup, crInputs.AssignmentGroup);
            input.BusinessService = Pick(input.BusinessService, crInputs.BusinessService);
            input.ChgModel = Pick(input.ChgModel, crInputs.ChgModel);
            input.Type = Pick(input.Type, crInputs.Type);
            input.ShortDescription = Pick(input.ShortDescription, crInputs.ShortDescription);
            input.BackoutPlan = Pick(input.BackoutPlan, crInputs.BackoutPlan);
            input.ImplementationPlan = Pick(input.ImplementationPlan, crInputs.ImplementationPlan);
            input.Justification = Pick(input.Justification, crInputs.Justification);
            input.TestPlan = Pick(input.TestPlan, crInputs.TestPlan);
            input.RiskImpactAnalysis = Pick(input.RiskImpactAnalysis, crInputs.RiskImpactAnalysis);
            input.WorkNotes = Pick(input.WorkNotes, crInputs.WorkNotes);
            input.Category = Pick(input.Category, crInputs.Category);
            input.CorrelationId = Pick(input.CorrelationId, crInputs.CorrelationId);
            input.Impact = Pick(input.Impact, crInputs.Impact);
            input.Priority = Pick(input.Priority, crInputs.Priority);
            input.Urgency = Pick(input.Urgency, crInputs.Urgency);
        }
    }
}