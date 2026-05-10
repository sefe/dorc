using Dorc.Api.Interfaces;
using Dorc.ApiModel;
using Dorc.Core.AzureStorageAccount;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Dorc.Terraform.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using System.Security.Principal;

namespace Dorc.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class TerraformController : ControllerBase
    {
        private readonly ILogger _log;
        private readonly IRequestsPersistentSource _requestsPersistentSource;
        private readonly ISecurityPrivilegesChecker _apiSecurityService;
        private readonly IClaimsPrincipalReader _claimsPrincipalReader;
        private readonly IAzureStorageAccountWorker _azureStorageAccountWorker;
        private readonly ITemplateCatalog _templateCatalog;
        private readonly IProjectsPersistentSource _projectsPersistentSource;
        private readonly IManageProjectsPersistentSource _manageProjectsPersistentSource;

        public TerraformController(
            ILogger<TerraformController> log,
            IRequestsPersistentSource requestsPersistentSource,
            ISecurityPrivilegesChecker apiSecurityService,
            IClaimsPrincipalReader claimsPrincipalReader,
            IAzureStorageAccountWorker azureStorageAccountWorker,
            ITemplateCatalog templateCatalog,
            IProjectsPersistentSource projectsPersistentSource,
            IManageProjectsPersistentSource manageProjectsPersistentSource)
        {
            _log = log;
            _requestsPersistentSource = requestsPersistentSource;
            _apiSecurityService = apiSecurityService;
            _claimsPrincipalReader = claimsPrincipalReader;
            _azureStorageAccountWorker = azureStorageAccountWorker;
            _templateCatalog = templateCatalog;
            _projectsPersistentSource = projectsPersistentSource;
            _manageProjectsPersistentSource = manageProjectsPersistentSource;
        }

        /// <summary>
        /// Lists all stock Terraform templates available in the catalog.
        /// </summary>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(IReadOnlyList<TerraformTemplateManifest>))]
        [HttpGet("templates")]
        public async Task<IActionResult> ListTemplates(CancellationToken cancellationToken)
        {
            var manifests = await _templateCatalog.ListAsync(cancellationToken);
            return Ok(manifests);
        }

        /// <summary>
        /// Gets the latest version of a named stock template.
        /// </summary>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(TerraformTemplateManifest))]
        [SwaggerResponse(StatusCodes.Status404NotFound)]
        [HttpGet("templates/{name}")]
        public async Task<IActionResult> GetTemplateLatest(string name, CancellationToken cancellationToken)
        {
            var manifest = await _templateCatalog.GetAsync(name, cancellationToken);
            return manifest is null ? NotFound() : Ok(manifest);
        }

        /// <summary>
        /// Gets a specific (name, version) of a stock template.
        /// </summary>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(TerraformTemplateManifest))]
        [SwaggerResponse(StatusCodes.Status404NotFound)]
        [HttpGet("templates/{name}/{version}")]
        public async Task<IActionResult> GetTemplateVersion(string name, string version, CancellationToken cancellationToken)
        {
            var manifest = await _templateCatalog.GetAsync(name, version, cancellationToken);
            return manifest is null ? NotFound() : Ok(manifest);
        }

        /// <summary>
        /// Instantiates a stock template as a new Catalog-mode component in
        /// the destination project. The engineer then deploys the new
        /// component through the existing DOrc deploy flow.
        ///
        /// This endpoint is the "Deploy from template" entry point used by
        /// the Stock Modules page in the dorc-web UI. It does NOT trigger
        /// a deployment itself; it only persists a new ComponentApiModel
        /// pre-wired with TerraformSourceType=Catalog and the chosen
        /// (TerraformTemplateName, TerraformTemplateVersion).
        /// </summary>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(ComponentApiModel))]
        [SwaggerResponse(StatusCodes.Status400BadRequest)]
        [SwaggerResponse(StatusCodes.Status403Forbidden)]
        [SwaggerResponse(StatusCodes.Status404NotFound)]
        [HttpPost("templates/{name}/{version}/instantiate")]
        public async Task<IActionResult> InstantiateTemplate(
            string name,
            string version,
            [FromBody] TerraformTemplateInstantiateRequestApiModel request,
            CancellationToken cancellationToken)
        {
            if (request is null)
            {
                return BadRequest("Request body is required.");
            }
            if (request.ProjectId <= 0)
            {
                return BadRequest("ProjectId is required and must be a positive integer.");
            }

            var manifest = await _templateCatalog.GetAsync(name, version, cancellationToken);
            if (manifest is null)
            {
                return NotFound($"Stock template '{name}@{version}' was not found in the catalog.");
            }

            var project = _projectsPersistentSource.GetProject(request.ProjectId);
            if (project is null)
            {
                return NotFound($"Project with id {request.ProjectId} was not found.");
            }

            if (!_apiSecurityService.IsProjectOwnerOrAdmin(User, project.ProjectName))
            {
                return Forbid();
            }

            var componentName = string.IsNullOrWhiteSpace(request.ComponentName)
                ? manifest.Name
                : request.ComponentName.Trim();

            var component = new ComponentApiModel
            {
                ComponentName = componentName,
                ScriptPath = string.Empty,
                ComponentType = ComponentType.Terraform,
                TerraformSourceType = TerraformSourceType.Catalog,
                TerraformTemplateName = manifest.Name,
                TerraformTemplateVersion = manifest.Version,
                IsEnabled = true,
                StopOnFailure = true,
            };

            var username = _claimsPrincipalReader.GetUserFullDomainName(User);
            // Sanitize user-supplied strings before they reach the logger -
            // strip CR/LF so an attacker cannot inject forged log lines via
            // the component-name field. Non-printable control characters are
            // also trimmed for the same reason.
            var safeComponentName = SanitizeForLog(componentName);
            // GetUserName returns a non-email identifier and is the common
            // pattern used by the rest of this controller; avoids leaking
            // an email-classed PII into platform logs.
            var safeUserId = SanitizeForLog(_claimsPrincipalReader.GetUserName(User));

            try
            {
                _manageProjectsPersistentSource.CreateComponent(
                    component,
                    request.ProjectId,
                    request.ParentComponentId,
                    username);
            }
            catch (InvalidOperationException ex)
            {
                // Persistence-layer business/state failures (duplicate component
                // name, project-shape violation, etc.) are the expected,
                // actionable case here. Anything else bubbles up to the
                // framework's centralized handler so stacks and types are
                // preserved instead of being masked as a flat 500.
                _log.LogError(ex,
                    "Failed to instantiate template '{Manifest}' as component '{Component}' in project {ProjectId}.",
                    $"{manifest.Name}@{manifest.Version}", safeComponentName, request.ProjectId);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    "Failed to create the component for the chosen template. See server logs.");
            }

            _log.LogInformation(
                "Stock template '{Manifest}' instantiated as component '{Component}' in project '{ProjectName}' (id {ProjectId}) by {UserId}.",
                $"{manifest.Name}@{manifest.Version}", safeComponentName, project.ProjectName, request.ProjectId, safeUserId);

            return Ok(component);
        }

        /// <summary>
        /// Strips CR / LF / other control characters from a string before it
        /// reaches the structured logger, preventing log-injection attacks
        /// where a user-supplied value forges fake log entries by embedding
        /// newlines.
        /// </summary>
        private static string SanitizeForLog(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var buf = new System.Text.StringBuilder(value.Length);
            foreach (var c in value.Where(c => c >= 0x20 && c != 0x7f))
            {
                buf.Append(c);
            }
            // Cap length so a pathological caller can't produce an unbounded
            // log line.
            const int cap = 256;
            return buf.Length <= cap ? buf.ToString() : buf.ToString(0, cap) + "[truncated]";
        }

        /// <summary>
        /// Gets Terraform plan for a deployment result
        /// </summary>
        /// <param name="deploymentResultId">The deployment result ID</param>
        /// <returns>The Terraform plan details</returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(TerraformPlanApiModel))]
        [SwaggerResponse(StatusCodes.Status404NotFound)]
        [HttpGet("plan/{deploymentResultId}")]
        public IActionResult GetTerraformPlan(int deploymentResultId)
        {
            try
            {
                _log.LogInformation($"Getting Terraform plan for deployment result ID: {deploymentResultId}");

                // Get the deployment result
                var deploymentResult = _requestsPersistentSource.GetDeploymentResults(deploymentResultId);
                if (deploymentResult == null)
                {
                    return NotFound($"Deployment result with ID {deploymentResultId} not found");
                }

                var deploymentRequest = _requestsPersistentSource.GetRequest(deploymentResult.RequestId);

                if (!HasViewPermission(deploymentRequest))
                {
                    return Forbid();
                }

                // Load plan content from storage
                var planContent = LoadPlanContentFromStorage(deploymentResultId);
                
                var plan = new TerraformPlanApiModel
                {
                    DeploymentResultId = deploymentResultId,
                    PlanContent = planContent,
                    CreatedAt = deploymentResult.StartedTime?.DateTime ?? DateTime.UtcNow,
                    Status = deploymentResult.Status ?? "Unknown"
                };

                return Ok(plan);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Failed to get Terraform plan for deployment result ID {deploymentResultId}: {ex.Message}");
                return StatusCode(StatusCodes.Status500InternalServerError, "Failed to retrieve Terraform plan");
            }
        }

        /// <summary>
        /// Confirms a Terraform plan for execution
        /// </summary>
        /// <param name="deploymentResultId">The deployment result ID</param>
        /// <returns>Success response</returns>
        [SwaggerResponse(StatusCodes.Status200OK)]
        [SwaggerResponse(StatusCodes.Status404NotFound)]
        [HttpPost("plan/{deploymentResultId}/confirm")]
        public IActionResult ConfirmTerraformPlan(int deploymentResultId)
        {
            try
            {
                _log.LogInformation($"Confirming Terraform plan for deployment result ID: {deploymentResultId}");

                // Get the deployment result
                var deploymentResult = _requestsPersistentSource.GetDeploymentResults(deploymentResultId);
                if (deploymentResult == null)
                {
                    return NotFound($"Deployment result with ID {deploymentResultId} not found");
                }

                var deploymentRequest = _requestsPersistentSource.GetRequest(deploymentResult.RequestId);

                if (!HasConfirmPermission(deploymentRequest))
                {
                    return Forbid();
                }

                // Validate that the deployment is in the correct status
                if (deploymentResult.Status != DeploymentResultStatus.WaitingConfirmation.ToString())
                {
                    return BadRequest($"Deployment result {deploymentResultId} is not in WaitingConfirmation status. Current status: {deploymentResult.Status}");
                }

                // Update deployment result status to Confirmed
                _requestsPersistentSource.UpdateResultStatus(
                    deploymentResult,
                    DeploymentResultStatus.Confirmed);

                // Update deployment request status to Confirmed
                _requestsPersistentSource.UpdateRequestStatus(
                    deploymentResult.RequestId,
                    DeploymentRequestStatus.Confirmed);

                // Log the confirmation action for audit purposes
                var userName = _claimsPrincipalReader.GetUserName(User);
                _log.LogInformation($"Terraform plan confirmed for deployment result ID: {deploymentResultId} by user: {userName}");
                
                // Note: The actual execution will be handled by the Monitor service when it picks up the Confirmed status
                
                return Ok(new { 
                    message = "Terraform plan confirmed successfully",
                    deploymentResultId = deploymentResultId,
                    confirmedBy = userName,
                    confirmedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Failed to confirm Terraform plan for deployment result ID {deploymentResultId}: {ex.Message}", ex);
                return StatusCode(StatusCodes.Status500InternalServerError, "Failed to confirm Terraform plan");
            }
        }

        /// <summary>
        /// Declines a Terraform plan
        /// </summary>
        /// <param name="deploymentResultId">The deployment result ID</param>
        /// <returns>Success response</returns>
        [SwaggerResponse(StatusCodes.Status200OK)]
        [SwaggerResponse(StatusCodes.Status404NotFound)]
        [HttpPost("plan/{deploymentResultId}/decline")]
        public IActionResult DeclineTerraformPlan(int deploymentResultId)
        {
            try
            {
                _log.LogInformation($"Declining Terraform plan for deployment result ID: {deploymentResultId}");

                // Get the deployment result
                var deploymentResult = _requestsPersistentSource.GetDeploymentResults(deploymentResultId);
                if (deploymentResult == null)
                {
                    return NotFound($"Deployment result with ID {deploymentResultId} not found");
                }

                var deploymentRequest = _requestsPersistentSource.GetRequest(deploymentResult.RequestId);

                if (!HasDeclinePermission(deploymentRequest))
                {
                    return Forbid();
                }

                // Validate that the deployment is in the correct status
                if (deploymentResult.Status != DeploymentResultStatus.WaitingConfirmation.ToString())
                {
                    return BadRequest($"Deployment result {deploymentResultId} is not in WaitingConfirmation status. Current status: {deploymentResult.Status}");
                }

                // Update deployment result status to Cancelled
                _requestsPersistentSource.UpdateResultStatus(
                    deploymentResult,
                    DeploymentResultStatus.Cancelled);

                // Update deployment result status to Cancelled
                _requestsPersistentSource.UpdateRequestStatus(
                    deploymentResult.RequestId,
                    DeploymentRequestStatus.Cancelled);

                // Log the decline action for audit purposes
                var userName = _claimsPrincipalReader.GetUserName(User);
                _log.LogInformation($"Terraform plan declined for deployment result ID: {deploymentResultId} by user: {userName}");

                return Ok(new { 
                    message = "Terraform plan declined successfully",
                    deploymentResultId = deploymentResultId,
                    declinedBy = userName,
                    declinedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Failed to decline Terraform plan for deployment result ID {deploymentResultId}: {ex.Message}", ex);
                return StatusCode(StatusCodes.Status500InternalServerError, "Failed to decline Terraform plan");
            }
        }

        private bool HasViewPermission(DeploymentRequestApiModel? request)
        {
            if (request is null) return false;
            if (!string.IsNullOrEmpty(request.EnvironmentName)
                && _apiSecurityService.IsEnvironmentOwnerOrAdmin(User, request.EnvironmentName))
            {
                return true;
            }
            if (!string.IsNullOrEmpty(request.Project)
                && _apiSecurityService.IsProjectOwnerOrAdmin(User, request.Project))
            {
                return true;
            }
            return false;
        }

        private bool HasConfirmPermission(DeploymentRequestApiModel? request)
        {
            if (request is null) return false;
            if (string.IsNullOrEmpty(request.EnvironmentName)) return false;
            return _apiSecurityService.CanModifyEnvironment(User, request.EnvironmentName);
        }

        private bool HasDeclinePermission(DeploymentRequestApiModel? request)
            => HasConfirmPermission(request);

        private string LoadPlanContentFromStorage(int deploymentResultId)
        {
            try
            {
                var terraformPlanBlobName = deploymentResultId.CreateTerraformPlanContentBlobName();
                var blobContent = _azureStorageAccountWorker.LoadFileFromBlobs(terraformPlanBlobName);

                return blobContent;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Failed to load plan content for deployment result ID {deploymentResultId}: {ex.Message}", ex);
                throw;
            }
        }
    }
}