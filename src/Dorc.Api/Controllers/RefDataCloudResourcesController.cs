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
    public sealed class RefDataCloudResourcesController : ControllerBase
    {
        private static readonly JsonSerializerOptions _auditJsonOptions = new() { WriteIndented = true };

        private readonly ICloudResourcesPersistentSource _cloudResourcesPersistentSource;
        private readonly ICloudResourceAuditPersistentSource _cloudResourceAuditPersistentSource;
        private readonly IEnvironmentsPersistentSource _environmentsPersistentSource;
        private readonly IRolePrivilegesChecker _rolePrivilegesChecker;
        private readonly ISecurityPrivilegesChecker _securityPrivilegesChecker;
        private readonly IClaimsPrincipalReader _claimsPrincipalReader;

        public RefDataCloudResourcesController(
            ICloudResourcesPersistentSource cloudResourcesPersistentSource,
            ICloudResourceAuditPersistentSource cloudResourceAuditPersistentSource,
            IEnvironmentsPersistentSource environmentsPersistentSource,
            IRolePrivilegesChecker rolePrivilegesChecker,
            ISecurityPrivilegesChecker securityPrivilegesChecker,
            IClaimsPrincipalReader claimsPrincipalReader)
        {
            _cloudResourcesPersistentSource = cloudResourcesPersistentSource;
            _cloudResourceAuditPersistentSource = cloudResourceAuditPersistentSource;
            _environmentsPersistentSource = environmentsPersistentSource;
            _rolePrivilegesChecker = rolePrivilegesChecker;
            _securityPrivilegesChecker = securityPrivilegesChecker;
            _claimsPrincipalReader = claimsPrincipalReader;
        }

        /// <summary>
        /// Get all cloud resource definitions
        /// </summary>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(List<CloudResourceApiModel>))]
        [HttpGet]
        public IActionResult Get()
        {
            return Ok(_cloudResourcesPersistentSource.GetAll().ToList());
        }

        /// <summary>
        /// Get cloud resource by id
        /// </summary>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(CloudResourceApiModel))]
        [SwaggerResponse(StatusCodes.Status404NotFound, Type = typeof(string))]
        [HttpGet]
        [Route("ById/{id}")]
        public IActionResult GetById(int id)
        {
            var container = _cloudResourcesPersistentSource.GetById(id);
            if (container == null)
                return NotFound($"Cloud resource with id {id} not found");
            return Ok(container);
        }

        /// <summary>
        /// Get cloud resources attached to an environment
        /// </summary>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(List<CloudResourceApiModel>))]
        [HttpGet]
        [Route("ByEnvId/{envId}")]
        public IActionResult GetByEnvId(int envId)
        {
            return Ok(_cloudResourcesPersistentSource.GetForEnvironmentId(envId).ToList());
        }

        /// <summary>
        /// Create new cloud resource definition
        /// </summary>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(CloudResourceApiModel))]
        [SwaggerResponse(StatusCodes.Status403Forbidden, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status409Conflict, Type = typeof(string))]
        [HttpPost]
        public IActionResult Post([FromBody] CloudResourceApiModel model)
        {
            if (!_rolePrivilegesChecker.IsPowerUser(User) && !_rolePrivilegesChecker.IsAdmin(User))
                return StatusCode(StatusCodes.Status403Forbidden,
                    "Cloud resources can only be created by PowerUsers or Admins!");

            if (_cloudResourcesPersistentSource.GetByName(model.Name) != null)
                return Conflict($"A cloud resource with the name {model.Name} already exists!");

            var created = _cloudResourcesPersistentSource.Add(model);

            _cloudResourceAuditPersistentSource.InsertCloudResourceAudit(
                _claimsPrincipalReader.GetUserFullDomainName(User),
                ActionType.Create,
                created.Id,
                fromValue: null,
                toValue: JsonSerializer.Serialize(created, _auditJsonOptions));

            return Ok(created);
        }

        /// <summary>
        /// Update cloud resource definition
        /// </summary>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(CloudResourceApiModel))]
        [SwaggerResponse(StatusCodes.Status403Forbidden, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status404NotFound, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status409Conflict, Type = typeof(string))]
        [HttpPut]
        [Route("{id}")]
        public IActionResult Put(int id, [FromBody] CloudResourceApiModel model)
        {
            var forbidden = CheckWritePermissionForItem(id, "modify");
            if (forbidden != null)
                return forbidden;

            var before = _cloudResourcesPersistentSource.GetById(id);
            if (before == null)
                return NotFound($"Cloud resource with id {id} not found");

            var sameName = _cloudResourcesPersistentSource.GetByName(model.Name);
            if (sameName != null && sameName.Id != id)
                return Conflict($"A cloud resource with the name {model.Name} already exists!");

            var updated = _cloudResourcesPersistentSource.Update(id, model);
            if (updated == null)
                return NotFound($"Cloud resource with id {id} not found");

            _cloudResourceAuditPersistentSource.InsertCloudResourceAudit(
                _claimsPrincipalReader.GetUserFullDomainName(User),
                ActionType.Update,
                id,
                fromValue: JsonSerializer.Serialize(before, _auditJsonOptions),
                toValue: JsonSerializer.Serialize(updated, _auditJsonOptions));

            return Ok(updated);
        }

        /// <summary>
        /// Delete cloud resource definition
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

            var before = _cloudResourcesPersistentSource.GetById(id);
            if (before == null)
                return NotFound($"Cloud resource with id {id} not found");

            var result = _cloudResourcesPersistentSource.Delete(id);
            if (result)
            {
                _cloudResourceAuditPersistentSource.InsertCloudResourceAudit(
                    _claimsPrincipalReader.GetUserFullDomainName(User),
                    ActionType.Delete,
                    id,
                    fromValue: JsonSerializer.Serialize(before, _auditJsonOptions),
                    toValue: null);
            }

            return Ok(new ApiBoolResult { Result = result });
        }

        /// <summary>
        /// Attach cloud resource to an environment
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
                var outcome = _cloudResourcesPersistentSource.AttachToEnvironment(id, envId);
                switch (outcome)
                {
                    case EnvironmentAttachmentOutcome.ItemNotFound:
                        return NotFound($"Cloud resource with id {id} not found");
                    case EnvironmentAttachmentOutcome.EnvironmentNotFound:
                        return NotFound($"Environment with id {envId} not found");
                    case EnvironmentAttachmentOutcome.AlreadyAttached:
                        return Conflict("Cloud resource is already attached to this environment");
                    default:
                        _cloudResourceAuditPersistentSource.InsertCloudResourceAudit(
                            _claimsPrincipalReader.GetUserFullDomainName(User),
                            ActionType.Attach,
                            id,
                            fromValue: null,
                            toValue: $"Attached to environment '{environment!.EnvironmentName}'");
                        return Ok(new ApiBoolResult { Result = true });
                }
            }
            catch (DbUpdateException)
            {
                // Composite-PK backstop: a concurrent attach slipped past the
                // behavioural exists-check — surface as the same conflict.
                return Conflict("Cloud resource is already attached to this environment");
            }
        }

        /// <summary>
        /// Detach cloud resource from an environment
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

            var outcome = _cloudResourcesPersistentSource.DetachFromEnvironment(id, envId);
            switch (outcome)
            {
                case EnvironmentAttachmentOutcome.ItemNotFound:
                    return NotFound($"Cloud resource with id {id} not found");
                case EnvironmentAttachmentOutcome.NotAttached:
                    return Conflict("Cloud resource is not attached to this environment");
                default:
                    _cloudResourceAuditPersistentSource.InsertCloudResourceAudit(
                        _claimsPrincipalReader.GetUserFullDomainName(User),
                        ActionType.Detach,
                        id,
                        fromValue: $"Attached to environment '{environment!.EnvironmentName}'",
                        toValue: null);
                    return Ok(new ApiBoolResult { Result = true });
            }
        }

        // Update/Delete gating (HLPS §5.3): environment-write on every mapped
        // environment; items mapped to no environment fall back to PowerUser/Admin.
        private IActionResult? CheckWritePermissionForItem(int id, string action)
        {
            var environmentNames = _cloudResourcesPersistentSource.GetEnvironmentNamesForId(id).ToList();

            if (environmentNames.Count == 0)
            {
                if (!_rolePrivilegesChecker.IsPowerUser(User) && !_rolePrivilegesChecker.IsAdmin(User))
                    return StatusCode(StatusCodes.Status403Forbidden,
                        $"Unattached cloud resources can only be {action}d by PowerUsers or Admins!");
                return null;
            }

            foreach (var environmentName in environmentNames)
            {
                if (!_securityPrivilegesChecker.CanModifyEnvironment(User, environmentName))
                    return StatusCode(StatusCodes.Status403Forbidden,
                        $"You need write permission on environment '{environmentName}' to {action} this cloud resource");
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
