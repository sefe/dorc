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
    public partial class TerraformController : ControllerBase
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
        private readonly IConfigValuesPersistentSource _configValuesPersistentSource;

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
            IConfigurationSettings configurationSettings,
            IConfigValuesPersistentSource configValuesPersistentSource)
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
            _configValuesPersistentSource = configValuesPersistentSource;
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
            CancellationToken cancellationToken,
            [FromQuery] int? componentId = null)
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

            CatalogEnvironmentInputs? configuration = null;
            if (componentId.HasValue)
            {
                var selected = _projectsPersistentSource.GetComponentsForProject(projectId)
                    .SingleOrDefault(component => component.ComponentId == componentId);
                if (selected?.TerraformSourceType != TerraformSourceType.Catalog ||
                    selected.TerraformTemplateName != manifest.Name || selected.TerraformTemplateVersion != manifest.Version)
                    return Conflict("Select a component pinned to this template version.");
                configuration = _requestsPersistentSource.GetCatalogEnvironmentInputs(componentId.Value, environment.EnvironmentId);
                if (configuration != null && configuration.TemplateVersion != manifest.Version)
                    return Conflict("Saved inputs target a different module version. Review and replace the saved configuration.");
            }
            var savedInputs = ReadSavedInputs(configuration);
            var resolved = LoadEnvironmentProperties(manifest, new Dictionary<string, string?>(), environment);
            foreach (var input in savedInputs)
                resolved[input.Key] = new VariableValue { Value = input.Value, Type = typeof(string) };
            var secureInputNames = _configValuesPersistentSource.GetAllConfigValues(true)
                .Where(value => value.Secure).Select(value => value.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var parameters = manifest.Parameters
                .Select(p => new TerraformParameterResolutionApiModel
                {
                    Name = p.Name,
                    Required = p.Required,
                    Sensitive = p.Sensitive || _propertyValuesPersistentSource.IsCachedPropertySecure(p.Name) || secureInputNames.Contains(p.Name),
                    Status = savedInputs.ContainsKey(p.Name)
                        ? TerraformParameterResolutionStatus.SavedConfiguration
                        : resolved.TryGetValue(p.Name, out var value)
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
                Parameters = parameters,
                Readiness = DescribeReadiness(manifest, resolved),
                SavedConfiguration = new CatalogEnvironmentInputsApiModel
                {
                    Revision = configuration?.Revision ?? 0,
                    ParameterNames = savedInputs.Keys.ToList()
                }
            });
        }

        private static Dictionary<string, string> ReadSavedInputs(CatalogEnvironmentInputs? configuration) =>
            System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(configuration?.InputsJson ?? "{}")!;

        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(CatalogEnvironmentInputsApiModel))]
        [SwaggerResponse(StatusCodes.Status400BadRequest)]
        [SwaggerResponse(StatusCodes.Status403Forbidden)]
        [SwaggerResponse(StatusCodes.Status409Conflict)]
        [HttpPut("templates/{name}/{version}/configuration")]
        public async Task<IActionResult> SaveEnvironmentInputs(string name, string version,
            [FromQuery] int projectId, [FromQuery] int componentId, [FromQuery] string environmentName,
            [FromBody] SaveCatalogEnvironmentInputsApiModel input, CancellationToken cancellationToken)
        {
            var project = FindProject(projectId);
            if (project == null) return NotFound();
            if (!_apiSecurityService.IsProjectOwnerOrAdmin(User, project.ProjectName)) return Forbid();
            var environment = _environmentsPersistentSource.GetEnvironment(environmentName);
            if (environment == null || !IsProjectMappedTo(project, environment)) return BadRequest("The environment is not mapped to this project.");
            if (!_apiSecurityService.CanModifyEnvironment(User, environment.EnvironmentName)) return Forbid();
            var component = _projectsPersistentSource.GetComponentsForProject(projectId)
                .SingleOrDefault(item => item.ComponentId == componentId);
            if (component?.TerraformSourceType != TerraformSourceType.Catalog ||
                component.TerraformTemplateName != name || component.TerraformTemplateVersion != version)
                return Conflict("The component pin changed. Reload before saving.");
            var manifest = await _templateCatalog.GetAsync(name, version, cancellationToken);
            if (manifest == null) return NotFound();
            if (input?.Inputs == null || input.ExpectedRevision < 0) return BadRequest("Inputs and a nonnegative expected revision are required.");
            if (input.Inputs.Keys.Any(key => !manifest.Parameters.Any(parameter =>
                    parameter.Name == key && !parameter.Sensitive)))
                return BadRequest("Only declared non-sensitive module inputs can be saved. Secrets must remain secure properties or one-off overrides.");
            // Save validates only the supplied scalar values; missing values remain inherited.
            var partialManifest = manifest with
            {
                Parameters = manifest.Parameters.Where(parameter => input.Inputs.ContainsKey(parameter.Name)).ToArray(),
                ParameterConflicts = null
            };
            var validation = _parameterValidator.Validate(partialManifest,
                input.Inputs.ToDictionary(pair => pair.Key, pair => (string?)pair.Value));
            if (!validation.IsValid)
                return BadRequest("Saved inputs are invalid: " + string.Join(", ", validation.Errors.Select(error => error.ParameterName)));
            try
            {
                var revision = _requestsPersistentSource.SaveCatalogEnvironmentInputs(componentId, projectId,
                    environment.EnvironmentId, version, input.ExpectedRevision, input.Inputs,
                    _claimsPrincipalReader.GetUserFullDomainName(User));
                return Ok(new CatalogEnvironmentInputsApiModel { Revision = revision, ParameterNames = input.Inputs.Keys.ToList() });
            }
            catch (InvalidOperationException ex) { return Conflict(ex.Message); }
        }

        [HttpGet("components/{componentId}/upgrade/{name}/{version}")]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(CatalogUpgradePreviewApiModel))]
        [SwaggerResponse(StatusCodes.Status403Forbidden)]
        [SwaggerResponse(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> PreviewUpgrade(int componentId, string name, string version,
            [FromQuery] int projectId, CancellationToken cancellationToken)
        {
            var project = FindProject(projectId);
            if (project == null) return NotFound();
            if (!_apiSecurityService.IsProjectOwnerOrAdmin(User, project.ProjectName)) return Forbid();
            var component = _projectsPersistentSource.GetComponentsForProject(projectId)
                .SingleOrDefault(item => item.ComponentId == componentId);
            if (component?.TerraformSourceType != TerraformSourceType.Catalog || component.TerraformTemplateName != name)
                return Conflict("Select an existing component of the same module.");
            CatalogUpgradePreviewApiModel scope;
            try { scope = _requestsPersistentSource.GetCatalogUpgradeScope(componentId, projectId); }
            catch (InvalidOperationException ex) { return Conflict(ex.Message); }
            if (scope.Environments.Any(environment => !_apiSecurityService.CanModifyEnvironment(User, environment.EnvironmentName)))
                return Forbid();
            var oldManifest = await _templateCatalog.GetAsync(name, scope.CurrentVersion, cancellationToken);
            var newManifest = await _templateCatalog.GetAsync(name, version, cancellationToken);
            if (oldManifest == null || newManifest == null) return NotFound();
            if (!Version.TryParse(scope.CurrentVersion.TrimStart('v'), out var oldVersion) ||
                !Version.TryParse(version.TrimStart('v'), out var newVersion) || newVersion <= oldVersion)
                return BadRequest("Choose a newer stable version of this module.");
            scope.TargetVersion = version;
            foreach (var parameter in newManifest.Parameters)
            {
                var previous = oldManifest.Parameters.SingleOrDefault(item => item.Name == parameter.Name);
                if (previous == null)
                    scope.InputChanges.Add($"Added {parameter.Name}: {(parameter.Required ? "required; configure before the next plan" : "optional")}. Inherited values are not checked by upgrade.");
                else if (System.Text.Json.JsonSerializer.Serialize(previous) != System.Text.Json.JsonSerializer.Serialize(parameter))
                    scope.InputChanges.Add($"Changed contract for {parameter.Name}: review type, defaults, constraints and sensitivity in the module inputs.");
            }
            scope.InputChanges.AddRange(oldManifest.Parameters.Where(parameter =>
                !newManifest.Parameters.Any(item => item.Name == parameter.Name)).Select(parameter => $"Removed {parameter.Name}; remove it from saved configuration before upgrading."));
            scope.RequiresExistingServiceOwnership = !oldManifest.Parameters.Any(parameter => parameter.Name == "create_service") &&
                newManifest.Parameters.Any(parameter => parameter.Name == "create_service" &&
                    bool.TryParse(parameter.Default, out var createsService) && createsService);
            foreach (var environment in scope.Environments)
                scope.ConfigurationRevisions[environment.EnvironmentId.ToString()] =
                    _requestsPersistentSource.GetCatalogEnvironmentInputs(componentId, environment.EnvironmentId)?.Revision ?? 0;
            return Ok(scope);
        }

        [HttpPost("components/{componentId}/upgrade/{name}/{version}")]
        [SwaggerResponse(StatusCodes.Status200OK)]
        [SwaggerResponse(StatusCodes.Status403Forbidden)]
        [SwaggerResponse(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> UpgradeComponent(int componentId, string name, string version,
            [FromQuery] int projectId, [FromBody] CatalogUpgradeRequestApiModel request, CancellationToken cancellationToken)
        {
            if (request == null || !request.ConfirmImpact || request.EnvironmentIds == null || request.ConfigurationRevisions == null)
                return BadRequest("Explicit impact confirmation, environment IDs and configuration revisions are required.");
            var previewResult = await PreviewUpgrade(componentId, name, version, projectId, cancellationToken);
            if (previewResult is not OkObjectResult { Value: CatalogUpgradePreviewApiModel preview }) return previewResult;
            if (preview.RequiresExistingServiceOwnership && !request.PreserveExistingServiceOwnership)
                return BadRequest("This ownership transition requires explicitly saving create_service=false for all mapped environments. Service adoption/import is not supported.");
            var manifest = (await _templateCatalog.GetAsync(name, version, cancellationToken))!;
            var inputs = new Dictionary<int, Dictionary<string, string>>();
            foreach (var environment in preview.Environments)
            {
                var saved = ReadSavedInputs(_requestsPersistentSource.GetCatalogEnvironmentInputs(componentId, environment.EnvironmentId));
                if (preview.RequiresExistingServiceOwnership) saved["create_service"] = "false";
                if (saved.Keys.Any(key => !manifest.Parameters.Any(parameter => parameter.Name == key && !parameter.Sensitive)))
                    return Conflict($"Saved inputs for '{environment.EnvironmentName}' include removed or sensitive parameters. Explicitly replace that configuration first.");
                var validation = _parameterValidator.Validate(manifest with
                {
                    Parameters = manifest.Parameters.Where(parameter => saved.ContainsKey(parameter.Name)).ToArray(),
                    ParameterConflicts = null
                }, saved.ToDictionary(pair => pair.Key, pair => (string?)pair.Value));
                if (!validation.IsValid)
                    return Conflict($"Saved inputs for '{environment.EnvironmentName}' are incompatible: " +
                        string.Join(", ", validation.Errors.Select(error => error.ParameterName)));
                inputs[environment.EnvironmentId] = saved;
            }
            var username = _claimsPrincipalReader.GetUserFullDomainName(User);
            if (string.IsNullOrWhiteSpace(username)) return Forbid();
            try
            {
                _requestsPersistentSource.UpgradeCatalogComponent(componentId, projectId, version, request, inputs, username);
                return Ok();
            }
            catch (InvalidOperationException ex) { return Conflict(ex.Message); }
        }

        private TerraformReadinessApiModel DescribeReadiness(TerraformTemplateManifest manifest,
            IDictionary<string, VariableValue> resolved)
        {
            var readiness = new TerraformReadinessApiModel();
            try
            {
                readiness.AzureCredentialMode = TerraformArmCredentials.Resolve(resolved) == null
                    ? "Runner ambient Azure identity (not checked)"
                    : "Environment service principal configured (access not checked)";
            }
            catch (InvalidOperationException)
            {
                // Resolver diagnostics can contain property values; never return them.
                readiness.AzureCredentialMode = "Invalid environment service-principal configuration";
                readiness.Warnings.Add("Set valid TerraformClientId and TerraformTenantId GUIDs and secure TerraformClientSecret, or remove all three to use the runner ambient identity.");
            }
            var target = TerraformCloudTargeting.ResolveSubscription(resolved, out var warning);
            if (warning != null)
                readiness.Warnings.Add("Invalid or conflicting subscription cloud-resource configuration. Review the environment Cloud tab; the runner may use a fallback target.");
            var explicitSubscription = resolved.TryGetValue(TerraformCloudTargeting.SubscriptionIdPropertyName, out var subscription)
                ? Convert.ToString(subscription?.Value, CultureInfo.InvariantCulture) : null;
            readiness.AzureTargetSource = target != null
                ? "Environment subscription cloud resource (value hidden)"
                : !string.IsNullOrWhiteSpace(explicitSubscription)
                    ? Guid.TryParse(explicitSubscription, out _)
                        ? "TerraformSubscriptionId property (value hidden)"
                        : "Invalid TerraformSubscriptionId property"
                    : "Runner default subscription or ambient identity default (unknown to API)";
            if (target == null && string.IsNullOrWhiteSpace(explicitSubscription))
                readiness.Warnings.Add("No explicit Azure subscription target. The runner's fallback subscription must be checked before approving the plan.");
            readiness.AivenCredentialMode = TerraformArmCredentials.ResolveAivenApiToken(resolved) == null
                ? "No environment Aiven token; runner ambient configuration not checked"
                : "Environment Aiven token configured (access not checked)";
            var secureConfiguration = _configValuesPersistentSource.GetAllConfigValues(true)
                .Where(value => value.Secure).Select(value => value.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            readiness.WorkloadTarget = TerraformWorkloadTarget.Describe(manifest, resolved,
                target?.SubscriptionId ?? explicitSubscription,
                name => _propertyValuesPersistentSource.IsCachedPropertySecure(name) || secureConfiguration.Contains(name) ||
                    manifest.Parameters.Any(parameter => parameter.Name == name && parameter.Sensitive));
            var inputs = manifest.Parameters.ToDictionary(parameter => parameter.Name, parameter =>
                resolved.TryGetValue(parameter.Name, out var value)
                    ? Convert.ToString(value?.Value, CultureInfo.InvariantCulture) ?? parameter.Default
                    : parameter.Default);
            readiness.InvalidInputs = _parameterValidator.Validate(manifest, inputs)?.Errors
                .Select(error => error.ParameterName).Distinct().ToList() ?? new List<string>();
            return readiness;
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
        [SwaggerResponse(StatusCodes.Status409Conflict)]
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

            // Sanitize user-supplied strings before they reach the logger -
            // strip CR/LF so an attacker cannot inject forged log lines via
            // the component-name field. Non-printable control characters are
            // also trimmed for the same reason.
            var safeComponentName = SanitizeForLog(componentName);
            // GetUserName returns a non-email identifier and is the common
            // pattern used by the rest of this controller; avoids leaking
            // an email-classed PII into platform logs.
            var safeUserId = SanitizeForLog(_claimsPrincipalReader.GetUserName(User));
            CatalogSubmissionIdentity? submission = null;
            if (request.SubmissionKey.HasValue && deployEnvironment == null)
                return BadRequest("SubmissionKey is supported only for create-and-deploy requests.");

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

                if (request.SubmissionKey.HasValue)
                {
                    var caller = _claimsPrincipalReader.GetUserFullDomainName(User);
                    if (string.IsNullOrWhiteSpace(caller)) return Forbid();
                    if (request.SubmissionKey == Guid.Empty) return BadRequest("SubmissionKey must be a nonempty random UUID.");
                    submission = new CatalogSubmissionIdentity
                    {
                        Caller = caller,
                        Key = request.SubmissionKey.Value,
                        CanonicalPayload = System.Text.Json.JsonSerializer.Serialize(new
                        {
                            Template = manifest.Name,
                            Version = manifest.Version,
                            request.ProjectId,
                            Environment = deployEnvironment.EnvironmentName,
                            Component = componentName,
                            request.ParentComponentId,
                            Parameters = (request.Parameters ?? new Dictionary<string, string>())
                                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                                .Select(pair => new { pair.Key, pair.Value }).ToArray()
                        })
                    };
                    try
                    {
                        var previous = _requestsPersistentSource.FindCatalogSubmission(submission);
                        if (previous != null) return Ok(previous);
                    }
                    catch (CatalogSubmissionConflictException ex) { return Conflict(ex.Message); }
                }

                // Resolve only manifest-declared inputs. Explicit request
                // overrides win; otherwise we inherit the environment-scoped
                // value already configured in DOrc. Manifest defaults are used
                // only when runtime would rely on them.
                var supplied = (request.Parameters ?? new Dictionary<string, string>())
                    .ToDictionary(kv => kv.Key, kv => (string?)kv.Value);
                var existing = _projectsPersistentSource.GetComponentsForProject(project.ProjectName)
                    .SingleOrDefault(item => string.Equals(item.ComponentName, componentName, StringComparison.OrdinalIgnoreCase));
                var configuration = existing?.TerraformSourceType == TerraformSourceType.Catalog &&
                    existing.TerraformTemplateName == manifest.Name && existing.TerraformTemplateVersion == manifest.Version &&
                    existing.ComponentId.HasValue
                    ? _requestsPersistentSource.GetCatalogEnvironmentInputs(existing.ComponentId.Value, deployEnvironment.EnvironmentId) : null;
                if (configuration != null && configuration.TemplateVersion != manifest.Version)
                    return Conflict("Saved inputs target a different module version. Review and replace the configuration.");
                var validationInputs = BuildValidationInputs(manifest, supplied, deployEnvironment, ReadSavedInputs(configuration));
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

            CatalogComponentCreation creation;
            try
            {
                creation = _manageProjectsPersistentSource.CreateCatalogComponent(
                    component, request.ProjectId, request.ParentComponentId, allowReuse: deployEnvironment is not null,
                    actor: _claimsPrincipalReader.GetUserFullDomainName(User));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            switch (creation.Status)
            {
                case CatalogComponentCreationStatus.Conflict:
                    return Conflict(creation.Message);
                case CatalogComponentCreationStatus.Disabled:
                case CatalogComponentCreationStatus.InvalidParent:
                    return BadRequest(creation.Message);
                case CatalogComponentCreationStatus.ProjectNotFound:
                    return NotFound(creation.Message);
            }
            component = creation.Component!;
            if (submission != null)
                submission.ComponentJson = System.Text.Json.JsonSerializer.Serialize(component);
            _log.LogInformation(
                "Stock template '{Manifest}' component '{Component}' in project '{ProjectName}' (id {ProjectId}) {Outcome} by {UserId}.",
                $"{manifest.Name}@{manifest.Version}", safeComponentName, project.ProjectName, request.ProjectId, creation.Status, safeUserId);

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
                    Components = new List<string> { component.ComponentName },
                    RequestProperties = requestProperties,
                    CatalogSubmission = submission,
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
                catch (CatalogSubmissionConflictException ex)
                {
                    return Conflict(ex.Message);
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

            // Match PendingRequestProcessor: ConfigValues fill missing values
            // before environment scope values and request overrides are seeded.
            foreach (var configValue in _configValuesPersistentSource.GetAllConfigValues(true))
            {
                if (_variableResolver.GetPropertyValue(configValue.Key) is null
                    && (!configValue.IsForProd.HasValue
                        || configValue.IsForProd.Value == environment.EnvironmentIsProd))
                {
                    _variableResolver.SetPropertyValue(configValue.Key, configValue.Value);
                }
            }
            _variableScopeOptionsResolver.SetPropertyValues(_variableResolver, environment);

            // Derived environment properties must see the same overrides as
            // the Monitor, which seeds request properties before resolution.
            foreach (var parameter in manifest.Parameters.Where(p => supplied.ContainsKey(p.Name)))
            {
                _variableResolver.SetRequestSuppliedPropertyValue(parameter.Name, supplied[parameter.Name] ?? string.Empty);
            }
            return _variableResolver.LoadProperties();
        }

        private Dictionary<string, string?> BuildValidationInputs(
            TerraformTemplateManifest manifest,
            IDictionary<string, string?> supplied,
            EnvironmentApiModel environment,
            IDictionary<string, string>? savedInputs = null)
        {
            var resolvedProperties = LoadEnvironmentProperties(manifest, supplied, environment);
            var validationInputs = new Dictionary<string, string?>(supplied, StringComparer.Ordinal);
            foreach (var input in savedInputs ?? new Dictionary<string, string>())
                validationInputs.TryAdd(input.Key, input.Value);

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

                var authorization = AuthorizeForEnvironment(deploymentResultId, allowHistory: true);
                if (!authorization.Succeeded)
                {
                    return authorization.Failure!;
                }

                var deploymentResult = authorization.Result!;
                var receipt = _requestsPersistentSource.GetTerraformDeploymentReceipt(deploymentResultId);
                if (receipt != null && (string.IsNullOrWhiteSpace(receipt.EnvironmentName) ||
                    !_apiSecurityService.CanModifyEnvironment(User, receipt.EnvironmentName) ||
                    (receipt.EnvironmentId.HasValue && _environmentsPersistentSource.GetEnvironment(receipt.EnvironmentName)?.EnvironmentId != receipt.EnvironmentId)))
                    return StatusCode(StatusCodes.Status403Forbidden, NoEnvironmentAuthorityMessage);

                // Plan content is only loaded once authority is established. Ordering
                // matters: the plan embeds resolved variable values, so a refused caller
                // must not cause it to be read at all.
                var planContent = LoadPlanContentFromStorage(deploymentResultId);
                var contentDigest = TerraformPlanArtifactIntegrity.HashPlanContent(planContent);
                if (receipt != null && !string.Equals(receipt.PlanContentSha256, contentDigest, StringComparison.OrdinalIgnoreCase))
                    return Conflict("Retained plan content no longer matches its receipt. Do not apply it; create and review a fresh plan.");
                var approval = _requestsPersistentSource.GetTerraformPlanApproval(deploymentResultId);

                var plan = new TerraformPlanApiModel
                {
                    DeploymentResultId = deploymentResultId,
                    PlanContent = planContent,
                    CreatedAt = receipt?.PlannedAt.UtcDateTime ?? deploymentResult.StartedTime?.UtcDateTime,
                    Status = deploymentResult.Status ?? "Unknown",
                    PlanContentSha256 = contentDigest,
                    IsHistorical = authorization.IsHistorical,
                    Outcome = _requestsPersistentSource.GetTerraformDeploymentOutcome(deploymentResultId)?.ToApiModel(),
                    ApprovedBy = approval?.ApprovedBy,
                    ApprovedAt = approval?.ApprovedAt,
                    Receipt = receipt == null ? null : new TerraformDeploymentReceiptApiModel
                    {
                        DeploymentRequestId = receipt.DeploymentRequestId,
                        ProjectId = receipt.ProjectId,
                        DefinitionRevision = receipt.DefinitionRevision,
                        SourceType = receipt.SourceType,
                        RequestedSource = receipt.RequestedSource,
                        RequestedRef = receipt.RequestedRef,
                        SourceSubPath = receipt.SourceSubPath,
                        BuildIdentity = receipt.BuildIdentity,
                        ComponentId = receipt.ComponentId,
                        EnvironmentId = receipt.EnvironmentId,
                        WorkloadTarget = receipt.WorkloadTarget,
                        ProjectName = receipt.ProjectName,
                        EnvironmentName = receipt.EnvironmentName,
                        ComponentName = receipt.ComponentName,
                        TemplateName = receipt.TemplateName,
                        TemplateVersion = receipt.TemplateVersion,
                        GitCommitSha = receipt.GitCommitSha,
                        StateStorageAccount = receipt.StateStorageAccount,
                        StateContainerName = receipt.StateContainerName,
                        StateKey = receipt.StateKey,
                        PlannedAt = receipt.PlannedAt
                    }
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
        public IActionResult ConfirmTerraformPlan(int deploymentResultId, [FromQuery] string? planContentSha256 = null)
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

                if (deploymentRequest.Status != DeploymentRequestStatus.WaitingConfirmation.ToString() &&
                    deploymentRequest.Status != DeploymentRequestStatus.Confirmed.ToString())
                    return Conflict("The request is not awaiting approval. It may be processing, cancelled, or complete.");

                if (string.IsNullOrWhiteSpace(planContentSha256))
                    return Conflict("Reload and review the plan before confirming it.");

                var receipt = _requestsPersistentSource.GetTerraformDeploymentReceipt(deploymentResultId);
                if (receipt?.EnvironmentId != null &&
                    _environmentsPersistentSource.GetEnvironment(receipt.EnvironmentName)?.EnvironmentId != receipt.EnvironmentId)
                    return Conflict("The original environment identity is unavailable. Create and review a fresh plan.");
                var digests = TerraformPlanArtifactIntegrity.Calculate(_azureStorageAccountWorker, deploymentResultId);
                if (receipt != null &&
                    (receipt.DeploymentRequestId != deploymentRequest.Id ||
                     !string.Equals(receipt.EnvironmentName, deploymentRequest.EnvironmentName, StringComparison.OrdinalIgnoreCase) ||
                     !string.Equals(receipt.PlanSha256, digests.PlanSha256, StringComparison.OrdinalIgnoreCase) ||
                     !string.Equals(receipt.SourceSha256, digests.SourceSha256, StringComparison.OrdinalIgnoreCase) ||
                     !string.Equals(receipt.PlanContentSha256, digests.PlanContentSha256, StringComparison.OrdinalIgnoreCase)))
                    return Conflict("Plan artifacts or target no longer match the plan-time receipt. Create and review a fresh plan.");
                if (!string.Equals(planContentSha256, digests.PlanContentSha256, StringComparison.OrdinalIgnoreCase))
                    return Conflict("The plan has changed since it was displayed. Reload and review it before confirming.");

                var userName = _claimsPrincipalReader.GetUserFullDomainName(User);
                if (string.IsNullOrWhiteSpace(userName))
                    return StatusCode(StatusCodes.Status403Forbidden, "Unable to establish the approver identity.");
                var confirmedAt = DateTimeOffset.UtcNow;
                var transitioned = _requestsPersistentSource.TryConfirmTerraformPlan(deploymentResult,
                    new TerraformPlanApproval
                    {
                        DeploymentResultId = deploymentResultId,
                        DeploymentRequestId = deploymentRequest.Id,
                        ApprovedBy = userName,
                        ApprovedAt = confirmedAt,
                        PlanSha256 = digests.PlanSha256,
                        SourceSha256 = digests.SourceSha256,
                        PlanContentSha256 = digests.PlanContentSha256
                    });

                if (!transitioned)
                {
                    _log.LogWarning($"Terraform plan confirmation for deployment result ID {deploymentResultId} lost a race; the result is no longer awaiting confirmation.");
                    return Conflict($"Deployment result {deploymentResultId} is no longer awaiting confirmation. It may have been confirmed, declined, or started already.");
                }

                _log.LogInformation($"Terraform plan confirmed for deployment result ID: {deploymentResultId} by user: {userName}");

                // Note: The actual execution will be handled by the Monitor service when it picks up the Confirmed status

                return Ok(new
                {
                    message = "Terraform plan confirmed successfully",
                    deploymentResultId = deploymentResultId,
                    confirmedBy = userName,
                    confirmedAt
                });
            }
            catch (FileNotFoundException ex)
            {
                _log.LogWarning(ex, "Approval artifacts are missing for result {DeploymentResultId}.", deploymentResultId);
                return Conflict("Required plan artifacts are missing. Replan and review a fresh plan.");
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

                var userName = _claimsPrincipalReader.GetUserFullDomainName(User);
                var declinedAt = DateTimeOffset.UtcNow;
                var transitioned = _requestsPersistentSource.TryDeclineTerraformPlan(deploymentResult, userName, declinedAt);

                if (!transitioned)
                {
                    _log.LogWarning($"Terraform plan decline for deployment result ID {deploymentResultId} lost a race; the result is no longer awaiting confirmation.");
                    return Conflict($"Deployment result {deploymentResultId} is no longer awaiting confirmation. It may have been confirmed, declined, or started already.");
                }

                _log.LogInformation($"Terraform plan declined for deployment result ID: {deploymentResultId} by user: {userName}");

                return Ok(new
                {
                    message = "Terraform plan declined successfully",
                    deploymentResultId = deploymentResultId,
                    declinedBy = userName,
                    declinedAt
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
        private AuthorizationOutcome AuthorizeForEnvironment(int deploymentResultId, bool allowHistory = false)
        {
            DeploymentResultApiModel? deploymentResult;
            DeploymentRequestApiModel? deploymentRequest;
            var isHistorical = false;

            try
            {
                deploymentResult = _requestsPersistentSource.GetDeploymentResults(deploymentResultId);
                if (deploymentResult == null && allowHistory)
                {
                    deploymentResult = _requestsPersistentSource.GetArchivedDeploymentResult(deploymentResultId);
                    isHistorical = deploymentResult != null;
                }

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

            return AuthorizationOutcome.Allowed(deploymentResult, deploymentRequest, isHistorical);
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
            public bool IsHistorical { get; private init; }

            public bool Succeeded => Failure == null;

            public static AuthorizationOutcome Refused(IActionResult failure) =>
                new() { Failure = failure };

            public static AuthorizationOutcome Allowed(
                DeploymentResultApiModel result,
                DeploymentRequestApiModel request, bool isHistorical = false) =>
                new() { Result = result, Request = request, IsHistorical = isHistorical };
        }
    }
}
