using Dorc.Api.Interfaces;
using Dorc.Api.Model;
using Dorc.Api.Services;
using Dorc.ApiModel;
using Dorc.Core.AzureStorageAccount;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources;
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
        private readonly IParameterValidator _parameterValidator;
        private readonly IRequestService _requestService;

        public TerraformController(
            ILogger<TerraformController> log,
            IRequestsPersistentSource requestsPersistentSource,
            ISecurityPrivilegesChecker apiSecurityService,
            IClaimsPrincipalReader claimsPrincipalReader,
            IAzureStorageAccountWorker azureStorageAccountWorker,
            ITemplateCatalog templateCatalog,
            IProjectsPersistentSource projectsPersistentSource,
            IManageProjectsPersistentSource manageProjectsPersistentSource,
            IParameterValidator parameterValidator,
            IRequestService requestService)
        {
            _log = log;
            _requestsPersistentSource = requestsPersistentSource;
            _apiSecurityService = apiSecurityService;
            _claimsPrincipalReader = claimsPrincipalReader;
            _azureStorageAccountWorker = azureStorageAccountWorker;
            _templateCatalog = templateCatalog;
            _projectsPersistentSource = projectsPersistentSource;
            _manageProjectsPersistentSource = manageProjectsPersistentSource;
            _parameterValidator = parameterValidator;
            _requestService = requestService;
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
                // Explicitly 0 (not the int? default of null): the Post
                // validation pipeline requires ids to be 0, and
                // ManageProjectsPersistentSource.CreateComponent only
                // performs the insert inside `if (ComponentId == 0)` - a null
                // id silently no-ops the create.
                ComponentId = 0,
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

            // Same validation pipeline as RefDataController's component
            // create/update paths: charset, 64-char limit, and cross-project
            // name ownership. Without this, CreateComponent's legacy
            // duplicate handler would silently rename an existing same-named
            // component - even one owned by a different project - to a GUID
            // and hand its name to the one created here.
            try
            {
                _manageProjectsPersistentSource.ValidateComponents(
                    new List<ComponentApiModel> { component }, request.ProjectId, HttpRequestType.Post);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }

            var projectComponents = _projectsPersistentSource
                .GetComponentsForProject(project.ProjectName)
                .ToList();

            // A duplicate inside the destination project passes the
            // cross-project validation above but would still trigger the
            // legacy rename-the-existing-component behaviour: reject it
            // explicitly instead.
            if (projectComponents.Any(c =>
                    string.Equals(c.ComponentName, componentName, StringComparison.OrdinalIgnoreCase)))
            {
                return Conflict(
                    $"A component named '{componentName}' already exists in project '{project.ProjectName}'. Choose a different component name.");
            }

            // The parent, when supplied, must belong to the destination
            // project - otherwise the new component is grafted into another
            // project's component tree.
            if (request.ParentComponentId is int parentComponentId
                && !projectComponents.Any(c => c.ComponentId == parentComponentId))
            {
                return BadRequest(
                    $"Parent component id {parentComponentId} does not belong to project '{project.ProjectName}'.");
            }

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

            // create-and-deploy mode. When the wizard supplies an
            // environment + parameter values, additionally validate the
            // values, verify env-modify permission (controller-pin,
            // matching RequestController.Post), compose a Catalog-mode
            // RequestDto, and submit it through the existing
            // IRequestService.CreateRequest path. On failure we return an
            // error WITH the created Component still persisted; the caller
            // sees the component in their project and can retry the deploy.
            var deployRequested = !string.IsNullOrWhiteSpace(request.EnvironmentName);
            if (deployRequested)
            {
                // C-13 RBAC controller-pin: this check lives in the controller,
                // not RequestService. RequestService.CreateRequest does NOT
                // enforce CanModifyEnvironment.
                if (!_apiSecurityService.CanModifyEnvironment(User, request.EnvironmentName))
                {
                    return StatusCode(StatusCodes.Status403Forbidden,
                        $"Forbidden: caller cannot modify environment '{request.EnvironmentName}'.");
                }

                // Server-side parameter validation against the manifest.
                var supplied = (request.Parameters ?? new Dictionary<string, string>())
                    .ToDictionary(kv => kv.Key, kv => (string?)kv.Value);
                var validation = _parameterValidator.Validate(manifest, supplied);
                if (!validation.IsValid)
                {
                    var firstError = validation.Errors[0];
                    return BadRequest(
                        $"Parameter '{firstError.ParameterName}' invalid: {firstError.Message}");
                }

                // Compose RequestDto. BuildUrl is set to the catalog sentinel
                // directly because we already know this single component is
                // Catalog-mode (we just created it). RequestProperties carry
                // each manifest parameter's value with IsSensitive sourced
                // from the manifest's Sensitive flag so the closed
                //  redaction surface covers them.
                var requestProperties = manifest.Parameters
                    .Where(p => supplied.TryGetValue(p.Name, out var v) && !string.IsNullOrEmpty(v))
                    .Select(p => new RequestProperty
                    {
                        PropertyName = p.Name,
                        PropertyValue = supplied[p.Name],
                        IsSensitive = p.Sensitive,
                    })
                    .ToList();
                var requestDto = new RequestDto
                {
                    Project = project.ProjectName,
                    Environment = request.EnvironmentName,
                    BuildUrl = BuildDetails.CatalogSentinel,
                    BuildText = string.Empty,
                    BuildNum = string.Empty,
                    Components = new List<string> { componentName },
                    RequestProperties = requestProperties,
                };

                try
                {
                    var status = _requestService.CreateRequest(requestDto, User);
                    if (status == null || status.Id <= 0)
                    {
                        _log.LogError(
                            "Catalog instantiate-and-deploy: deploy request submission returned no id for component '{Component}' in env '{Env}'.",
                            safeComponentName, SanitizeForLog(request.EnvironmentName));
                        return StatusCode(StatusCodes.Status500InternalServerError,
                            "Component was created, but the deploy request failed to submit. See server logs.");
                    }
                    return Ok(new { component, requestId = status.Id, requestStatus = status.Status });
                }
                catch (Exception ex) when (ex is InvalidOperationException
                                          || ex is WrongBuildTypeException
                                          || ex is ArgumentException)
                {
                    _log.LogError(ex,
                        "Catalog instantiate-and-deploy: deploy request submission threw for component '{Component}' in env '{Env}'.",
                        safeComponentName, SanitizeForLog(request.EnvironmentName));
                    return StatusCode(StatusCodes.Status500InternalServerError,
                        "Component was created, but the deploy request failed to submit. See server logs.");
                }
            }

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
            catch (FileNotFoundException)
            {
                // No plan-content blob for this result (plan phase never ran,
                // failed early, or a legacy result) is a "not found", not a
                // server error.
                _log.LogInformation("No Terraform plan content found for deployment result ID {DeploymentResultId}.", deploymentResultId);
                return NotFound($"No Terraform plan content found for deployment result {deploymentResultId}.");
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