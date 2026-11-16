using System.Text.Json;
using Dorc.Api.Interfaces;
using Dorc.Api.Model;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Sources.Interfaces;
using Dorc.Terraform.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;

namespace Dorc.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class EphemeralEnvironmentsController : ControllerBase
    {
        private static readonly HashSet<string> AllowedNonProdTiers = new(StringComparer.OrdinalIgnoreCase)
        {
            "ephemeral", "xx", "dv", "qa", "it", "in", "ut", "np", "pp", "ff"
        };

        private static readonly HashSet<string> BlockedProdTiers = new(StringComparer.OrdinalIgnoreCase)
        {
            "pr", "dr", "prd", "live", "prod", "production"
        };

        private static readonly string[] SensitiveParameterNameTokens =
        {
            "password",
            "passwd",
            "passphrase",
            "secret",
            "token",
            "api_key",
            "apikey",
            "private_key",
            "privatekey",
            "access_key",
            "account_key",
            "master_key",
            "signing_key",
            "encryption_key",
            "connection_string",
            "connectionstring",
            "credential"
        };

        private const string SensitiveOverrideRejectedMessage =
            "Sensitive parameters cannot be passed as plain-text overrides; configure them as secure DOrc properties on the base environment.";

        private static readonly ParameterValidator ParameterValidatorInstance = new();

        private readonly ILogger<EphemeralEnvironmentsController> _log;
        private readonly IEnvironmentsPersistentSource _environmentsPersistentSource;
        private readonly IEnvironmentTemplatesPersistentSource _environmentTemplatesPersistentSource;
        private readonly IProjectsPersistentSource _projectsPersistentSource;
        private readonly IRequestsPersistentSource _requestsPersistentSource;
        private readonly ISecurityPrivilegesChecker _securityPrivilegesChecker;
        private readonly IRolePrivilegesChecker? _rolePrivilegesChecker;
        private readonly IClaimsPrincipalReader _claimsPrincipalReader;
        private readonly ITemplateCatalog _templateCatalog;
        private readonly IRequestService _requestService;
        private readonly IPropertyValuesPersistentSource? _propertyValuesPersistentSource;
        private readonly Func<DateTimeOffset> _timeProvider;

        [ActivatorUtilitiesConstructor]
        public EphemeralEnvironmentsController(
            ILogger<EphemeralEnvironmentsController> log,
            IEnvironmentsPersistentSource environmentsPersistentSource,
            IEnvironmentTemplatesPersistentSource environmentTemplatesPersistentSource,
            IProjectsPersistentSource projectsPersistentSource,
            IRequestsPersistentSource requestsPersistentSource,
            ISecurityPrivilegesChecker securityPrivilegesChecker,
            IClaimsPrincipalReader claimsPrincipalReader,
            ITemplateCatalog templateCatalog,
            IRequestService requestService,
            IRolePrivilegesChecker? rolePrivilegesChecker = null,
            IPropertyValuesPersistentSource? propertyValuesPersistentSource = null)
            : this(
                log,
                environmentsPersistentSource,
                environmentTemplatesPersistentSource,
                projectsPersistentSource,
                requestsPersistentSource,
                securityPrivilegesChecker,
                claimsPrincipalReader,
                templateCatalog,
                requestService,
                rolePrivilegesChecker,
                timeProvider: null,
                propertyValuesPersistentSource: propertyValuesPersistentSource)
        {
        }

        public EphemeralEnvironmentsController(
            ILogger<EphemeralEnvironmentsController> log,
            IEnvironmentsPersistentSource environmentsPersistentSource,
            IEnvironmentTemplatesPersistentSource environmentTemplatesPersistentSource,
            IProjectsPersistentSource projectsPersistentSource,
            IRequestsPersistentSource requestsPersistentSource,
            ISecurityPrivilegesChecker securityPrivilegesChecker,
            IClaimsPrincipalReader claimsPrincipalReader,
            ITemplateCatalog templateCatalog,
            IRequestService requestService,
            IRolePrivilegesChecker? rolePrivilegesChecker,
            Func<DateTimeOffset>? timeProvider,
            IPropertyValuesPersistentSource? propertyValuesPersistentSource = null)
        {
            _log = log;
            _environmentsPersistentSource = environmentsPersistentSource;
            _environmentTemplatesPersistentSource = environmentTemplatesPersistentSource;
            _projectsPersistentSource = projectsPersistentSource;
            _requestsPersistentSource = requestsPersistentSource;
            _securityPrivilegesChecker = securityPrivilegesChecker;
            _claimsPrincipalReader = claimsPrincipalReader;
            _templateCatalog = templateCatalog;
            _requestService = requestService;
            _rolePrivilegesChecker = rolePrivilegesChecker;
            _propertyValuesPersistentSource = propertyValuesPersistentSource;
            _timeProvider = timeProvider ?? (() => DateTimeOffset.UtcNow);
        }

        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<EnvironmentApiModel>), StatusCodes.Status200OK)]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(IEnumerable<EnvironmentApiModel>))]
        public IActionResult GetEphemeralEnvironments([FromQuery] int? projectId = null, [FromQuery] string? state = null)
        {
            var environments = _environmentsPersistentSource.GetEphemeralEnvironments(projectId, state, User);
            return Ok(environments);
        }

        [HttpGet("templates")]
        [ProducesResponseType(typeof(IEnumerable<EnvironmentTemplateApiModel>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(IEnumerable<EnvironmentTemplateApiModel>))]
        [SwaggerResponse(StatusCodes.Status400BadRequest)]
        public IActionResult GetTemplates([FromQuery] int projectId)
        {
            if (projectId <= 0)
            {
                return BadRequest("projectId must be a positive integer.");
            }

            return Ok(_environmentTemplatesPersistentSource.GetTemplatesForProject(projectId));
        }

        [HttpGet("templates/{templateId:int}")]
        [ProducesResponseType(typeof(EnvironmentTemplateApiModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(EnvironmentTemplateApiModel))]
        [SwaggerResponse(StatusCodes.Status404NotFound)]
        public IActionResult GetTemplate(int templateId)
        {
            var template = _environmentTemplatesPersistentSource.GetTemplateById(templateId);
            if (template is null)
            {
                return NotFound($"Environment template with ID {templateId} was not found.");
            }

            return Ok(template);
        }

        [HttpPost("templates")]
        [ProducesResponseType(typeof(EnvironmentTemplateApiModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(EnvironmentTemplateApiModel))]
        [SwaggerResponse(StatusCodes.Status400BadRequest)]
        [SwaggerResponse(StatusCodes.Status403Forbidden)]
        [SwaggerResponse(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> CreateTemplate(
            [FromBody] EnvironmentTemplateApiModel? model,
            CancellationToken cancellationToken = default)
        {
            if (model is null || model.ProjectId <= 0)
            {
                return BadRequest("A valid template with a positive ProjectId is required.");
            }

            var project = _projectsPersistentSource.GetProject(model.ProjectId);
            if (project is null)
            {
                return NotFound($"Project with ID {model.ProjectId} was not found.");
            }

            if (!_securityPrivilegesChecker.CanModifyProject(User, project.ProjectName))
            {
                return Forbid();
            }

            var baseEnvError = GetBaseEnvironmentAccessError(model.BaseEnvironmentId);
            if (baseEnvError is not null)
            {
                return baseEnvError;
            }

            try
            {
                var validationError = await ValidateTemplateComponentsAndOverridesAsync(project.ProjectId, model, cancellationToken);
                if (validationError is not null)
                {
                    return validationError;
                }

                var created = _environmentTemplatesPersistentSource.CreateTemplate(
                    model,
                    _claimsPrincipalReader.GetUserFullDomainName(User));
                return Ok(created);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPut("templates/{templateId:int}")]
        [ProducesResponseType(typeof(EnvironmentTemplateApiModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(EnvironmentTemplateApiModel))]
        [SwaggerResponse(StatusCodes.Status400BadRequest)]
        [SwaggerResponse(StatusCodes.Status403Forbidden)]
        [SwaggerResponse(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UpdateTemplate(
            int templateId,
            [FromBody] EnvironmentTemplateApiModel? model,
            CancellationToken cancellationToken = default)
        {
            if (model is null)
            {
                return BadRequest("Template payload is required.");
            }

            var existing = _environmentTemplatesPersistentSource.GetTemplateById(templateId);
            if (existing is null)
            {
                return NotFound($"Environment template with ID {templateId} was not found.");
            }

            var project = _projectsPersistentSource.GetProject(existing.ProjectId);
            if (project is null)
            {
                return NotFound($"Project with ID {existing.ProjectId} was not found.");
            }

            if (!_securityPrivilegesChecker.CanModifyProject(User, project.ProjectName))
            {
                return Forbid();
            }

            var baseEnvError = GetBaseEnvironmentAccessError(model.BaseEnvironmentId);
            if (baseEnvError is not null)
            {
                return baseEnvError;
            }

            model.Id = templateId;
            model.ProjectId = existing.ProjectId;

            try
            {
                var validationError = await ValidateTemplateComponentsAndOverridesAsync(project.ProjectId, model, cancellationToken);
                if (validationError is not null)
                {
                    return validationError;
                }

                var updated = _environmentTemplatesPersistentSource.UpdateTemplate(
                    model,
                    _claimsPrincipalReader.GetUserFullDomainName(User));
                return Ok(updated);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpDelete("templates/{templateId:int}")]
        [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(bool))]
        [SwaggerResponse(StatusCodes.Status403Forbidden)]
        [SwaggerResponse(StatusCodes.Status404NotFound)]
        public IActionResult DeleteTemplate(int templateId)
        {
            var existing = _environmentTemplatesPersistentSource.GetTemplateById(templateId);
            if (existing is null)
            {
                return NotFound($"Environment template with ID {templateId} was not found.");
            }

            var project = _projectsPersistentSource.GetProject(existing.ProjectId);
            if (project is null)
            {
                return NotFound($"Project with ID {existing.ProjectId} was not found.");
            }

            if (!_securityPrivilegesChecker.CanModifyProject(User, project.ProjectName))
            {
                return Forbid();
            }

            try
            {
                return Ok(_environmentTemplatesPersistentSource.DeleteTemplate(templateId));
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(ex.Message);
            }
        }

        [HttpPost("preview-tier-parameters")]
        [HttpPost("preview-parameters")]
        [ProducesResponseType(typeof(EphemeralEnvironmentTierPreviewResponseApiModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(EphemeralEnvironmentTierPreviewResponseApiModel))]
        [SwaggerResponse(StatusCodes.Status400BadRequest)]
        [SwaggerResponse(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> PreviewTierParameters(
            [FromBody] EphemeralEnvironmentTierPreviewRequestApiModel? request,
            CancellationToken cancellationToken)
        {
            if (request is null || request.ProjectId <= 0)
            {
                return BadRequest("ProjectId must be a positive integer.");
            }

            var project = _projectsPersistentSource.GetProject(request.ProjectId);
            if (project is null)
            {
                return NotFound($"Project with ID {request.ProjectId} was not found.");
            }

            EnvironmentTemplateApiModel? template = null;
            if (request.TemplateId.HasValue)
            {
                template = _environmentTemplatesPersistentSource.GetTemplateById(request.TemplateId.Value);
                if (template is null)
                {
                    return NotFound($"Environment template with ID {request.TemplateId.Value} was not found.");
                }

                if (template.ProjectId != request.ProjectId)
                {
                    return BadRequest("Template does not belong to the specified project.");
                }
            }

            if (!TryResolveNonProdTier(request.EnvironmentTier, template, out var resolvedTier, out var tierError))
            {
                return BadRequest(tierError);
            }

            Dictionary<string, string?> mergedOverrides;
            HashSet<string> templateOverrideKeys;
            try
            {
                mergedOverrides = MergeParameterOverrides(template?.TierParameterOverridesJson, request.ParameterOverrides, resolvedTier);
                templateOverrideKeys = ExtractOverrideKeys(template?.TierParameterOverridesJson);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }

            if (templateOverrideKeys.Any(IsLikelySensitiveParameterName))
            {
                return BadRequest(SensitiveOverrideRejectedMessage);
            }

            var baseEnvironmentId = ResolveEffectiveBaseEnvironmentId(request.BaseEnvironmentId, template);
            var baseEnvError = GetBaseEnvironmentAccessError(baseEnvironmentId);
            if (baseEnvError is not null)
            {
                return baseEnvError;
            }

            var envName = string.IsNullOrWhiteSpace(request.EnvironmentName)
                ? "ephemeral-preview"
                : request.EnvironmentName.Trim().ReplaceLineEndings(string.Empty);
            var targetComponents = GetTargetComponents(project.ProjectId, template?.ProvisionComponentIds, allowFallbackWhenEmpty: template is null);
            if (template?.ValidationComponentIds is { Count: > 0 })
            {
                targetComponents = targetComponents
                    .Concat(GetTargetComponents(project.ProjectId, template.ValidationComponentIds, allowFallbackWhenEmpty: false))
                    .DistinctBy(c => c.ComponentId)
                    .ToList();
            }

            var response = new EphemeralEnvironmentTierPreviewResponseApiModel
            {
                EnvironmentTier = resolvedTier,
                IsEphemeral = true
            };

            var resolvedPreviewCatalogComponents = new List<(ComponentApiModel Component, TerraformTemplateManifest Manifest)>();
            foreach (var component in targetComponents)
            {
                if (!IsCatalogTerraformComponent(component))
                {
                    continue;
                }

                var manifest = await _templateCatalog.GetAsync(
                    component.TerraformTemplateName,
                    component.TerraformTemplateVersion,
                    cancellationToken);
                if (manifest is null)
                {
                    if (mergedOverrides.Count > 0)
                    {
                        return BadRequest($"Catalog template manifest '{component.TerraformTemplateName}@{component.TerraformTemplateVersion}' could not be resolved to validate parameter overrides.");
                    }
                    continue;
                }

                if (manifest.Parameters.Any(p => p.Sensitive && templateOverrideKeys.Any(k => DoesOverrideKeyTargetParameter(k, component.ComponentName, p.Name))))
                {
                    return BadRequest(SensitiveOverrideRejectedMessage);
                }

                resolvedPreviewCatalogComponents.Add((component, manifest));
                var baseInputs = GetBaseEnvironmentParameterValues(component.ComponentId!.Value, baseEnvironmentId, out _);
                var preview = BuildComponentPreview(
                    component,
                    manifest,
                    envName,
                    project,
                    resolvedTier,
                    mergedOverrides,
                    baseInputs);
                var previewValueError = ValidateResolvedComponentParameters(component, manifest, preview.ResolvedParameters);
                if (previewValueError is not null)
                {
                    return BadRequest(previewValueError);
                }

                preview.ResolvedParameters = RedactSensitiveParameters(manifest, preview.ResolvedParameters);
                response.ComponentPreviews.Add(preview);
            }

            var unknownPreviewOverride = FindUnmatchedOverrideKey(
                request.ParameterOverrides,
                mergedOverrides,
                resolvedPreviewCatalogComponents);
            if (unknownPreviewOverride is not null)
            {
                return BadRequest($"Parameter override '{unknownPreviewOverride}' does not match any target component parameter.");
            }

            return Ok(response);
        }

        [HttpPost("provision")]
        [ProducesResponseType(typeof(EphemeralEnvironmentProvisionResponseApiModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(EphemeralEnvironmentProvisionResponseApiModel))]
        [SwaggerResponse(StatusCodes.Status400BadRequest)]
        [SwaggerResponse(StatusCodes.Status403Forbidden)]
        [SwaggerResponse(StatusCodes.Status404NotFound)]
        [SwaggerResponse(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Provision(
            [FromBody] EphemeralEnvironmentProvisionRequestApiModel? request,
            CancellationToken cancellationToken)
        {
            if (request is null || request.ProjectId <= 0 || string.IsNullOrWhiteSpace(request.EnvironmentName))
            {
                return BadRequest("ProjectId and EnvironmentName are required.");
            }

            var project = _projectsPersistentSource.GetProject(request.ProjectId);
            if (project is null)
            {
                return NotFound($"Project with ID {request.ProjectId} was not found.");
            }

            if (!_securityPrivilegesChecker.CanModifyProject(User, project.ProjectName))
            {
                return Forbid();
            }

            EnvironmentTemplateApiModel? template = null;
            if (request.TemplateId.HasValue)
            {
                template = _environmentTemplatesPersistentSource.GetTemplateById(request.TemplateId.Value);
                if (template is null)
                {
                    return NotFound($"Environment template with ID {request.TemplateId.Value} was not found.");
                }

                if (template.ProjectId != request.ProjectId)
                {
                    return BadRequest("Template does not belong to the specified project.");
                }
            }

            if (!TryResolveNonProdTier(request.EnvironmentTier, template, out var resolvedTier, out var tierError))
            {
                return BadRequest(tierError);
            }

            var baseEnvironmentId = ResolveEffectiveBaseEnvironmentId(request.BaseEnvironmentId, template);
            var baseEnvError = GetBaseEnvironmentAccessError(baseEnvironmentId);
            if (baseEnvError is not null)
            {
                return baseEnvError;
            }

            int maxTtlMinutes = template?.MaxTtlMinutes ?? 1440;
            int ttlMinutes = request.TtlMinutes ?? template?.DefaultTtlMinutes ?? 240;
            if (ttlMinutes <= 0 || ttlMinutes > maxTtlMinutes)
            {
                return BadRequest($"TtlMinutes must be between 1 and {maxTtlMinutes}.");
            }

            Dictionary<string, string?> mergedOverrides;
            HashSet<string> templateOverrideKeys;
            try
            {
                mergedOverrides = MergeParameterOverrides(template?.TierParameterOverridesJson, request.ParameterOverrides, resolvedTier);
                templateOverrideKeys = ExtractOverrideKeys(template?.TierParameterOverridesJson);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }

            if (templateOverrideKeys.Any(IsLikelySensitiveParameterName))
            {
                return BadRequest(SensitiveOverrideRejectedMessage);
            }

            var envName = request.EnvironmentName.Trim().ReplaceLineEndings(string.Empty);
            var envSlug = CatalogInputSuggestions.NormaliseEnvironmentName(envName);
            var conflictingExisting = _environmentsPersistentSource.GetEphemeralEnvironments(user: null)
                .FirstOrDefault(existing =>
                    string.Equals(CatalogInputSuggestions.NormaliseEnvironmentName(existing.EnvironmentName), envSlug, StringComparison.OrdinalIgnoreCase));
            if (_environmentsPersistentSource.EnvironmentExists(envName) || conflictingExisting is not null)
            {
                return Conflict($"Environment '{envName}' already exists or conflicts with normalized slug '{envSlug}'.");
            }

            if (template?.ProvisionComponentIds is { Count: > 0 } &&
                !AreAllTemplateComponentsValidAndEnabled(project.ProjectId, template.ProvisionComponentIds))
            {
                return BadRequest("One or more template provisioning components no longer exist or are disabled in this project.");
            }

            if (request.RunValidationComponents &&
                template?.ValidationComponentIds is { Count: > 0 } &&
                !AreAllTemplateComponentsValidAndEnabled(project.ProjectId, template.ValidationComponentIds))
            {
                return BadRequest("One or more template validation components no longer exist or are disabled in this project.");
            }

            var provisionComponents = GetTargetComponents(project.ProjectId, template?.ProvisionComponentIds, allowFallbackWhenEmpty: template is null);
            var validationComponents = request.RunValidationComponents && template?.ValidationComponentIds is { Count: > 0 }
                ? GetTargetComponents(project.ProjectId, template.ValidationComponentIds, allowFallbackWhenEmpty: false)
                : new List<ComponentApiModel>();

            if (!string.IsNullOrWhiteSpace(request.BuildUrl) &&
                request.BuildUrl.Trim().StartsWith(BuildDetails.CatalogSentinel, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest("BuildUrl must not use the reserved catalog scheme.");
            }

            if (validationComponents.Any(c => !IsCatalogTerraformComponent(c)) &&
                string.IsNullOrWhiteSpace(request.BuildUrl) &&
                string.IsNullOrWhiteSpace(request.BuildNum))
            {
                return BadRequest("BuildUrl or BuildNum is required when validation components include non-catalog components.");
            }

            var preparedCatalogInputs = new List<(ComponentApiModel Component, TerraformTemplateManifest Manifest, EphemeralComponentParameterPreviewApiModel Preview, HashSet<string> DorcPropertyOnlyKeys)>();
            foreach (var component in provisionComponents
                .Concat(validationComponents)
                .DistinctBy(c => c.ComponentId ?? -1))
            {
                if (!IsCatalogTerraformComponent(component))
                {
                    continue;
                }

                var manifest = await _templateCatalog.GetAsync(
                    component.TerraformTemplateName,
                    component.TerraformTemplateVersion,
                    cancellationToken);
                if (manifest is null)
                {
                    if (mergedOverrides.Count > 0)
                    {
                        return BadRequest($"Catalog template manifest '{component.TerraformTemplateName}@{component.TerraformTemplateVersion}' could not be resolved to validate parameter overrides.");
                    }
                    continue;
                }

                if (manifest.Parameters.Any(p => p.Sensitive && templateOverrideKeys.Any(k => DoesOverrideKeyTargetParameter(k, component.ComponentName, p.Name))))
                {
                    return BadRequest(SensitiveOverrideRejectedMessage);
                }

                var baseInputs = GetBaseEnvironmentParameterValues(component.ComponentId!.Value, baseEnvironmentId, out var dorcPropertyOnlyKeys);
                var preview = BuildComponentPreview(
                    component,
                    manifest,
                    envName,
                    project,
                    resolvedTier,
                    mergedOverrides,
                    baseInputs,
                    dorcPropertyOnlyKeys);
                var provisionValueError = ValidateResolvedComponentParameters(component, manifest, preview.ResolvedParameters);
                if (provisionValueError is not null)
                {
                    return BadRequest(provisionValueError);
                }

                preparedCatalogInputs.Add((component, manifest, preview, dorcPropertyOnlyKeys));
            }

            var allTemplateCatalogComponents = preparedCatalogInputs
                .Select(x => (x.Component, x.Manifest))
                .ToList();
            var inactiveValidationComponents = !request.RunValidationComponents && template?.ValidationComponentIds is { Count: > 0 }
                ? GetTargetComponents(project.ProjectId, template.ValidationComponentIds, allowFallbackWhenEmpty: false)
                    .Where(IsCatalogTerraformComponent)
                : Enumerable.Empty<ComponentApiModel>();
            foreach (var valComponent in inactiveValidationComponents)
            {
                var valManifest = await _templateCatalog.GetAsync(
                    valComponent.TerraformTemplateName,
                    valComponent.TerraformTemplateVersion,
                    cancellationToken);
                if (valManifest is not null)
                {
                    allTemplateCatalogComponents.Add((valComponent, valManifest));
                }
            }

            var unknownProvisionOverride = FindUnmatchedOverrideKey(
                request.ParameterOverrides,
                mergedOverrides,
                allTemplateCatalogComponents);
            if (unknownProvisionOverride is not null)
            {
                return BadRequest($"Parameter override '{unknownProvisionOverride}' does not match any target component parameter.");
            }

            var username = _claimsPrincipalReader.GetUserFullDomainName(User);
            EnvironmentApiModel createdEnvironment;
            try
            {
                createdEnvironment = _environmentsPersistentSource.CreateEnvironment(new EnvironmentApiModel
                {
                    EnvironmentName = envName,
                    EnvironmentIsProd = false,
                    EnvironmentSecure = false,
                    IsEphemeral = true,
                    EnvironmentTier = resolvedTier,
                    TemplateId = template?.Id,
                    ParentId = baseEnvironmentId,
                    ExpiresAtUtc = _timeProvider().UtcDateTime.AddMinutes(ttlMinutes),
                    EphemeralState = EphemeralEnvironmentStates.Provisioning,
                    Details = new EnvironmentDetailsApiModel
                    {
                        EnvironmentOwner = username,
                        Description = $"Ephemeral environment ({resolvedTier})"
                    }
                }, User);
            }
            catch (Exception ex) when (ex is InvalidOperationException or Microsoft.EntityFrameworkCore.DbUpdateException)
            {
                return Conflict(ex is InvalidOperationException && ex.Message.Contains("template", StringComparison.OrdinalIgnoreCase)
                    ? ex.Message
                    : $"Environment '{envName}' already exists.");
            }

            var requestSubmitted = false;
            try
            {
                if (baseEnvironmentId.HasValue)
                {
                    _environmentsPersistentSource.SetParentForEnvironment(baseEnvironmentId, createdEnvironment.EnvironmentId, User);
                    createdEnvironment.ParentId = baseEnvironmentId;
                }

                _projectsPersistentSource.AddEnvironmentMappingToProject(project.ProjectName, createdEnvironment.EnvironmentName, User);

                var response = new EphemeralEnvironmentProvisionResponseApiModel
                {
                    Environment = createdEnvironment
                };

                var sensitiveRequestProps = new List<RequestProperty>();
                var seenSensitivePropNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (component, manifest, preview, dorcPropertyOnlyKeys) in preparedCatalogInputs)
                {
                    var nonSensitiveInputs = StripSensitiveParameters(manifest, preview.ResolvedParameters, dorcPropertyOnlyKeys);
                    _requestsPersistentSource.SaveCatalogEnvironmentInputs(
                        component.ComponentId!.Value,
                        project.ProjectId,
                        createdEnvironment.EnvironmentId,
                        manifest.Version,
                        0,
                        nonSensitiveInputs,
                        username);
                    response.SeededComponentInputs[component.ComponentName] = RedactSensitiveParameters(manifest, preview.ResolvedParameters);
                    foreach (var kvp in preview.ResolvedParameters)
                    {
                        var scopedPropName = $"{component.ComponentName}:{kvp.Key}";
                        if (!nonSensitiveInputs.ContainsKey(kvp.Key) &&
                            request.ParameterOverrides is not null &&
                            request.ParameterOverrides.Keys.Any(k => DoesOverrideKeyTargetParameter(k, component.ComponentName, kvp.Key)) &&
                            seenSensitivePropNames.Add(scopedPropName))
                        {
                            sensitiveRequestProps.Add(new RequestProperty
                            {
                                PropertyName = scopedPropName,
                                PropertyValue = kvp.Value,
                                IsSensitive = true
                            });
                        }
                    }
                }

                var provisionProps = new List<RequestProperty>(sensitiveRequestProps)
                {
                    new RequestProperty
                    {
                        PropertyName = TerraformDestroyOperationConstants.EphemeralTargetStatePropertyName,
                        PropertyValue = validationComponents.Count > 0
                            ? EphemeralEnvironmentStates.Testing
                            : EphemeralEnvironmentStates.Ready
                    }
                };
                if (validationComponents.Count > 0)
                {
                    provisionProps.Add(new RequestProperty
                    {
                        PropertyName = TerraformDestroyOperationConstants.DeferredValidationComponentsPropertyName,
                        PropertyValue = string.Join(",", validationComponents.Select(c => c.ComponentId!.Value))
                    });
                    if (!string.IsNullOrWhiteSpace(request.BuildNum))
                    {
                        provisionProps.Add(new RequestProperty
                        {
                            PropertyName = TerraformDestroyOperationConstants.DeferredValidationBuildNumPropertyName,
                            PropertyValue = request.BuildNum.Trim().ReplaceLineEndings(string.Empty)
                        });
                    }
                    if (!string.IsNullOrWhiteSpace(request.BuildUrl))
                    {
                        provisionProps.Add(new RequestProperty
                        {
                            PropertyName = TerraformDestroyOperationConstants.DeferredValidationBuildUrlPropertyName,
                            PropertyValue = request.BuildUrl.Trim().ReplaceLineEndings(string.Empty)
                        });
                    }
                }

                if (provisionComponents.Count > 0)
                {
                    var requestId = SubmitComponentRequest(
                        project,
                        createdEnvironment.EnvironmentName,
                        provisionComponents,
                        provisionProps,
                        createdEnvironment.EnvironmentId,
                        request.BuildNum,
                        request.BuildUrl,
                        request.BuildText,
                        hasNonCatalogValidationComponents: validationComponents.Any(c => !IsCatalogTerraformComponent(c)));
                    requestSubmitted = true;
                    response.ProvisionRequestIds.Add(requestId);
                }
                else if (validationComponents.Count > 0)
                {
                    if (!_environmentsPersistentSource.TryTransitionEphemeralState(
                            createdEnvironment.EnvironmentName,
                            new[] { EphemeralEnvironmentStates.Provisioning },
                            EphemeralEnvironmentStates.Testing,
                            createdEnvironment.EnvironmentId))
                    {
                        return Conflict($"Ephemeral environment '{createdEnvironment.EnvironmentName}' is no longer in '{EphemeralEnvironmentStates.Provisioning}' state.");
                    }
                    createdEnvironment.EphemeralState = EphemeralEnvironmentStates.Testing;
                    var validationProps = new List<RequestProperty>(sensitiveRequestProps)
                    {
                        new()
                        {
                            PropertyName = TerraformDestroyOperationConstants.EphemeralTargetStatePropertyName,
                            PropertyValue = EphemeralEnvironmentStates.Ready
                        }
                    };
                    var validationRequestId = SubmitComponentRequest(
                        project,
                        createdEnvironment.EnvironmentName,
                        validationComponents,
                        validationProps,
                        createdEnvironment.EnvironmentId,
                        request.BuildNum,
                        request.BuildUrl,
                        request.BuildText);
                    requestSubmitted = true;
                    response.ValidationRequestIds.Add(validationRequestId);
                }
                else
                {
                    if (!_environmentsPersistentSource.TryTransitionEphemeralState(
                            createdEnvironment.EnvironmentName,
                            new[] { EphemeralEnvironmentStates.Provisioning },
                            EphemeralEnvironmentStates.Ready,
                            createdEnvironment.EnvironmentId))
                    {
                        return Conflict($"Ephemeral environment '{createdEnvironment.EnvironmentName}' is no longer in '{EphemeralEnvironmentStates.Provisioning}' state.");
                    }
                    createdEnvironment.EphemeralState = EphemeralEnvironmentStates.Ready;
                }

                _log.LogInformation(
                    "Provisioned ephemeral environment '{EnvironmentName}' (tier '{Tier}', TTL {TtlMinutes}m) for project '{ProjectName}'.",
                    createdEnvironment.EnvironmentName,
                    resolvedTier,
                    ttlMinutes,
                    project.ProjectName);

                return Ok(response);
            }
            catch (Exception ex)
            {
                if (!requestSubmitted)
                {
                    _log.LogError(
                        ex,
                        "Provisioning failed before request submission for ephemeral environment '{EnvironmentName}' ({EnvironmentId}); rolling back environment creation.",
                        createdEnvironment.EnvironmentName,
                        createdEnvironment.EnvironmentId);
                    _environmentsPersistentSource.DeleteEnvironmentInternal(
                        createdEnvironment.EnvironmentId,
                        onlyIfProvisioningWithoutRequests: true);
                }
                else
                {
                    _log.LogError(
                        ex,
                        "Provisioning failed after request submission for ephemeral environment '{EnvironmentName}' ({EnvironmentId}); transitioning to ProvisionFailed.",
                        createdEnvironment.EnvironmentName,
                        createdEnvironment.EnvironmentId);
                    _environmentsPersistentSource.TryTransitionEphemeralState(
                        createdEnvironment.EnvironmentName,
                        new[]
                        {
                            EphemeralEnvironmentStates.Provisioning,
                            EphemeralEnvironmentStates.Testing
                        },
                        EphemeralEnvironmentStates.ProvisionFailed,
                        createdEnvironment.EnvironmentId);
                }
                throw;
            }
        }

        [HttpPost("{environmentName}/teardown")]
        [ProducesResponseType(typeof(EphemeralEnvironmentTeardownResponseApiModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(EphemeralEnvironmentTeardownResponseApiModel))]
        [SwaggerResponse(StatusCodes.Status400BadRequest)]
        [SwaggerResponse(StatusCodes.Status403Forbidden)]
        [SwaggerResponse(StatusCodes.Status404NotFound)]
        [SwaggerResponse(StatusCodes.Status409Conflict)]
        public IActionResult Teardown(
            string environmentName,
            [FromBody] EphemeralEnvironmentTeardownRequestApiModel? request = null)
        {
            var sanitizedEnvironmentName = environmentName.Trim().ReplaceLineEndings(string.Empty);
            var environment = _environmentsPersistentSource.GetEnvironment(sanitizedEnvironmentName);
            if (environment is null)
            {
                return NotFound($"Environment '{sanitizedEnvironmentName}' was not found.");
            }

            if (!environment.IsEphemeral || environment.EnvironmentIsProd)
            {
                return BadRequest("Only non-production ephemeral environments can be torn down.");
            }

            if (!_securityPrivilegesChecker.CanModifyEnvironment(User, environment.EnvironmentName))
            {
                return Forbid();
            }

            var isStrandedTearingDown = string.Equals(
                environment.EphemeralState,
                EphemeralEnvironmentStates.TearingDown,
                StringComparison.OrdinalIgnoreCase);
            var hasActiveDestroy = isStrandedTearingDown && GetAllActiveNonProdRequests().Any(r =>
                string.Equals(r.EnvironmentName, environment.EnvironmentName, StringComparison.OrdinalIgnoreCase) &&
                IsDestroyRequest(r.RequestDetails));

            if (string.Equals(environment.EphemeralState, EphemeralEnvironmentStates.TornDown, StringComparison.OrdinalIgnoreCase) ||
                (isStrandedTearingDown && hasActiveDestroy) ||
                !_environmentsPersistentSource.TryClaimEphemeralEnvironmentForTeardown(
                    environment.EnvironmentId,
                    requireExpired: false,
                    _timeProvider().UtcDateTime,
                    out _,
                    allowReclaimStrandedTearingDown: isStrandedTearingDown))
            {
                return Conflict("Environment is already tearing down or torn down.");
            }

            var response = new EphemeralEnvironmentTeardownResponseApiModel
            {
                EnvironmentName = environment.EnvironmentName,
                EphemeralState = EphemeralEnvironmentStates.TearingDown
            };

            try
            {
                var username = _claimsPrincipalReader.GetUserFullDomainName(User);
                var allActiveRequests = GetAllActiveNonProdRequests();
                var hasInFlightCancellingRequests = false;

                foreach (var activeRequest in allActiveRequests)
                {
                    if (!string.Equals(activeRequest.EnvironmentName, environment.EnvironmentName, StringComparison.OrdinalIgnoreCase)
                        || IsDestroyRequest(activeRequest.RequestDetails))
                    {
                        continue;
                    }

                    if (string.Equals(activeRequest.Status, DeploymentRequestStatus.Cancelling.ToString(), StringComparison.OrdinalIgnoreCase))
                    {
                        hasInFlightCancellingRequests = true;
                        continue;
                    }

                    var isRunning = string.Equals(activeRequest.Status, DeploymentRequestStatus.Running.ToString(), StringComparison.OrdinalIgnoreCase)
                        || string.Equals(activeRequest.Status, DeploymentRequestStatus.Requesting.ToString(), StringComparison.OrdinalIgnoreCase);

                    if (isRunning)
                    {
                        hasInFlightCancellingRequests = true;
                    }

                    var targetStatus = isRunning
                        ? DeploymentRequestStatus.Cancelling
                        : DeploymentRequestStatus.Cancelled;

                    _requestsPersistentSource.UpdateRequestStatus(
                        activeRequest.Id,
                        targetStatus,
                        username,
                        _timeProvider());
                    response.CancelledRequestIds.Add(activeRequest.Id);
                }

                hasInFlightCancellingRequests |= GetAllActiveNonProdRequests().Any(r =>
                    string.Equals(r.EnvironmentName, environment.EnvironmentName, StringComparison.OrdinalIgnoreCase)
                    && !IsDestroyRequest(r.RequestDetails)
                    && string.Equals(r.Status, DeploymentRequestStatus.Cancelling.ToString(), StringComparison.OrdinalIgnoreCase));

                var template = environment.TemplateId.HasValue
                    ? _environmentTemplatesPersistentSource.GetTemplateById(environment.TemplateId.Value)
                    : null;

                var (projectBatches, hasUnresolvedDurableComponents) =
                    ResolveTeardownTerraformComponents(environment, template);

                if (hasUnresolvedDurableComponents)
                {
                    _environmentsPersistentSource.UpdateEphemeralState(
                        environment.EnvironmentName,
                        EphemeralEnvironmentStates.TeardownFailed);
                    _environmentsPersistentSource.UpdateEphemeralState(
                        environment.EnvironmentId,
                        EphemeralEnvironmentStates.TeardownFailed);
                    response.EphemeralState = EphemeralEnvironmentStates.TeardownFailed;
                    return Conflict("Provisioned Terraform components could not be resolved for teardown; environment marked TeardownFailed.");
                }

                var shouldDeleteAfterDestroy = request?.RetainMetadataAfterTeardown != true;
                if (projectBatches.Count > 0)
                {
                    for (var batchIndex = 0; batchIndex < projectBatches.Count; batchIndex++)
                    {
                        var (project, reversedTerraformComponents) = projectBatches[batchIndex];
                        var provisionedRequestProps = _requestsPersistentSource.GetProvisionedRequestPropertiesForEnvironment(
                            environment.EnvironmentId,
                            project.ProjectName);
                        var destroyProps = new List<RequestProperty>(provisionedRequestProps)
                        {
                            new() { PropertyName = TerraformDestroyOperationConstants.PropertyName, PropertyValue = TerraformDestroyOperationConstants.DestroyValue },
                            new() { PropertyName = TerraformDestroyOperationConstants.IsDestroyPropertyName, PropertyValue = "true" },
                            new() { PropertyName = TerraformDestroyOperationConstants.DestroyOrderPreReversedPropertyName, PropertyValue = "true" },
                            new()
                            {
                                PropertyName = TerraformDestroyOperationConstants.DestroyComponentIdsPropertyName,
                                PropertyValue = string.Join(",", reversedTerraformComponents.Select(c => c.ComponentId))
                            },
                            new()
                            {
                                PropertyName = TerraformDestroyOperationConstants.DeleteEnvironmentOnSuccessPropertyName,
                                PropertyValue = shouldDeleteAfterDestroy ? "true" : "false"
                            }
                        };

                        if (projectBatches.Count > 1)
                        {
                            destroyProps.Add(new RequestProperty
                            {
                                PropertyName = TerraformDestroyOperationConstants.DestroyBatchCountPropertyName,
                                PropertyValue = projectBatches.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                            });
                            if (response.DestroyRequestIds.Count > 0)
                            {
                                destroyProps.Add(new RequestProperty
                                {
                                    PropertyName = TerraformDestroyOperationConstants.SiblingDestroyRequestIdsPropertyName,
                                    PropertyValue = string.Join(",", response.DestroyRequestIds)
                                });
                            }
                        }

                        var submittedDestroyId = SubmitComponentRequest(
                            project,
                            environment.EnvironmentName,
                            reversedTerraformComponents,
                            destroyProps,
                            environment.EnvironmentId,
                            isDestroy: true);
                        response.DestroyRequestId = submittedDestroyId;
                        response.DestroyRequestIds.Add(submittedDestroyId);
                    }
                }
                else
                {
                    var shouldDeleteNoComponents = request is null
                        || request.ForceDeleteMetadataOnNoComponents
                        || !request.RetainMetadataAfterTeardown;
                    if (hasInFlightCancellingRequests)
                    {
                        _environmentsPersistentSource.UpdateEphemeralState(
                            environment.EnvironmentId,
                            EphemeralEnvironmentStates.TearingDown,
                            expiresAtUtc: shouldDeleteNoComponents ? DateTime.UnixEpoch.AddSeconds(1) : DateTime.UnixEpoch);
                        response.EphemeralState = EphemeralEnvironmentStates.TearingDown;
                        response.EnvironmentDeleted = false;
                    }
                    else if (shouldDeleteNoComponents)
                    {
                        var deleted = _environmentsPersistentSource.DeleteEnvironmentInternal(environment.EnvironmentId);
                        if (!deleted)
                        {
                            _environmentsPersistentSource.UpdateEphemeralState(
                                environment.EnvironmentId,
                                EphemeralEnvironmentStates.TornDown);
                        }
                        response.EphemeralState = EphemeralEnvironmentStates.TornDown;
                        response.EnvironmentDeleted = deleted;
                    }
                    else
                    {
                        _environmentsPersistentSource.UpdateEphemeralState(
                            environment.EnvironmentId,
                            EphemeralEnvironmentStates.TornDown);
                        response.EphemeralState = EphemeralEnvironmentStates.TornDown;
                        response.EnvironmentDeleted = false;
                    }
                }

                return Ok(response);
            }
            catch (Exception ex)
            {
                _log.LogError(
                    ex,
                    "Teardown failed for ephemeral environment '{EnvironmentName}' ({EnvironmentId}); transitioning state to TeardownFailed.",
                    environment.EnvironmentName,
                    environment.EnvironmentId);
                _environmentsPersistentSource.UpdateEphemeralState(
                    environment.EnvironmentName,
                    EphemeralEnvironmentStates.TeardownFailed);
                _environmentsPersistentSource.UpdateEphemeralState(
                    environment.EnvironmentId,
                    EphemeralEnvironmentStates.TeardownFailed);
                throw;
            }
        }

        [HttpPost("{environmentName}/extend-ttl")]
        [ProducesResponseType(typeof(EnvironmentApiModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(EnvironmentApiModel))]
        [SwaggerResponse(StatusCodes.Status400BadRequest)]
        [SwaggerResponse(StatusCodes.Status403Forbidden)]
        [SwaggerResponse(StatusCodes.Status404NotFound)]
        public IActionResult ExtendTtl(
            string environmentName,
            [FromBody] EphemeralEnvironmentExtendTtlRequestApiModel? request)
        {
            var sanitizedEnvironmentName = environmentName.Trim().ReplaceLineEndings(string.Empty);
            var environment = _environmentsPersistentSource.GetEnvironment(sanitizedEnvironmentName);
            if (environment is null)
            {
                return NotFound($"Environment '{sanitizedEnvironmentName}' was not found.");
            }

            if (!environment.IsEphemeral || environment.EnvironmentIsProd)
            {
                return BadRequest("Only non-production ephemeral environments can have their TTL extended.");
            }

            if (!_securityPrivilegesChecker.CanModifyEnvironment(User, environment.EnvironmentName))
            {
                return Forbid();
            }

            DateTime newExpiresAtUtc;
            int? additionalMinutes = null;
            if (request?.NewExpiresAtUtc is { } explicitExpiry)
            {
                newExpiresAtUtc = explicitExpiry;
            }
            else if (request?.AdditionalMinutes is > 0)
            {
                var nowUtc = _timeProvider().UtcDateTime;
                var baseTime = environment.ExpiresAtUtc is { } currentExpiry && currentExpiry > nowUtc
                    ? currentExpiry
                    : nowUtc;
                newExpiresAtUtc = baseTime.AddMinutes(request.AdditionalMinutes.Value);
                additionalMinutes = request.AdditionalMinutes.Value;
            }
            else
            {
                return BadRequest("Either NewExpiresAtUtc or a positive AdditionalMinutes value must be provided.");
            }

            var template = environment.TemplateId.HasValue
                ? _environmentTemplatesPersistentSource.GetTemplateById(environment.TemplateId.Value)
                : null;
            int maxTtlMinutes = template?.MaxTtlMinutes ?? 1440;

            try
            {
                var updated = _environmentsPersistentSource.ExtendEphemeralTtl(
                    environment.EnvironmentName,
                    newExpiresAtUtc,
                    maxTtlMinutes,
                    User,
                    environment.EnvironmentId,
                    additionalMinutes);
                if (updated is null)
                {
                    return Conflict($"Ephemeral environment '{environment.EnvironmentName}' was replaced or deleted before TTL extension could complete.");
                }
                return Ok(updated);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return BadRequest(ex.Message);
            }
        }

        public static bool IsLikelySensitiveParameterName(string? parameterName)
        {
            if (string.IsNullOrWhiteSpace(parameterName))
            {
                return false;
            }

            var colonIndex = parameterName.LastIndexOf(':');
            var unqualified = colonIndex >= 0 ? parameterName[(colonIndex + 1)..] : parameterName;
            foreach (var token in SensitiveParameterNameTokens)
            {
                if (unqualified.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool DoesOverrideKeyTargetParameter(
            string overrideKey,
            string? componentName,
            string parameterName)
        {
            var colonIndex = overrideKey.LastIndexOf(':');
            return colonIndex >= 0
                ? string.Equals(overrideKey[..colonIndex], componentName, StringComparison.OrdinalIgnoreCase) &&
                  string.Equals(overrideKey[(colonIndex + 1)..], parameterName, StringComparison.OrdinalIgnoreCase)
                : string.Equals(overrideKey, parameterName, StringComparison.OrdinalIgnoreCase);
        }

        private static string? ValidateResolvedComponentParameters(
            ComponentApiModel component,
            TerraformTemplateManifest manifest,
            IReadOnlyDictionary<string, string> resolvedParameters)
        {
            var effectiveManifest = manifest with
            {
                Parameters = manifest.Parameters
                    .Select(p => IsLikelySensitiveParameterName(p.Name) ? p with { Sensitive = true } : p)
                    .ToList()
            };
            var nullableParams = resolvedParameters.ToDictionary(
                kvp => kvp.Key,
                kvp => (string?)kvp.Value,
                StringComparer.Ordinal);
            var validation = ParameterValidatorInstance.Validate(effectiveManifest, nullableParams, isEphemeral: true, isProd: false);
            var error = validation.Errors.FirstOrDefault(e => e.Kind != ParameterValidationErrorKind.Missing);
            return error is null
                ? null
                : $"Invalid parameter value for component '{component.ComponentName}': {error.Message}";
        }

        private static string? FindUnmatchedOverrideKey(
            IDictionary<string, string>? requestOverrides,
            IReadOnlyDictionary<string, string?> mergedOverrides,
            IReadOnlyList<(ComponentApiModel Component, TerraformTemplateManifest Manifest)> resolvedCatalogComponents)
        {
            var keysToCheck = (requestOverrides?.Keys ?? Enumerable.Empty<string>())
                .Concat(mergedOverrides.Keys.Where(k => k.Contains(':')))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var key in keysToCheck)
            {
                var matched = resolvedCatalogComponents.Any(entry =>
                    entry.Manifest.Parameters.Any(p => DoesOverrideKeyTargetParameter(key, entry.Component.ComponentName, p.Name)));
                if (!matched)
                {
                    return key;
                }
            }

            return null;
        }

        private async Task<IActionResult?> ValidateTemplateComponentsAndOverridesAsync(
            int projectId,
            EnvironmentTemplateApiModel model,
            CancellationToken cancellationToken)
        {
            var overrideKeys = ExtractOverrideKeys(model.TierParameterOverridesJson);
            if (overrideKeys.Any(IsLikelySensitiveParameterName))
            {
                return BadRequest(SensitiveOverrideRejectedMessage);
            }

            var requestedIds = model.ProvisionComponentIds
                .Concat(model.ValidationComponentIds)
                .Distinct()
                .ToList();

            if (requestedIds.Count == 0 && overrideKeys.Count == 0)
            {
                return null;
            }

            var projectComponents = _projectsPersistentSource.GetComponentsForProject(projectId)
                .Where(c => c.ComponentId.HasValue)
                .ToDictionary(c => c.ComponentId!.Value);

            if (requestedIds.Count > 0)
            {
                var unknownIds = requestedIds.Where(id => !projectComponents.ContainsKey(id)).ToList();
                if (unknownIds.Count > 0)
                {
                    return BadRequest($"Unknown component IDs for project {projectId}: {string.Join(", ", unknownIds)}.");
                }

                var disabledIds = requestedIds.Where(id => projectComponents[id].IsEnabled == false).ToList();
                if (disabledIds.Count > 0)
                {
                    return BadRequest($"Disabled component IDs cannot be used in environment templates: {string.Join(", ", disabledIds)}.");
                }
            }

            if (overrideKeys.Count > 0)
            {
                var componentsToInspect = model.ProvisionComponentIds.Count > 0
                    ? model.ProvisionComponentIds
                        .Concat(model.ValidationComponentIds)
                        .Distinct()
                        .Select(id => projectComponents[id])
                    : projectComponents.Values;

                var resolvedTemplateCatalogComponents = new List<(ComponentApiModel Component, TerraformTemplateManifest Manifest)>();
                foreach (var component in componentsToInspect)
                {
                    if (!IsCatalogTerraformComponent(component))
                    {
                        continue;
                    }

                    var manifest = await _templateCatalog.GetAsync(
                        component.TerraformTemplateName,
                        component.TerraformTemplateVersion,
                        cancellationToken);
                    if (manifest is null)
                    {
                        return BadRequest($"Catalog template manifest '{component.TerraformTemplateName}@{component.TerraformTemplateVersion}' could not be resolved to validate parameter overrides.");
                    }

                    if (manifest.Parameters.Any(p => p.Sensitive && overrideKeys.Any(k => DoesOverrideKeyTargetParameter(k, component.ComponentName, p.Name))))
                    {
                        return BadRequest(SensitiveOverrideRejectedMessage);
                    }

                    resolvedTemplateCatalogComponents.Add((component, manifest));
                }

                var unknownTemplateOverride = FindUnmatchedOverrideKey(
                    null,
                    overrideKeys.ToDictionary(k => k, _ => (string?)string.Empty, StringComparer.OrdinalIgnoreCase),
                    resolvedTemplateCatalogComponents);
                if (unknownTemplateOverride is not null)
                {
                    return BadRequest($"Parameter override '{unknownTemplateOverride}' does not match any target component parameter.");
                }
            }

            return null;
        }

        private bool AreAllTemplateComponentsValidAndEnabled(int projectId, IList<int> componentIds)
        {
            var byId = _projectsPersistentSource.GetComponentsForProject(projectId)
                .Where(c => c.ComponentId.HasValue)
                .ToDictionary(c => c.ComponentId!.Value);

            foreach (var id in componentIds)
            {
                if (!byId.TryGetValue(id, out var comp) || comp.IsEnabled == false)
                {
                    return false;
                }
            }

            return true;
        }

        private static HashSet<string> ExtractOverrideKeys(string? templateOverridesJson)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(templateOverridesJson))
            {
                return keys;
            }

            try
            {
                using var doc = JsonDocument.Parse(templateOverridesJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return keys;
                }

                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var nested in prop.Value.EnumerateObject())
                        {
                            keys.Add(nested.Name);
                        }
                    }
                    else
                    {
                        keys.Add(prop.Name);
                    }
                }

                return keys;
            }
            catch (JsonException ex)
            {
                throw new ArgumentException("Invalid TierParameterOverridesJson format.", ex);
            }
        }

        private IActionResult? GetBaseEnvironmentAccessError(int? baseEnvironmentId)
        {
            if (!baseEnvironmentId.HasValue)
            {
                return null;
            }

            var baseEnvironment = _environmentsPersistentSource.GetEnvironment(baseEnvironmentId.Value, User)
                ?? _environmentsPersistentSource.GetEnvironment(baseEnvironmentId.Value);
            if (baseEnvironment is null || baseEnvironment.EnvironmentIsProd || baseEnvironment.IsEphemeral)
            {
                return BadRequest("Base environment must exist and must not be a production or ephemeral environment.");
            }

            if (!_securityPrivilegesChecker.CanModifyEnvironment(User, baseEnvironment.EnvironmentName) ||
                (!_securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(User, baseEnvironment.EnvironmentName) &&
                 !_rolePrivilegesChecker.IsAdmin(User)))
            {
                return Forbid();
            }

            if (baseEnvironment.EnvironmentSecure &&
                !_securityPrivilegesChecker.CanReadSecrets(User, baseEnvironment.EnvironmentName) &&
                !_rolePrivilegesChecker.IsAdmin(User))
            {
                return Forbid();
            }

            return null;
        }

        private (List<(ProjectApiModel Project, List<ComponentApiModel> Components)> ProjectBatches, bool HasUnresolvedDurableComponents)
            ResolveTeardownTerraformComponents(
                EnvironmentApiModel environment,
                EnvironmentTemplateApiModel? template)
        {
            var candidateProjects = new List<ProjectApiModel>();
            var seenProjectIds = new HashSet<int>();

            if (template is not null)
            {
                var templateProject = _projectsPersistentSource.GetProject(template.ProjectId);
                if (templateProject is not null)
                {
                    seenProjectIds.Add(templateProject.ProjectId);
                    candidateProjects.Add(templateProject);
                }
            }

            var mappedProjects = _environmentsPersistentSource.GetMappedProjects(environment.EnvironmentName);
            if (mappedProjects is not null)
            {
                foreach (var mapped in mappedProjects)
                {
                    if (seenProjectIds.Add(mapped.ProjectId))
                    {
                        candidateProjects.Add(mapped);
                    }
                }
            }

            var allComponents = new List<(ProjectApiModel Project, ComponentApiModel Component)>();
            var seenProjectComponents = new HashSet<(int ProjectId, int ComponentId)>();
            foreach (var proj in candidateProjects)
            {
                var projComponents = _projectsPersistentSource.GetComponentsForProject(proj.ProjectId);
                foreach (var comp in FlattenComponents(projComponents))
                {
                    if (!comp.ComponentId.HasValue || comp.ComponentType != ComponentType.Terraform || seenProjectComponents.Add((proj.ProjectId, comp.ComponentId.Value)))
                    {
                        allComponents.Add((proj, comp));
                    }
                }
            }

            foreach (var (pinnedProj, pinnedComp) in _requestsPersistentSource.GetProvisionedComponentDefinitionsForEnvironment(environment.EnvironmentId))
            {
                if (seenProjectComponents.Add((pinnedProj.ProjectId, pinnedComp.ComponentId.GetValueOrDefault())))
                {
                    allComponents.Add((pinnedProj, pinnedComp));
                }
            }

            var leafTerraformComponents = allComponents
                .Where(entry => IsLeafTerraformComponent(entry.Component))
                .ToList();
            var byId = allComponents
                .Where(entry => entry.Component.ComponentType == ComponentType.Terraform && entry.Component.ComponentId.HasValue)
                .ToLookup(entry => entry.Component.ComponentId!.Value);

            var durableOrderedIds = _requestsPersistentSource.GetProvisionedComponentIdsForEnvironment(environment.EnvironmentId) ?? Array.Empty<int>();
            if (durableOrderedIds.Any(id => !byId.Contains(id)))
            {
                return (new List<(ProjectApiModel, List<ComponentApiModel>)>(), true);
            }

            var selected = new List<(ProjectApiModel Project, ComponentApiModel Component)>();
            var selectedPairs = new HashSet<(int ProjectId, int ComponentId)>();

            var isUnprovisioned =
                string.Equals(environment.EphemeralState, EphemeralEnvironmentStates.Provisioning, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(environment.EphemeralState, EphemeralEnvironmentStates.ProvisionFailed, StringComparison.OrdinalIgnoreCase) ||
                environment.ExpiresAtUtc <= DateTime.UnixEpoch.AddSeconds(1);

            var orderedCandidateIds = durableOrderedIds.Count > 0 ||
                                      template is null ||
                                      isUnprovisioned
                ? durableOrderedIds
                : template.ProvisionComponentIds.Where(byId.Contains);

            foreach (var id in orderedCandidateIds)
            {
                var candidatesForId = byId[id].ToList();
                var appliedProjects = _requestsPersistentSource.GetProvisionedProjectsForComponent(environment.EnvironmentId, id);
                List<(ProjectApiModel Project, ComponentApiModel Component)> resolvedEntries;
                if (appliedProjects.Count > 0)
                {
                    resolvedEntries = new List<(ProjectApiModel Project, ComponentApiModel Component)>();
                    foreach (var appliedProject in appliedProjects)
                    {
                        var match = candidatesForId.FirstOrDefault(entry =>
                            string.Equals(entry.Project.ProjectName, appliedProject, StringComparison.OrdinalIgnoreCase));
                        if (match.Project is null)
                        {
                            return (new List<(ProjectApiModel, List<ComponentApiModel>)>(), true);
                        }
                        resolvedEntries.Add(match);
                    }
                }
                else
                {
                    var preferred = candidatesForId.FirstOrDefault(entry => entry.Project.ProjectId == template?.ProjectId);
                    resolvedEntries = new List<(ProjectApiModel Project, ComponentApiModel Component)>
                    {
                        preferred.Project is not null ? preferred : candidatesForId[0]
                    };
                }

                var takeSingleOccurrence = durableOrderedIds.Count(x => x == id) > 1;
                foreach (var entry in resolvedEntries)
                {
                    if (selectedPairs.Add((entry.Project.ProjectId, id)))
                    {
                        selected.Add(entry);
                        if (takeSingleOccurrence)
                        {
                            break;
                        }
                    }
                }
            }

            if (template is null && !isUnprovisioned && selected.Count == 0)
            {
                foreach (var entry in leafTerraformComponents.Where(e => e.Component.IsEnabled != false))
                {
                    if (!entry.Component.ComponentId.HasValue || selectedPairs.Add((entry.Project.ProjectId, entry.Component.ComponentId.Value)))
                    {
                        selected.Add(entry);
                    }
                }
            }

            var batches = new List<(ProjectApiModel Project, List<ComponentApiModel> Components)>();
            string? currentBatchBuildIdentity = null;
            for (var i = selected.Count - 1; i >= 0; i--)
            {
                var entry = selected[i];
                var entryBuildIdentity = ResolveDestroyBuildIdentity(environment.EnvironmentName, entry.Project.ProjectName, entry.Component);
                if (batches.Count > 0 &&
                    batches[^1].Project.ProjectId == entry.Project.ProjectId &&
                    string.Equals(currentBatchBuildIdentity ?? entryBuildIdentity, entryBuildIdentity ?? currentBatchBuildIdentity, StringComparison.OrdinalIgnoreCase))
                {
                    batches[^1].Components.Add(entry.Component);
                    currentBatchBuildIdentity = entryBuildIdentity ?? currentBatchBuildIdentity;
                }
                else
                {
                    batches.Add((entry.Project, new List<ComponentApiModel> { entry.Component }));
                    currentBatchBuildIdentity = entryBuildIdentity;
                }
            }

            return (batches, false);
        }

        private string? ResolveDestroyBuildIdentity(
            string environmentName,
            string projectName,
            ComponentApiModel component)
        {
            if (IsCatalogTerraformComponent(component))
            {
                return null;
            }

            var prior = _requestsPersistentSource.GetLatestAppliedRequestForEnvironment(
                environmentName,
                projectName,
                component.ComponentId);
            return prior is null
                ? null
                : $"{prior.BuildUri}|{prior.DropLocation}|{prior.BuildNumber}";
        }

        private static IEnumerable<ComponentApiModel> FlattenComponents(IEnumerable<ComponentApiModel> components)
        {
            foreach (var comp in components)
            {
                yield return comp;
                if (comp.Children is { Count: > 0 })
                {
                    foreach (var child in FlattenComponents(comp.Children))
                    {
                        yield return child;
                    }
                }
            }
        }

        private static bool IsLeafTerraformComponent(ComponentApiModel component)
        {
            return component.ComponentType == ComponentType.Terraform
                && (component.Children is null || !component.Children.Any());
        }

        private static bool TryResolveNonProdTier(
            string? requestedTier,
            EnvironmentTemplateApiModel? template,
            out string resolvedTier,
            out string errorMessage)
        {
            var rawTier = !string.IsNullOrWhiteSpace(requestedTier)
                ? requestedTier.Trim()
                : !string.IsNullOrWhiteSpace(template?.DefaultTier)
                    ? template.DefaultTier.Trim()
                    : "ephemeral";

            if (BlockedProdTiers.Contains(rawTier))
            {
                resolvedTier = string.Empty;
                errorMessage = $"Ephemeral environments cannot use production tier '{rawTier}'.";
                return false;
            }

            var normalized = CatalogInputSuggestions.EnvironmentTier(
                new CatalogInputSuggestions.Context(
                    string.Empty,
                    EnvironmentIsProd: false,
                    BusinessVertical: null,
                    ApplicationCode: null,
                    IsEphemeral: false,
                    ExplicitTierOverride: rawTier),
                out var tierReason);

            if (!tierReason.StartsWith("From explicit tier override", StringComparison.Ordinal) ||
                !AllowedNonProdTiers.Contains(normalized))
            {
                resolvedTier = string.Empty;
                errorMessage = $"Tier '{rawTier}' is not an allowed non-production ephemeral tier.";
                return false;
            }

            resolvedTier = normalized;
            errorMessage = string.Empty;
            return true;
        }

        private static int? ResolveEffectiveBaseEnvironmentId(
            int? requestBaseEnvironmentId,
            EnvironmentTemplateApiModel? template)
        {
            if (requestBaseEnvironmentId.HasValue)
            {
                return requestBaseEnvironmentId.Value > 0 ? requestBaseEnvironmentId.Value : null;
            }

            return template?.BaseEnvironmentId;
        }

        private Dictionary<string, string?> GetBaseEnvironmentParameterValues(
            int componentId,
            int? baseEnvironmentId,
            out HashSet<string> dorcPropertyOnlyKeys)
        {
            dorcPropertyOnlyKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            if (!baseEnvironmentId.HasValue || baseEnvironmentId.Value <= 0)
            {
                return result;
            }

            if (_propertyValuesPersistentSource is not null)
            {
                var dorcProps = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                foreach (var globalProp in _propertyValuesPersistentSource.GetGlobalProperties(null))
                {
                    if (globalProp.Property is { Secure: false } &&
                        !string.IsNullOrWhiteSpace(globalProp.Property.Name) &&
                        !IsLikelySensitiveParameterName(globalProp.Property.Name))
                    {
                        dorcProps[globalProp.Property.Name] = globalProp.Value;
                    }
                }

                var ancestorChain = new List<EnvironmentApiModel>();
                var visitedIds = new HashSet<int>();
                var currentId = baseEnvironmentId;
                while (currentId.HasValue && currentId.Value > 0 && visitedIds.Add(currentId.Value))
                {
                    var env = _environmentsPersistentSource.GetEnvironment(currentId.Value, User)
                        ?? _environmentsPersistentSource.GetEnvironment(currentId.Value);
                    if (env is null)
                    {
                        break;
                    }

                    ancestorChain.Add(env);
                    currentId = env.ParentId;
                }

                for (var i = ancestorChain.Count - 1; i >= 0; i--)
                {
                    var envName = ancestorChain[i].EnvironmentName;
                    if (string.IsNullOrWhiteSpace(envName))
                    {
                        continue;
                    }

                    foreach (var envProp in _propertyValuesPersistentSource.GetEnvironmentProperties(envName, null))
                    {
                        if (envProp.Property is { Secure: false } &&
                            !string.IsNullOrWhiteSpace(envProp.Property.Name) &&
                            !IsLikelySensitiveParameterName(envProp.Property.Name))
                        {
                            dorcProps[envProp.Property.Name] = envProp.Value;
                        }
                    }
                }

                foreach (var kvp in dorcProps)
                {
                    var expanded = ExpandDorcPropertyValue(kvp.Value, dorcProps);
                    if (expanded is not null && !expanded.Contains("$$", StringComparison.Ordinal))
                    {
                        result[kvp.Key] = expanded;
                        dorcPropertyOnlyKeys.Add(kvp.Key);
                    }
                }
            }

            var catalogInputs = _requestsPersistentSource.GetCatalogEnvironmentInputs(componentId, baseEnvironmentId.Value);
            if (catalogInputs is null || string.IsNullOrWhiteSpace(catalogInputs.InputsJson))
            {
                return result;
            }

            var deserialized = JsonSerializer.Deserialize<Dictionary<string, string?>>(catalogInputs.InputsJson);
            if (deserialized is not null)
            {
                foreach (var kvp in deserialized)
                {
                    if (!IsLikelySensitiveParameterName(kvp.Key))
                    {
                        result[kvp.Key] = kvp.Value;
                        dorcPropertyOnlyKeys.Remove(kvp.Key);
                    }
                }
            }

            return result;
        }

        private static string? ExpandDorcPropertyValue(string? value, IReadOnlyDictionary<string, string?> properties)
        {
            if (string.IsNullOrEmpty(value) || !value.Contains("$$", StringComparison.Ordinal))
            {
                return value;
            }

            return System.Text.RegularExpressions.Regex.Replace(value, @"\$\$([^$]+)\$\$", match =>
            {
                var token = match.Groups[1].Value;
                return properties.TryGetValue(token, out var replacement) &&
                       !string.IsNullOrEmpty(replacement) &&
                       !replacement.Contains("$$", StringComparison.Ordinal)
                    ? replacement
                    : match.Value;
            });
        }

        private List<ComponentApiModel> GetTargetComponents(
            int projectId,
            IList<int>? componentIds,
            bool allowFallbackWhenEmpty = true)
        {
            var allComponents = _projectsPersistentSource.GetComponentsForProject(projectId).ToList();
            if (componentIds is not { Count: > 0 })
            {
                if (!allowFallbackWhenEmpty)
                {
                    return new List<ComponentApiModel>();
                }

                return allComponents.Where(c => c.IsEnabled != false).ToList();
            }

            var byId = allComponents
                .Where(c => c.ComponentId.HasValue)
                .ToDictionary(c => c.ComponentId!.Value);

            var ordered = new List<ComponentApiModel>();
            foreach (var id in componentIds)
            {
                if (byId.TryGetValue(id, out var component))
                {
                    ordered.Add(component);
                }
            }

            return ordered;
        }

        private static bool IsCatalogTerraformComponent(ComponentApiModel component)
        {
            return component.ComponentType == ComponentType.Terraform
                && component.TerraformSourceType == TerraformSourceType.Catalog
                && component.ComponentId.HasValue
                && !string.IsNullOrWhiteSpace(component.TerraformTemplateName);
        }

        internal static Dictionary<string, string?> MergeParameterOverrides(
            string? templateOverridesJson,
            IDictionary<string, string>? requestOverrides,
            string resolvedTier = "ephemeral")
        {
            var merged = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(templateOverridesJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(templateOverridesJson);
                    if (doc.RootElement.ValueKind == JsonValueKind.Null)
                    {
                        // Treat JSON "null" as no template overrides
                    }
                    else if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    {
                        throw new ArgumentException("Invalid TierParameterOverridesJson format.");
                    }
                    else
                    {
                        JsonElement? matchedTierObject = null;
                        foreach (var prop in doc.RootElement.EnumerateObject())
                        {
                            if (prop.Value.ValueKind == JsonValueKind.Object)
                            {
                                if (string.Equals(prop.Name, resolvedTier, StringComparison.OrdinalIgnoreCase))
                                {
                                    matchedTierObject = prop.Value;
                                }
                            }
                            else
                            {
                                merged[prop.Name] = ReadPrimitiveJsonValue(prop.Value);
                            }
                        }

                        if (matchedTierObject is { } tierObj)
                        {
                            foreach (var nested in tierObj.EnumerateObject())
                            {
                                RemoveShadowedQualifiedKeys(merged, nested.Name);
                            }

                            foreach (var nested in tierObj.EnumerateObject())
                            {
                                merged[nested.Name] = ReadPrimitiveJsonValue(nested.Value);
                            }
                        }
                    }
                }
                catch (JsonException ex)
                {
                    throw new ArgumentException("Invalid TierParameterOverridesJson format.", ex);
                }
            }

            if (requestOverrides is not null)
            {
                foreach (var kvp in requestOverrides)
                {
                    RemoveShadowedQualifiedKeys(merged, kvp.Key);
                }

                foreach (var kvp in requestOverrides)
                {
                    merged[kvp.Key] = kvp.Value;
                }
            }

            return merged;
        }

        private static void RemoveShadowedQualifiedKeys(Dictionary<string, string?> target, string overrideKey)
        {
            if (overrideKey.Contains(':'))
            {
                return;
            }

            var suffix = ":" + overrideKey;
            var toRemove = target.Keys
                .Where(k => k.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var key in toRemove)
            {
                target.Remove(key);
            }
        }

        private static string? ReadPrimitiveJsonValue(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => null,
                _ => throw new ArgumentException("Invalid TierParameterOverridesJson format.")
            };
        }

        private static EphemeralComponentParameterPreviewApiModel BuildComponentPreview(
            ComponentApiModel component,
            TerraformTemplateManifest manifest,
            string environmentName,
            ProjectApiModel project,
            string resolvedTier,
            IReadOnlyDictionary<string, string?> mergedOverrides,
            IReadOnlyDictionary<string, string?>? baseInputs = null,
            HashSet<string>? dorcPropertyOnlyKeys = null)
        {
            var resolvedParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var forcedInvariants = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var effectiveContext = new CatalogInputSuggestions.Context(
                environmentName,
                EnvironmentIsProd: false,
                project.BusinessVertical,
                project.ApplicationCode,
                IsEphemeral: true,
                ExplicitTierOverride: resolvedTier,
                ModuleName: manifest.Name,
                ComponentName: component.ComponentName);

            foreach (var parameter in manifest.Parameters)
            {
                if (TierSizingProfileResolver.IsTerminationProtectionParameter(parameter.Name))
                {
                    resolvedParams[parameter.Name] = "false";
                    forcedInvariants[parameter.Name] = "false";
                    dorcPropertyOnlyKeys?.Remove(parameter.Name);
                    continue;
                }

                if (parameter.Default is not null)
                {
                    resolvedParams[parameter.Name] = parameter.Default;
                }

                var generalSuggestion = CatalogInputSuggestions.For(parameter, effectiveContext);
                if (generalSuggestion is not null)
                {
                    resolvedParams[parameter.Name] = generalSuggestion.Value;
                }

                if (baseInputs is not null
                    && baseInputs.TryGetValue(parameter.Name, out var baseValue)
                    && baseValue is not null
                    && !IsTierOrEnvironmentSpecificSuggestion(parameter, effectiveContext))
                {
                    resolvedParams[parameter.Name] = baseValue;
                }
                else
                {
                    dorcPropertyOnlyKeys?.Remove(parameter.Name);
                }

                if ((mergedOverrides.TryGetValue($"{component.ComponentName}:{parameter.Name}", out var overrideValue)
                    || mergedOverrides.TryGetValue(parameter.Name, out overrideValue))
                    && overrideValue is not null)
                {
                    resolvedParams[parameter.Name] = overrideValue;
                    dorcPropertyOnlyKeys?.Remove(parameter.Name);
                }
            }

            var hasExplicitDedicatedService =
                resolvedParams.TryGetValue("create_service", out var createService)
                && string.Equals(createService, "true", StringComparison.OrdinalIgnoreCase);
            var isSharedService =
                !hasExplicitDedicatedService
                && (string.Equals(createService, "false", StringComparison.OrdinalIgnoreCase)
                    || (resolvedParams.TryGetValue("service_name", out var serviceName)
                        && SharedHostServiceNames.Contains(serviceName)));

            return new EphemeralComponentParameterPreviewApiModel
            {
                ComponentId = component.ComponentId!.Value,
                ComponentName = component.ComponentName,
                TemplateName = manifest.Name,
                IsSharedService = isSharedService,
                ResolvedParameters = resolvedParams,
                ForcedInvariants = forcedInvariants
            };
        }

        private static readonly HashSet<string> SharedHostServiceNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "traveler-unstable-dev",
            "traveler-non-prod"
        };

        private static readonly HashSet<string> EnvironmentIdentityParameterNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "dorc_environment", "environment", "env", "environment_name",
            "environment_tier", "env_tier", "tier", "is_prod", "is_production"
        };

        private static bool IsTierOrEnvironmentSpecificSuggestion(
            TerraformTemplateParameter parameter,
            CatalogInputSuggestions.Context effectiveContext)
        {
            return EnvironmentIdentityParameterNames.Contains(parameter.Name) ||
                   string.Equals(parameter.Suggest, CatalogInputSuggestions.Hints.DorcEnvironment, StringComparison.OrdinalIgnoreCase) ||
                   TierSizingProfileResolver.TrySuggestForParameter(parameter, effectiveContext) is not null;
        }

        private static Dictionary<string, string> StripSensitiveParameters(
            TerraformTemplateManifest manifest,
            IReadOnlyDictionary<string, string> parameters,
            IReadOnlySet<string> excludedKeys)
        {
            var sensitiveNames = manifest.Parameters
                .Where(p => p.Sensitive)
                .Select(p => p.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var copy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in parameters)
            {
                if (!sensitiveNames.Contains(kvp.Key) &&
                    !IsLikelySensitiveParameterName(kvp.Key) &&
                    !excludedKeys.Contains(kvp.Key))
                {
                    copy[kvp.Key] = kvp.Value;
                }
            }

            return copy;
        }

        private static Dictionary<string, string> RedactSensitiveParameters(
            TerraformTemplateManifest manifest,
            IReadOnlyDictionary<string, string> parameters)
        {
            var sensitiveNames = manifest.Parameters
                .Where(p => p.Sensitive)
                .Select(p => p.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var copy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in parameters)
            {
                copy[kvp.Key] = (sensitiveNames.Contains(kvp.Key) || IsLikelySensitiveParameterName(kvp.Key))
                    ? "***REDACTED***"
                    : kvp.Value;
            }

            foreach (var sensitiveName in sensitiveNames)
            {
                copy[sensitiveName] = "***REDACTED***";
            }

            return copy;
        }

        private int SubmitComponentRequest(
            ProjectApiModel project,
            string environmentName,
            List<ComponentApiModel> components,
            List<RequestProperty> requestProperties,
            int environmentId,
            string? buildNum = null,
            string? buildUrl = null,
            string? buildText = null,
            bool isDestroy = false,
            bool hasNonCatalogValidationComponents = false)
        {
            requestProperties.Add(new RequestProperty
            {
                PropertyName = TerraformDestroyOperationConstants.EphemeralEnvironmentIdPropertyName,
                PropertyValue = environmentId.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });

            var allCatalog = components.All(IsCatalogTerraformComponent);
            var effectiveAllCatalog = allCatalog && !hasNonCatalogValidationComponents;
            var resolvedBuildUrl = !string.IsNullOrWhiteSpace(buildUrl)
                ? buildUrl.Trim().ReplaceLineEndings(string.Empty)
                : ((effectiveAllCatalog || isDestroy) ? BuildDetails.CatalogSentinel : string.Empty);
            var extractedVstsBuildUri = TryExtractVstsBuildUri(resolvedBuildUrl);
            var resolvedVstsUrl = !string.IsNullOrWhiteSpace(buildUrl) &&
                                  !resolvedBuildUrl.StartsWith(BuildDetails.CatalogSentinel, StringComparison.OrdinalIgnoreCase) &&
                                  (project.SourceControlType == SourceControlType.GitHub ||
                                   resolvedBuildUrl.StartsWith("vstfs", StringComparison.OrdinalIgnoreCase) ||
                                   extractedVstsBuildUri is not null)
                ? (extractedVstsBuildUri ?? resolvedBuildUrl)
                : null;
            if (!string.IsNullOrWhiteSpace(resolvedVstsUrl) &&
                resolvedVstsUrl.StartsWith("vstfs", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(project.ArtefactsUrl))
            {
                resolvedBuildUrl = project.ArtefactsUrl.Split(';')[0].Trim();
            }

            var resolvedBuildNum = !string.IsNullOrWhiteSpace(buildNum)
                ? buildNum.Trim().ReplaceLineEndings(string.Empty)
                : string.Empty;
            var resolvedBuildText = !string.IsNullOrWhiteSpace(buildText)
                ? buildText.Trim().ReplaceLineEndings(string.Empty)
                : ResolveDefaultBuildText(project, resolvedBuildUrl, resolvedBuildNum);

            var requestDto = new RequestDto
            {
                Project = project.ProjectName,
                Environment = environmentName,
                BuildUrl = resolvedBuildUrl,
                VstsUrl = resolvedVstsUrl,
                BuildText = resolvedBuildText,
                BuildNum = resolvedBuildNum,
                Components = components.Select(c => c.ComponentName).ToList(),
                RequestProperties = requestProperties
            };

            RequestStatusDto? status;
            try
            {
                status = _requestService.CreateRequest(requestDto, User);
            }
            catch (InvalidOperationException ex) when (isDestroy && ex.Message.Contains("Cannot find deployment component", StringComparison.OrdinalIgnoreCase))
            {
                var detail = new Dorc.Core.DeploymentRequestDetail
                {
                    EnvironmentName = environmentName,
                    BuildDetail = new Dorc.Core.BuildDetail
                    {
                        Project = project.ProjectName,
                        BuildNumber = resolvedBuildNum,
                        DropLocation = string.Empty,
                        Uri = resolvedVstsUrl ?? string.Empty
                    },
                    Components = requestDto.Components.ToList(),
                    Properties = requestProperties
                        .Select(p => new Dorc.Core.PropertyPair(p.PropertyName, p.PropertyValue) { IsSensitive = p.IsSensitive })
                        .ToList()
                };
                var submittedId = _requestsPersistentSource.SubmitRequest(new Dorc.PersistentData.Model.DeploymentRequest
                {
                    RequestDetails = new Dorc.Core.DeploymentRequestDetailSerializer().Serialize(detail),
                    UserName = _claimsPrincipalReader.GetUserFullDomainName(User),
                    RequestedTime = _timeProvider().UtcDateTime,
                    Project = project.ProjectName,
                    Environment = environmentName,
                    BuildNumber = resolvedBuildNum,
                    Components = string.Join("|", requestDto.Components)
                });
                status = new RequestStatusDto { Id = submittedId, Status = DeploymentRequestStatus.Pending.ToString() };
            }

            if (status is null || status.Id <= 0)
            {
                throw new InvalidOperationException(status?.Status ?? "Failed to submit deployment request.");
            }

            if (isDestroy && !allCatalog)
            {
                var nonCatalogComponentId = components.First(c => !IsCatalogTerraformComponent(c)).ComponentId;
                var priorRequest = _requestsPersistentSource.GetLatestAppliedRequestForEnvironment(
                    environmentName,
                    project.ProjectName,
                    nonCatalogComponentId);
                if (priorRequest is not null)
                {
                    _requestsPersistentSource.BackfillRequestBuildMetadata(
                        status.Id,
                        priorRequest.BuildUri,
                        priorRequest.DropLocation,
                        priorRequest.BuildNumber);
                }
            }

            return status.Id;
        }

        private static string? TryExtractVstsBuildUri(string? rawBuildUrl)
        {
            if (string.IsNullOrWhiteSpace(rawBuildUrl))
            {
                return null;
            }

            var match = System.Text.RegularExpressions.Regex.Match(
                rawBuildUrl,
                @"(?:[?&]buildId=|/_apis/build/Builds/)(\d+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success
                ? $"vstfs:///Build/Build/{match.Groups[1].Value}"
                : null;
        }

        public static string ResolveDefaultBuildText(
            ProjectApiModel project,
            string resolvedBuildUrl,
            string resolvedBuildNum)
        {
            if (resolvedBuildUrl.StartsWith(BuildDetails.CatalogSentinel, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            if (project.SourceControlType == SourceControlType.GitHub &&
                !string.IsNullOrWhiteSpace(project.ArtefactsSubPaths))
            {
                var firstWorkflow = project.ArtefactsSubPaths
                    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(firstWorkflow))
                {
                    return firstWorkflow;
                }
            }

            return resolvedBuildNum;
        }

        private List<DeploymentRequestApiModel> GetAllActiveNonProdRequests()
        {
            var primaryActiveRequests = _requestsPersistentSource.GetRequestsWithStatus(
                DeploymentRequestStatus.Pending,
                DeploymentRequestStatus.Running,
                DeploymentRequestStatus.Requesting,
                DeploymentRequestStatus.WaitingConfirmation,
                isProd: false) ?? Array.Empty<DeploymentRequestApiModel>();
            var secondaryActiveRequests = _requestsPersistentSource.GetRequestsWithStatus(
                DeploymentRequestStatus.Confirmed,
                DeploymentRequestStatus.Paused,
                isProd: false) ?? Array.Empty<DeploymentRequestApiModel>();
            var cancellingRequests = _requestsPersistentSource.GetRequestsWithStatus(
                DeploymentRequestStatus.Cancelling,
                isProd: false) ?? Array.Empty<DeploymentRequestApiModel>();
            return primaryActiveRequests.Concat(secondaryActiveRequests).Concat(cancellingRequests).ToList();
        }

        private static bool IsDestroyRequest(string? requestDetailsXml)
        {
            return Dorc.PersistentData.Sources.RequestsPersistentSource.IsDestroyRequestXml(requestDetailsXml);
        }
    }
}
