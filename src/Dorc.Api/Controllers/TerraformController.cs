using Dorc.Api.Interfaces;
using Dorc.Api.Model;
using Dorc.Api.Services;
using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core.AzureStorageAccount;
using Dorc.Core;
using Dorc.Core.Configuration;
using Dorc.Core.VariableResolution;
using Dorc.Core.Interfaces;
using Dorc.Core.Security;
using Dorc.PersistentData;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources;
using Dorc.PersistentData.Sources.Interfaces;
using Dorc.Terraform.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using System.Globalization;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Text;

namespace Dorc.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class TerraformController : ControllerBase
    {
        private const string NoEnvironmentAuthorityMessage =
            "You do not have permission to act on Terraform plans for this environment.";

        private const string SeparateApproverRequiredMessage =
            "This deployment was requested by you. A different approver is required for this environment.";

        private const string CannotEstablishAuthorityMessage =
            "Unable to establish which environment this Terraform plan belongs to.";

        private readonly ILogger _log;
        private readonly IRequestsPersistentSource _requestsPersistentSource;
        private readonly ISecurityPrivilegesChecker _apiSecurityService;
        private readonly IClaimsPrincipalReader _claimsPrincipalReader;
        private readonly IAzureStorageAccountWorker _azureStorageAccountWorker;
        private readonly ITemplateCatalog _templateCatalog;
        private readonly IProjectsPersistentSource _projectsPersistentSource;
        private readonly IEnvironmentsPersistentSource _environmentsPersistentSource;
        private readonly IManageProjectsPersistentSource _manageProjectsPersistentSource;
        private readonly IParameterValidator _parameterValidator;
        private readonly IRequestService _requestService;
        private readonly IPropertyValuesPersistentSource _propertyValuesPersistentSource;
        private readonly IVariableResolver _variableResolver;
        private readonly IVariableScopeOptionsResolver _variableScopeOptionsResolver;
        private readonly IConfigurationSettings _configurationSettings;

        public TerraformController(
            ILogger<TerraformController> log,
            IRequestsPersistentSource requestsPersistentSource,
            ISecurityPrivilegesChecker apiSecurityService,
            IClaimsPrincipalReader claimsPrincipalReader,
            IAzureStorageAccountWorker azureStorageAccountWorker,
            ITemplateCatalog templateCatalog,
            IProjectsPersistentSource projectsPersistentSource,
            IEnvironmentsPersistentSource environmentsPersistentSource,
            IManageProjectsPersistentSource manageProjectsPersistentSource,
            IParameterValidator parameterValidator,
            IRequestService requestService,
            IPropertyValuesPersistentSource propertyValuesPersistentSource,
            [FromKeyedServices("VariableResolver")] IVariableResolver variableResolver,
            IVariableScopeOptionsResolver variableScopeOptionsResolver,
            IConfigurationSettings configurationSettings)
        {
            _log = log;
            _requestsPersistentSource = requestsPersistentSource;
            _apiSecurityService = apiSecurityService;
            _claimsPrincipalReader = claimsPrincipalReader;
            _azureStorageAccountWorker = azureStorageAccountWorker;
            _configurationSettings = configurationSettings;
            _templateCatalog = templateCatalog;
            _projectsPersistentSource = projectsPersistentSource;
            _environmentsPersistentSource = environmentsPersistentSource;
            _manageProjectsPersistentSource = manageProjectsPersistentSource;
            _parameterValidator = parameterValidator;
            _requestService = requestService;
            _propertyValuesPersistentSource = propertyValuesPersistentSource;
            _variableResolver = variableResolver;
            _variableScopeOptionsResolver = variableScopeOptionsResolver;
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
        /// Reports where each input of a stock template would be resolved from
        /// for a project + environment: an environment property, the manifest
        /// default, or nothing at all. Names and statuses only; no property
        /// value ever leaves the server. This is what lets the wizard mark an
        /// inherited required input as missing before the user submits.
        /// </summary>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(TerraformTemplateResolutionApiModel))]
        [SwaggerResponse(StatusCodes.Status400BadRequest)]
        [SwaggerResponse(StatusCodes.Status403Forbidden)]
        [SwaggerResponse(StatusCodes.Status404NotFound)]
        [HttpGet("templates/{name}/{version}/resolution")]
        public async Task<IActionResult> ResolveTemplateInputs(
            string name,
            string version,
            [FromQuery] int projectId,
            [FromQuery] string? environmentName,
            CancellationToken cancellationToken)
        {
            if (projectId <= 0)
            {
                return BadRequest("projectId is required and must be a positive integer.");
            }
            if (string.IsNullOrWhiteSpace(environmentName))
            {
                return BadRequest("environmentName is required.");
            }

            var manifest = await _templateCatalog.GetAsync(name, version, cancellationToken);
            if (manifest is null)
            {
                return NotFound($"Stock template '{name}@{version}' was not found in the catalog.");
            }

            var project = FindProject(projectId);
            if (project is null)
            {
                return NotFound($"Project with id {projectId} was not found.");
            }
            if (!_apiSecurityService.IsProjectOwnerOrAdmin(User, project.ProjectName))
            {
                return Forbid();
            }

            var environment = _environmentsPersistentSource.GetEnvironment(environmentName.Trim());
            if (environment is null)
            {
                return BadRequest($"Environment '{environmentName}' was not found.");
            }
            if (!IsProjectMappedTo(project, environment))
            {
                return BadRequest(
                    $"Project '{project.ProjectName}' is not mapped to environment '{environment.EnvironmentName}'.");
            }
            // Same gate as the deploy half of InstantiateTemplate: knowing which
            // property names an environment defines is only useful to someone
            // who can deploy into it.
            if (!_apiSecurityService.CanModifyEnvironment(User, environment.EnvironmentName))
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    $"Forbidden: caller cannot modify environment '{environment.EnvironmentName}'.");
            }

            var resolved = LoadEnvironmentProperties(manifest, new Dictionary<string, string?>(), environment);
            var parameters = manifest.Parameters
                .Select(p => new TerraformParameterResolutionApiModel
                {
                    Name = p.Name,
                    Required = p.Required,
                    Sensitive = p.Sensitive,
                    Status = resolved.TryGetValue(p.Name, out var value)
                             && Convert.ToString(value?.Value, CultureInfo.InvariantCulture) is not null
                        ? TerraformParameterResolutionStatus.Environment
                        : p.Default is not null
                            ? TerraformParameterResolutionStatus.Default
                            : p.Required
                                ? TerraformParameterResolutionStatus.Missing
                                : TerraformParameterResolutionStatus.Unset
                })
                .ToList();

            return Ok(new TerraformTemplateResolutionApiModel
            {
                ProjectName = project.ProjectName,
                EnvironmentName = environment.EnvironmentName,
                Parameters = parameters
            });
        }

        /// <summary>
        /// Instantiates a stock template as a new Catalog-mode component in
        /// the destination project. When an environment is supplied, the
        /// endpoint also validates the effective manifest inputs and submits
        /// a deploy request through the existing DOrc flow.
        ///
        /// This endpoint is the "Deploy from template" entry point used by
        /// the Stock Modules page in the dorc-web UI. In create-only mode
        /// it only persists a new ComponentApiModel pre-wired with
        /// TerraformSourceType=Catalog and the chosen
        /// (TerraformTemplateName, TerraformTemplateVersion).
        /// </summary>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(TerraformTemplateInstantiateResponseApiModel))]
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

            // ProjectsPersistentSource.GetProject(int) resolves via
            // Single(), which throws InvalidOperationException for an
            // unknown id instead of returning null (despite the nullable
            // return type). Translate that into the declared 404 rather
            // than letting it surface as an opaque 500.
            var project = FindProject(request.ProjectId);
            if (project is null)
            {
                return NotFound($"Project with id {request.ProjectId} was not found.");
            }

            if (!_apiSecurityService.IsProjectOwnerOrAdmin(User, project.ProjectName))
            {
                return Forbid();
            }

            var deployEnvironment = string.IsNullOrWhiteSpace(request.EnvironmentName)
                ? null
                : _environmentsPersistentSource.GetEnvironment(request.EnvironmentName.Trim());
            if (!string.IsNullOrWhiteSpace(request.EnvironmentName) && deployEnvironment is null)
            {
                return BadRequest($"Environment '{request.EnvironmentName}' was not found.");
            }

            if (deployEnvironment is not null && !IsProjectMappedTo(project, deployEnvironment))
            {
                return BadRequest(
                    $"Project '{project.ProjectName}' is not mapped to environment '{deployEnvironment.EnvironmentName}'.");
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

            var projectComponents = _projectsPersistentSource.GetComponentsForProject(project.ProjectName).ToList();

            if (deployEnvironment is not null)
            {
                // The deploy request is only legal for callers who can
                // modify the target environment. Validate that before we
                // create or reuse any component so invalid requests leave no
                // persisted side effects behind.
                if (!_apiSecurityService.CanModifyEnvironment(User, deployEnvironment.EnvironmentName))
                {
                    return StatusCode(StatusCodes.Status403Forbidden,
                        $"Forbidden: caller cannot modify environment '{deployEnvironment.EnvironmentName}'.");
                }

                // Resolve only manifest-declared inputs. Explicit request
                // overrides win; otherwise we inherit the environment-scoped
                // value already configured in DOrc. Manifest defaults are used
                // only when runtime would rely on them.
                var supplied = (request.Parameters ?? new Dictionary<string, string>())
                    .ToDictionary(kv => kv.Key, kv => (string?)kv.Value);
                var validationInputs = BuildValidationInputs(manifest, supplied, deployEnvironment);
                var validation = _parameterValidator.Validate(manifest, validationInputs);
                if (!validation.IsValid)
                {
                    var firstError = validation.Errors[0];
                    if (!supplied.ContainsKey(firstError.ParameterName))
                    {
                        return BadRequest(
                            $"Parameter '{firstError.ParameterName}' is missing or invalid in the environment/module defaults. Configure the environment property or supply a valid request override.");
                    }
                    return BadRequest(
                        $"Parameter '{firstError.ParameterName}' invalid: {firstError.Message}");
                }
            }

            // A duplicate inside the destination project passes the
            // cross-project validation above but would still trigger the
            // legacy rename-the-existing-component behaviour: reject it
            // explicitly instead.
            //
            // One exception, for retryability: a previous create-and-deploy
            // call may have persisted the component and then failed to submit
            // the deploy request (we deliberately leave the component in
            // place — see the comment above the deploy block). When the
            // caller retries the same create-and-deploy request and the
            // existing component is an identical Catalog-mode instantiation
            // of this template version, reuse it and proceed to the deploy
            // instead of trapping the caller behind a 409.
            var existingComponent = projectComponents.FirstOrDefault(c =>
                string.Equals(c.ComponentName, componentName, StringComparison.OrdinalIgnoreCase));
            if (existingComponent is not null)
            {
                var isIdenticalCatalogComponent =
                    existingComponent.ComponentType == ComponentType.Terraform
                    && existingComponent.TerraformSourceType == TerraformSourceType.Catalog
                    && string.Equals(existingComponent.TerraformTemplateName, manifest.Name, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(existingComponent.TerraformTemplateVersion, manifest.Version, StringComparison.OrdinalIgnoreCase);
                if (request.ParentComponentId.HasValue
                    && existingComponent.ParentId != request.ParentComponentId.Value)
                {
                    return Conflict(
                        $"A component named '{componentName}' already exists in project '{project.ProjectName}' under a different parent. Choose a different component name or retry with the original parent.");
                }
                if (deployEnvironment is null || !isIdenticalCatalogComponent)
                {
                    return Conflict(
                        $"A component named '{componentName}' already exists in project '{project.ProjectName}'. Choose a different component name.");
                }
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

            if (existingComponent is not null)
            {
                // Retry path: the identical Catalog-mode component already
                // exists from a previous partially-failed create-and-deploy
                // call; reuse it instead of creating a second one.
                component = existingComponent;
                _log.LogInformation(
                    "Stock template '{Manifest}' retry: reusing existing component '{Component}' in project '{ProjectName}' (id {ProjectId}) by {UserId}.",
                    $"{manifest.Name}@{manifest.Version}", safeComponentName, project.ProjectName, request.ProjectId, safeUserId);
            }
            else
            {
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
            }

            // create-and-deploy mode. When the wizard supplies an
            // environment + parameter values, compose a Catalog-mode
            // RequestDto and submit it through the existing
            // IRequestService.CreateRequest path. Parameter validation and
            // env-modify permission checks already ran before any create
            // path could persist a component, so invalid requests cannot
            // leave side effects behind. If the deploy submission fails, the
            // component stays persisted so the caller can retry.
            if (deployEnvironment is not null)
            {
                var supplied = (request.Parameters ?? new Dictionary<string, string>())
                    .ToDictionary(kv => kv.Key, kv => (string?)kv.Value);

                // Compose RequestDto. BuildUrl is set to the catalog sentinel
                // directly because we already know this single component is
                // Catalog-mode (we just created it, or verified the reused
                // one is an identical Catalog instantiation). RequestProperties
                // carry only explicit user overrides; inherited environment
                // values stay in the environment and are not duplicated or
                // displayed back to the caller.
                var requestProperties = manifest.Parameters
                    .Where(p => supplied.ContainsKey(p.Name))
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
                    Environment = deployEnvironment.EnvironmentName,
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
                            safeComponentName, SanitizeForLog(deployEnvironment.EnvironmentName));
                        return StatusCode(StatusCodes.Status500InternalServerError,
                            "Component was created, but the deploy request failed to submit. See server logs.");
                    }
                    return Ok(new TerraformTemplateInstantiateResponseApiModel
                    {
                        Component = component,
                        RequestId = status.Id,
                        RequestStatus = status.Status
                    });
                }
                catch (Exception ex) when (ex is InvalidOperationException
                                          || ex is WrongBuildTypeException
                                          || ex is ArgumentException)
                {
                    _log.LogError(ex,
                        "Catalog instantiate-and-deploy: deploy request submission threw for component '{Component}' in env '{Env}'.",
                        safeComponentName, SanitizeForLog(deployEnvironment.EnvironmentName));
                    return StatusCode(StatusCodes.Status500InternalServerError,
                        "Component was created, but the deploy request failed to submit. See server logs.");
                }
            }

            // create-only mode: no deploy request was submitted, so RequestId
            // / RequestStatus are left at their defaults. The endpoint
            // returns the same envelope type in both modes so its declared
            // 200 shape is honest and self-consistent.
            return Ok(new TerraformTemplateInstantiateResponseApiModel { Component = component });
        }

        /// <summary>
        /// GetProject resolves via Single() on a fresh context, so an unknown id
        /// surfaces as InvalidOperationException rather than null. Translate it
        /// into null so callers can return their declared 404; the warning keeps
        /// the rare non-not-found case (a Single() invariant breach) visible.
        /// </summary>
        private ProjectApiModel? FindProject(int projectId)
        {
            try
            {
                return _projectsPersistentSource.GetProject(projectId);
            }
            catch (InvalidOperationException ex)
            {
                _log.LogWarning(ex,
                    "GetProject({ProjectId}) threw; treating as project-not-found.",
                    projectId);
                return null;
            }
        }

        private bool IsProjectMappedTo(ProjectApiModel project, EnvironmentApiModel environment)
        {
            return _environmentsPersistentSource.GetMappedProjects(environment.EnvironmentName)
                .Select(p => p.ProjectName)
                .Contains(project.ProjectName, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Resolves the environment-scoped property set the Monitor would see for
        /// this environment, with any request overrides seeded first so derived
        /// properties observe them. Values stay inside the API process.
        /// </summary>
        private IDictionary<string, VariableValue> LoadEnvironmentProperties(
            TerraformTemplateManifest manifest,
            IDictionary<string, string?> supplied,
            EnvironmentApiModel environment)
        {
            _propertyValuesPersistentSource.AddFilter(PropertyValueFilterTypes.EnvironmentPropertyFilterType, environment.EnvironmentName);
            _variableResolver.SetPropertyValue(PropertyValueScopeOptionsFixed.EnvironmentName, environment.EnvironmentName);
            _variableScopeOptionsResolver.SetPropertyValues(_variableResolver, environment);

            // Derived environment properties must see the same overrides as
            // the Monitor, which seeds request properties before resolution.
            foreach (var parameter in manifest.Parameters.Where(p => supplied.ContainsKey(p.Name)))
            {
                _variableResolver.SetPropertyValue(parameter.Name, supplied[parameter.Name] ?? string.Empty);
            }
            return _variableResolver.LoadProperties();
        }

        private Dictionary<string, string?> BuildValidationInputs(
            TerraformTemplateManifest manifest,
            IDictionary<string, string?> supplied,
            EnvironmentApiModel environment)
        {
            var resolvedProperties = LoadEnvironmentProperties(manifest, supplied, environment);
            var validationInputs = new Dictionary<string, string?>(supplied, StringComparer.Ordinal);

            foreach (var parameter in manifest.Parameters)
            {
                if (validationInputs.ContainsKey(parameter.Name))
                {
                    continue;
                }

                if (resolvedProperties.TryGetValue(parameter.Name, out var resolvedValue))
                {
                    var value = Convert.ToString(resolvedValue?.Value, CultureInfo.InvariantCulture);
                    if (value is not null)
                    {
                        validationInputs[parameter.Name] = value;
                        continue;
                    }
                }

                if (parameter.Default is not null)
                {
                    validationInputs[parameter.Name] = parameter.Default;
                }
            }

            return validationInputs;
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
        [SwaggerResponse(StatusCodes.Status403Forbidden)]
        [SwaggerResponse(StatusCodes.Status404NotFound)]
        [HttpGet("plan/{deploymentResultId}")]
        public IActionResult GetTerraformPlan(int deploymentResultId)
        {
            try
            {
                _log.LogInformation($"Getting Terraform plan for deployment result ID: {deploymentResultId}");

                var authorization = AuthorizeForEnvironment(deploymentResultId);
                if (!authorization.Succeeded)
                {
                    return authorization.Failure!;
                }

                var deploymentResult = authorization.Result!;

                // Plan content is only loaded once authority is established. Ordering
                // matters: the plan embeds resolved variable values, so a refused caller
                // must not cause it to be read at all.
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
        [SwaggerResponse(StatusCodes.Status403Forbidden)]
        [SwaggerResponse(StatusCodes.Status404NotFound)]
        [SwaggerResponse(StatusCodes.Status409Conflict)]
        [HttpPost("plan/{deploymentResultId}/confirm")]
        public IActionResult ConfirmTerraformPlan(int deploymentResultId)
        {
            try
            {
                _log.LogInformation($"Confirming Terraform plan for deployment result ID: {deploymentResultId}");

                var authorization = AuthorizeForEnvironment(deploymentResultId);
                if (!authorization.Succeeded)
                {
                    return authorization.Failure!;
                }

                var deploymentResult = authorization.Result!;
                var deploymentRequest = authorization.Request!;

                // Confirming is the only action gated by segregation of duties. Declining is
                // not - see DeclineTerraformPlan.
                var separateApproverFailure = RequireSeparateApprover(deploymentRequest);
                if (separateApproverFailure != null)
                {
                    return separateApproverFailure;
                }

                if (deploymentResult.Status != DeploymentResultStatus.WaitingConfirmation.ToString())
                {
                    return BadRequest($"Deployment result {deploymentResultId} is not in WaitingConfirmation status. Current status: {deploymentResult.Status}");
                }

                // Guarded transition. The status was read before the authorization checks, so
                // it may have moved since - a confirm racing the Monitor would otherwise push
                // a running result back to Confirmed and cause a second apply dispatch.
                var transitioned = _requestsPersistentSource.UpdateResultStatus(
                    deploymentResult,
                    DeploymentResultStatus.Confirmed,
                    DeploymentResultStatus.WaitingConfirmation);

                if (!transitioned)
                {
                    _log.LogWarning($"Terraform plan confirmation for deployment result ID {deploymentResultId} lost a race; the result is no longer awaiting confirmation.");
                    return Conflict($"Deployment result {deploymentResultId} is no longer awaiting confirmation. It may have been confirmed, declined, or started already.");
                }

                _requestsPersistentSource.UpdateRequestStatus(
                    deploymentResult.RequestId,
                    DeploymentRequestStatus.Confirmed);

                var userName = _claimsPrincipalReader.GetUserName(User);
                _log.LogInformation($"Terraform plan confirmed for deployment result ID: {deploymentResultId} by user: {userName}");

                // Note: The actual execution will be handled by the Monitor service when it picks up the Confirmed status

                return Ok(new
                {
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
        [SwaggerResponse(StatusCodes.Status403Forbidden)]
        [SwaggerResponse(StatusCodes.Status404NotFound)]
        [HttpPost("plan/{deploymentResultId}/decline")]
        public IActionResult DeclineTerraformPlan(int deploymentResultId)
        {
            try
            {
                _log.LogInformation($"Declining Terraform plan for deployment result ID: {deploymentResultId}");

                // Environment authority only. Segregation of duties deliberately does NOT
                // apply here: declining stops a deployment rather than starting one, so
                // there is no self-approval to prevent, and applying it would stop a
                // requester cancelling their own pending plan.
                var authorization = AuthorizeForEnvironment(deploymentResultId);
                if (!authorization.Succeeded)
                {
                    return authorization.Failure!;
                }

                var deploymentResult = authorization.Result!;

                if (deploymentResult.Status != DeploymentResultStatus.WaitingConfirmation.ToString())
                {
                    return BadRequest($"Deployment result {deploymentResultId} is not in WaitingConfirmation status. Current status: {deploymentResult.Status}");
                }

                var transitioned = _requestsPersistentSource.UpdateResultStatus(
                    deploymentResult,
                    DeploymentResultStatus.Cancelled,
                    DeploymentResultStatus.WaitingConfirmation);

                if (!transitioned)
                {
                    _log.LogWarning($"Terraform plan decline for deployment result ID {deploymentResultId} lost a race; the result is no longer awaiting confirmation.");
                    return Conflict($"Deployment result {deploymentResultId} is no longer awaiting confirmation. It may have been confirmed, declined, or started already.");
                }

                _requestsPersistentSource.UpdateRequestStatus(
                    deploymentResult.RequestId,
                    DeploymentRequestStatus.Cancelled);

                var userName = _claimsPrincipalReader.GetUserName(User);
                _log.LogInformation($"Terraform plan declined for deployment result ID: {deploymentResultId} by user: {userName}");

                return Ok(new
                {
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

        /// <summary>
        /// Resolves the deployment result and its request, and checks that the caller holds
        /// modify authority over the request's environment.
        ///
        /// A deployment result does not carry an environment, so the environment must be
        /// reached through the result's request. Every failure in that chain refuses:
        /// CanModifyEnvironment falls back to an administrator check when the environment
        /// cannot be found, so an absent or blank environment name would otherwise permit
        /// every administrator rather than refuse.
        /// </summary>
        private AuthorizationOutcome AuthorizeForEnvironment(int deploymentResultId)
        {
            DeploymentResultApiModel? deploymentResult;
            DeploymentRequestApiModel? deploymentRequest;

            try
            {
                deploymentResult = _requestsPersistentSource.GetDeploymentResults(deploymentResultId);

                if (deploymentResult == null)
                {
                    return AuthorizationOutcome.Refused(
                        NotFound($"Deployment result with ID {deploymentResultId} not found"));
                }

                deploymentRequest = _requestsPersistentSource.GetRequest(deploymentResult.RequestId);
            }
            catch (Exception ex)
            {
                // Converted to a refusal rather than allowed to reach the action's blanket
                // handler, which would return 500. An authorization decision that cannot be
                // made is a refusal, not a fault to be reported as success-adjacent.
                _log.LogError(ex, $"Failed to resolve the environment for deployment result ID {deploymentResultId}; refusing.");
                return AuthorizationOutcome.Refused(
                    StatusCode(StatusCodes.Status403Forbidden, CannotEstablishAuthorityMessage));
            }

            if (deploymentRequest == null)
            {
                _log.LogWarning($"Deployment result {deploymentResultId} references request {deploymentResult.RequestId}, which could not be resolved; refusing.");
                return AuthorizationOutcome.Refused(
                    StatusCode(StatusCodes.Status403Forbidden, CannotEstablishAuthorityMessage));
            }

            if (string.IsNullOrWhiteSpace(deploymentRequest.EnvironmentName))
            {
                _log.LogWarning($"Request {deploymentRequest.Id} has no environment name; refusing rather than deferring to the privileges checker, which would fall back to an administrator check.");
                return AuthorizationOutcome.Refused(
                    StatusCode(StatusCodes.Status403Forbidden, CannotEstablishAuthorityMessage));
            }

            if (!_apiSecurityService.CanModifyEnvironment(User, deploymentRequest.EnvironmentName))
            {
                var userIdentifier = CreateLogIdentifier(
                    _claimsPrincipalReader.GetUserSafeIdentifier(User));
                _log.LogInformation(
                    "Forbidden Terraform plan access to deployment result {DeploymentResultId} from user {UserIdentifier}",
                    deploymentResultId,
                    userIdentifier);
                return AuthorizationOutcome.Refused(
                    StatusCode(StatusCodes.Status403Forbidden, NoEnvironmentAuthorityMessage));
            }

            return AuthorizationOutcome.Allowed(deploymentResult, deploymentRequest);
        }

        /// <summary>
        /// Refuses when the caller submitted the request they are confirming, where the
        /// environment's tier requires a separate approver. Returns null when the action
        /// may proceed.
        /// </summary>
        private IActionResult? RequireSeparateApprover(DeploymentRequestApiModel deploymentRequest)
        {
            // Absent or unparseable configuration means "required for production-tier".
            var configured = _configurationSettings.GetTerraformSeparateApproverRequired();
            var required = configured ?? deploymentRequest.IsProd;

            if (!required)
            {
                return null;
            }

            var submitter = GetOriginalSubmitter(deploymentRequest);
            var approverFullDomainName = _claimsPrincipalReader.GetUserFullDomainName(User);
            var approverLogin = _claimsPrincipalReader.GetUserLogin(User);

            // Every comparison input must be established. Permitting because one identifier
            // is unavailable could miss the representation that matches the stored submitter.
            if (!UserIdentityCanonicaliser.CanEstablish(submitter) ||
                !UserIdentityCanonicaliser.CanEstablish(approverFullDomainName) ||
                !UserIdentityCanonicaliser.CanEstablish(approverLogin))
            {
                _log.LogWarning($"Cannot establish submitter or approver identity for request {deploymentRequest.Id}; refusing confirmation.");
                return StatusCode(StatusCodes.Status403Forbidden, SeparateApproverRequiredMessage);
            }

            // OAuth's full-domain identity is normally an email address, whose local part
            // need not equal the user's SAM account name. Compare both representations so
            // DOMAIN\jsmith cannot approve as john.smith@corp when their login is jsmith.
            if (UserIdentityCanonicaliser.SamePrincipal(submitter, approverFullDomainName) ||
                UserIdentityCanonicaliser.SamePrincipal(submitter, approverLogin))
            {
                var approverIdentifier = CreateLogIdentifier(
                    _claimsPrincipalReader.GetUserSafeIdentifier(User));
                _log.LogInformation(
                    "Refused self-approval of request {RequestId} by {ApproverIdentifier}.",
                    deploymentRequest.Id,
                    approverIdentifier);
                return StatusCode(StatusCodes.Status403Forbidden, SeparateApproverRequiredMessage);
            }

            return null;
        }

        private static string CreateLogIdentifier(string? safeIdentifier)
        {
            if (string.IsNullOrWhiteSpace(safeIdentifier))
            {
                return "unavailable";
            }

            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(safeIdentifier));
            return Convert.ToHexString(hash.AsSpan(0, 8));
        }

        /// <summary>
        /// The identity that originally submitted the request.
        ///
        /// The request's own user name is not that identity: the restart endpoint overwrites
        /// it with the restarter's name. Restart archives the current attempt before doing
        /// so, and the archive copies the then-current user name, so the earliest attempt
        /// holds the original submitter. A request that has never been restarted has no
        /// attempts, and its user name has not been overwritten.
        /// </summary>
        private string? GetOriginalSubmitter(DeploymentRequestApiModel deploymentRequest)
        {
            var attempts = _requestsPersistentSource.GetAttemptsForRequest(deploymentRequest.Id);
            var earliestAttempt = attempts?.OrderBy(attempt => attempt.AttemptNumber).FirstOrDefault();

            return earliestAttempt != null
                ? earliestAttempt.UserName
                : deploymentRequest.UserName;
        }

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

        private sealed class AuthorizationOutcome
        {
            private AuthorizationOutcome() { }

            public IActionResult? Failure { get; private init; }
            public DeploymentResultApiModel? Result { get; private init; }
            public DeploymentRequestApiModel? Request { get; private init; }

            public bool Succeeded => Failure == null;

            public static AuthorizationOutcome Refused(IActionResult failure) =>
                new() { Failure = failure };

            public static AuthorizationOutcome Allowed(
                DeploymentResultApiModel result,
                DeploymentRequestApiModel request) =>
                new() { Result = result, Request = request };
        }
    }
}
