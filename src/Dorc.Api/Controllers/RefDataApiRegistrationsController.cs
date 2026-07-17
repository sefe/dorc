using System.Text.Json;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;

namespace Dorc.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public sealed class RefDataApiRegistrationsController : ControllerBase
    {
        private static readonly JsonSerializerOptions _auditJsonOptions = new() { WriteIndented = true };

        private readonly IApiRegistrationsPersistentSource _apiRegistrationsPersistentSource;
        private readonly IApiRegistrationAuditPersistentSource _apiRegistrationAuditPersistentSource;
        private readonly IEnvironmentsPersistentSource _environmentsPersistentSource;
        private readonly IRolePrivilegesChecker _rolePrivilegesChecker;
        private readonly ISecurityPrivilegesChecker _securityPrivilegesChecker;
        private readonly IClaimsPrincipalReader _claimsPrincipalReader;

        public RefDataApiRegistrationsController(
            IApiRegistrationsPersistentSource apiRegistrationsPersistentSource,
            IApiRegistrationAuditPersistentSource apiRegistrationAuditPersistentSource,
            IEnvironmentsPersistentSource environmentsPersistentSource,
            IRolePrivilegesChecker rolePrivilegesChecker,
            ISecurityPrivilegesChecker securityPrivilegesChecker,
            IClaimsPrincipalReader claimsPrincipalReader)
        {
            _apiRegistrationsPersistentSource = apiRegistrationsPersistentSource;
            _apiRegistrationAuditPersistentSource = apiRegistrationAuditPersistentSource;
            _environmentsPersistentSource = environmentsPersistentSource;
            _rolePrivilegesChecker = rolePrivilegesChecker;
            _securityPrivilegesChecker = securityPrivilegesChecker;
            _claimsPrincipalReader = claimsPrincipalReader;
        }

        /// <summary>
        /// Get all API registration definitions
        /// </summary>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(List<ApiRegistrationApiModel>))]
        [HttpGet]
        public IActionResult Get()
        {
            return Ok(_apiRegistrationsPersistentSource.GetAll().ToList());
        }

        /// <summary>
        /// Get API registration by id
        /// </summary>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(ApiRegistrationApiModel))]
        [SwaggerResponse(StatusCodes.Status404NotFound, Type = typeof(string))]
        [HttpGet]
        [Route("ById/{id}")]
        public IActionResult GetById(int id)
        {
            var apiRegistration = _apiRegistrationsPersistentSource.GetById(id);
            if (apiRegistration == null)
                return NotFound($"API registration with id {id} not found");
            return Ok(apiRegistration);
        }

        /// <summary>
        /// Get API registrations attached to an environment
        /// </summary>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(List<ApiRegistrationApiModel>))]
        [HttpGet]
        [Route("ByEnvId/{envId}")]
        public IActionResult GetByEnvId(int envId)
        {
            return Ok(_apiRegistrationsPersistentSource.GetForEnvironmentId(envId).ToList());
        }

        /// <summary>
        /// Create new API registration definition
        /// </summary>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(ApiRegistrationApiModel))]
        [SwaggerResponse(StatusCodes.Status403Forbidden, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status409Conflict, Type = typeof(string))]
        [HttpPost]
        public IActionResult Post([FromBody] ApiRegistrationApiModel model)
        {
            if (!_rolePrivilegesChecker.IsPowerUser(User) && !_rolePrivilegesChecker.IsAdmin(User))
                return StatusCode(StatusCodes.Status403Forbidden,
                    "API registrations can only be created by PowerUsers or Admins!");

            if (_apiRegistrationsPersistentSource.GetByName(model.Name) != null)
                return Conflict($"An API registration with the name {model.Name} already exists!");

            var created = _apiRegistrationsPersistentSource.Add(model);

            _apiRegistrationAuditPersistentSource.InsertApiRegistrationAudit(
                _claimsPrincipalReader.GetUserFullDomainName(User),
                ActionType.Create,
                created.Id,
                fromValue: null,
                toValue: JsonSerializer.Serialize(created, _auditJsonOptions));

            return Ok(created);
        }

        /// <summary>
        /// Update API registration definition
        /// </summary>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(ApiRegistrationApiModel))]
        [SwaggerResponse(StatusCodes.Status403Forbidden, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status404NotFound, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status409Conflict, Type = typeof(string))]
        [HttpPut]
        [Route("{id}")]
        public IActionResult Put(int id, [FromBody] ApiRegistrationApiModel model)
        {
            var forbidden = CheckWritePermissionForItem(id, "modify");
            if (forbidden != null)
                return forbidden;

            var before = _apiRegistrationsPersistentSource.GetById(id);
            if (before == null)
                return NotFound($"API registration with id {id} not found");

            var sameName = _apiRegistrationsPersistentSource.GetByName(model.Name);
            if (sameName != null && sameName.Id != id)
                return Conflict($"An API registration with the name {model.Name} already exists!");

            var updated = _apiRegistrationsPersistentSource.Update(id, model);
            if (updated == null)
                return NotFound($"API registration with id {id} not found");

            _apiRegistrationAuditPersistentSource.InsertApiRegistrationAudit(
                _claimsPrincipalReader.GetUserFullDomainName(User),
                ActionType.Update,
                id,
                fromValue: JsonSerializer.Serialize(before, _auditJsonOptions),
                toValue: JsonSerializer.Serialize(updated, _auditJsonOptions));

            return Ok(updated);
        }

        /// <summary>
        /// Delete API registration definition
        /// </summary>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(ApiBoolResult))]
        [SwaggerResponse(StatusCodes.Status403Forbidden, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status404NotFound, Type = typeof(string))]
        [HttpDelete]
        [Route("{id}")]
        public IActionResult Delete(int id)
        {
            var forbidden = CheckWritePermissionForItem(id, "delete");
            if (forbidden != null)
                return forbidden;

            var before = _apiRegistrationsPersistentSource.GetById(id);
            if (before == null)
                return NotFound($"API registration with id {id} not found");

            var result = _apiRegistrationsPersistentSource.Delete(id);
            if (!result)
                return NotFound($"API registration with id {id} not found");

            _apiRegistrationAuditPersistentSource.InsertApiRegistrationAudit(
                _claimsPrincipalReader.GetUserFullDomainName(User),
                ActionType.Delete,
                id,
                fromValue: JsonSerializer.Serialize(before, _auditJsonOptions),
                toValue: null);

            return Ok(new ApiBoolResult { Result = true });
        }

        /// <summary>
        /// Attach API registration to an environment
        /// </summary>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(ApiBoolResult))]
        [SwaggerResponse(StatusCodes.Status403Forbidden, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status404NotFound, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status409Conflict, Type = typeof(string))]
        [HttpPut]
        [Route("{id}/environments/{envId}")]
        public IActionResult Attach(int id, int envId)
        {
            var (environment, error) = CheckWritePermissionForEnvironment(envId, "attach to");
            if (error != null)
                return error;

            try
            {
                var outcome = _apiRegistrationsPersistentSource.AttachToEnvironment(id, envId);
                switch (outcome)
                {
                    case EnvironmentAttachmentOutcome.ItemNotFound:
                        return NotFound($"API registration with id {id} not found");
                    case EnvironmentAttachmentOutcome.EnvironmentNotFound:
                        return NotFound($"Environment with id {envId} not found");
                    case EnvironmentAttachmentOutcome.AlreadyAttached:
                        return Conflict("API registration is already attached to this environment");
                    case EnvironmentAttachmentOutcome.Attached:
                        _apiRegistrationAuditPersistentSource.InsertApiRegistrationAudit(
                            _claimsPrincipalReader.GetUserFullDomainName(User),
                            ActionType.Attach,
                            id,
                            fromValue: null,
                            toValue: $"Attached to environment '{environment!.EnvironmentName}'");
                        return Ok(new ApiBoolResult { Result = true });
                    default:
                        throw new InvalidOperationException($"Unexpected attach outcome: {outcome}");
                }
            }
            catch (DbUpdateException)
            {
                // Composite-PK backstop: a concurrent attach slipped past the
                // behavioural exists-check — surface as the same conflict.
                return Conflict("API registration is already attached to this environment");
            }
        }

        /// <summary>
        /// Detach API registration from an environment
        /// </summary>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(ApiBoolResult))]
        [SwaggerResponse(StatusCodes.Status403Forbidden, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status404NotFound, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status409Conflict, Type = typeof(string))]
        [HttpDelete]
        [Route("{id}/environments/{envId}")]
        public IActionResult Detach(int id, int envId)
        {
            var (environment, error) = CheckWritePermissionForEnvironment(envId, "detach from");
            if (error != null)
                return error;

            var outcome = _apiRegistrationsPersistentSource.DetachFromEnvironment(id, envId);
            switch (outcome)
            {
                case EnvironmentAttachmentOutcome.ItemNotFound:
                    return NotFound($"API registration with id {id} not found");
                case EnvironmentAttachmentOutcome.NotAttached:
                    return Conflict("API registration is not attached to this environment");
                case EnvironmentAttachmentOutcome.Detached:
                    _apiRegistrationAuditPersistentSource.InsertApiRegistrationAudit(
                        _claimsPrincipalReader.GetUserFullDomainName(User),
                        ActionType.Detach,
                        id,
                        fromValue: $"Attached to environment '{environment!.EnvironmentName}'",
                        toValue: null);
                    return Ok(new ApiBoolResult { Result = true });
                default:
                    throw new InvalidOperationException($"Unexpected detach outcome: {outcome}");
            }
        }

        // Update/Delete gating (HLPS §5.3): environment-write on every mapped
        // environment; items mapped to no environment fall back to PowerUser/Admin.
        private IActionResult? CheckWritePermissionForItem(int id, string action)
        {
            var environmentNames = _apiRegistrationsPersistentSource.GetEnvironmentNamesForId(id).ToList();

            if (environmentNames.Count == 0)
            {
                if (!_rolePrivilegesChecker.IsPowerUser(User) && !_rolePrivilegesChecker.IsAdmin(User))
                    return StatusCode(StatusCodes.Status403Forbidden,
                        $"Unattached API registrations can only be {action}d by PowerUsers or Admins!");
                return null;
            }

            foreach (var environmentName in environmentNames)
            {
                if (!_securityPrivilegesChecker.CanModifyEnvironment(User, environmentName))
                    return StatusCode(StatusCodes.Status403Forbidden,
                        $"You need write permission on environment '{environmentName}' to {action} this API registration");
            }

            return null;
        }

        private (EnvironmentApiModel? Environment, IActionResult? Error) CheckWritePermissionForEnvironment(
            int envId, string action)
        {
            var environment = _environmentsPersistentSource.GetEnvironment(envId, User);
            if (environment == null)
                return (null, NotFound($"Environment with id {envId} not found"));

            if (!_securityPrivilegesChecker.CanModifyEnvironment(User, environment.EnvironmentName))
                return (environment, StatusCode(StatusCodes.Status403Forbidden,
                    $"You need write permission on environment '{environment.EnvironmentName}' to {action} it"));

            return (environment, null);
        }
    }
}
