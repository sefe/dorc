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
    public sealed class RefDataContainersController : ControllerBase
    {
        private static readonly JsonSerializerOptions _auditJsonOptions = new() { WriteIndented = true };

        private readonly IContainersPersistentSource _containersPersistentSource;
        private readonly IContainerAuditPersistentSource _containerAuditPersistentSource;
        private readonly IEnvironmentsPersistentSource _environmentsPersistentSource;
        private readonly IRolePrivilegesChecker _rolePrivilegesChecker;
        private readonly ISecurityPrivilegesChecker _securityPrivilegesChecker;
        private readonly IClaimsPrincipalReader _claimsPrincipalReader;

        public RefDataContainersController(
            IContainersPersistentSource containersPersistentSource,
            IContainerAuditPersistentSource containerAuditPersistentSource,
            IEnvironmentsPersistentSource environmentsPersistentSource,
            IRolePrivilegesChecker rolePrivilegesChecker,
            ISecurityPrivilegesChecker securityPrivilegesChecker,
            IClaimsPrincipalReader claimsPrincipalReader)
        {
            _containersPersistentSource = containersPersistentSource;
            _containerAuditPersistentSource = containerAuditPersistentSource;
            _environmentsPersistentSource = environmentsPersistentSource;
            _rolePrivilegesChecker = rolePrivilegesChecker;
            _securityPrivilegesChecker = securityPrivilegesChecker;
            _claimsPrincipalReader = claimsPrincipalReader;
        }

        /// <summary>
        /// Get all container definitions
        /// </summary>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(List<ContainerApiModel>))]
        [HttpGet]
        public IActionResult Get()
        {
            return Ok(_containersPersistentSource.GetAll().ToList());
        }

        /// <summary>
        /// Get container by id
        /// </summary>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(ContainerApiModel))]
        [SwaggerResponse(StatusCodes.Status404NotFound, Type = typeof(string))]
        [HttpGet]
        [Route("ById/{id}")]
        public IActionResult GetById(int id)
        {
            var container = _containersPersistentSource.GetById(id);
            if (container == null)
                return NotFound($"Container with id {id} not found");
            return Ok(container);
        }

        /// <summary>
        /// Get containers attached to an environment
        /// </summary>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(List<ContainerApiModel>))]
        [HttpGet]
        [Route("ByEnvId/{envId}")]
        public IActionResult GetByEnvId(int envId)
        {
            return Ok(_containersPersistentSource.GetForEnvironmentId(envId).ToList());
        }

        /// <summary>
        /// Create new container definition
        /// </summary>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(ContainerApiModel))]
        [SwaggerResponse(StatusCodes.Status403Forbidden, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status409Conflict, Type = typeof(string))]
        [HttpPost]
        public IActionResult Post([FromBody] ContainerApiModel model)
        {
            if (!_rolePrivilegesChecker.IsPowerUser(User) && !_rolePrivilegesChecker.IsAdmin(User))
                return StatusCode(StatusCodes.Status403Forbidden,
                    "Containers can only be created by PowerUsers or Admins!");

            if (_containersPersistentSource.GetByName(model.Name) != null)
                return Conflict($"A container with the name {model.Name} already exists!");

            var created = _containersPersistentSource.Add(model);

            _containerAuditPersistentSource.InsertContainerAudit(
                _claimsPrincipalReader.GetUserFullDomainName(User),
                ActionType.Create,
                created.Id,
                fromValue: null,
                toValue: JsonSerializer.Serialize(created, _auditJsonOptions));

            return Ok(created);
        }

        /// <summary>
        /// Update container definition
        /// </summary>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(ContainerApiModel))]
        [SwaggerResponse(StatusCodes.Status403Forbidden, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status404NotFound, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status409Conflict, Type = typeof(string))]
        [HttpPut]
        [Route("{id}")]
        public IActionResult Put(int id, [FromBody] ContainerApiModel model)
        {
            var forbidden = CheckWritePermissionForItem(id, "modify");
            if (forbidden != null)
                return forbidden;

            var before = _containersPersistentSource.GetById(id);
            if (before == null)
                return NotFound($"Container with id {id} not found");

            var sameName = _containersPersistentSource.GetByName(model.Name);
            if (sameName != null && sameName.Id != id)
                return Conflict($"A container with the name {model.Name} already exists!");

            var updated = _containersPersistentSource.Update(id, model);
            if (updated == null)
                return NotFound($"Container with id {id} not found");

            _containerAuditPersistentSource.InsertContainerAudit(
                _claimsPrincipalReader.GetUserFullDomainName(User),
                ActionType.Update,
                id,
                fromValue: JsonSerializer.Serialize(before, _auditJsonOptions),
                toValue: JsonSerializer.Serialize(updated, _auditJsonOptions));

            return Ok(updated);
        }

        /// <summary>
        /// Delete container definition
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

            var before = _containersPersistentSource.GetById(id);
            if (before == null)
                return NotFound($"Container with id {id} not found");

            var result = _containersPersistentSource.Delete(id);
            if (result)
            {
                _containerAuditPersistentSource.InsertContainerAudit(
                    _claimsPrincipalReader.GetUserFullDomainName(User),
                    ActionType.Delete,
                    id,
                    fromValue: JsonSerializer.Serialize(before, _auditJsonOptions),
                    toValue: null);
            }

            return Ok(new ApiBoolResult { Result = result });
        }

        /// <summary>
        /// Attach container to an environment
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
                var outcome = _containersPersistentSource.AttachToEnvironment(id, envId);
                switch (outcome)
                {
                    case EnvironmentAttachmentOutcome.ItemNotFound:
                        return NotFound($"Container with id {id} not found");
                    case EnvironmentAttachmentOutcome.EnvironmentNotFound:
                        return NotFound($"Environment with id {envId} not found");
                    case EnvironmentAttachmentOutcome.AlreadyAttached:
                        return Conflict("Container is already attached to this environment");
                    default:
                        _containerAuditPersistentSource.InsertContainerAudit(
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
                return Conflict("Container is already attached to this environment");
            }
        }

        /// <summary>
        /// Detach container from an environment
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

            var outcome = _containersPersistentSource.DetachFromEnvironment(id, envId);
            switch (outcome)
            {
                case EnvironmentAttachmentOutcome.ItemNotFound:
                    return NotFound($"Container with id {id} not found");
                case EnvironmentAttachmentOutcome.NotAttached:
                    return Conflict("Container is not attached to this environment");
                default:
                    _containerAuditPersistentSource.InsertContainerAudit(
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
            var environmentNames = _containersPersistentSource.GetEnvironmentNamesForId(id).ToList();

            if (environmentNames.Count == 0)
            {
                if (!_rolePrivilegesChecker.IsPowerUser(User) && !_rolePrivilegesChecker.IsAdmin(User))
                    return StatusCode(StatusCodes.Status403Forbidden,
                        $"Unattached containers can only be {action}d by PowerUsers or Admins!");
                return null;
            }

            foreach (var environmentName in environmentNames)
            {
                if (!_securityPrivilegesChecker.CanModifyEnvironment(User, environmentName))
                    return StatusCode(StatusCodes.Status403Forbidden,
                        $"You need write permission on environment '{environmentName}' to {action} this container");
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
