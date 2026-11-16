using System.Text.Json;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.Core.Security;
using Dorc.PersistentData;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
        private readonly SharedResourceAccessPolicy _accessPolicy;
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
            _accessPolicy = new SharedResourceAccessPolicy(rolePrivilegesChecker, securityPrivilegesChecker);
            _claimsPrincipalReader = claimsPrincipalReader;
        }

        /// <summary>
        /// Get all container definitions
        /// </summary>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(List<ContainerApiModel>))]
        [HttpGet]
        public IActionResult Get()
        {
            return Ok(_containersPersistentSource.GetAll().Select(WithCapabilities).ToList());
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
            return Ok(WithCapabilities(container));
        }

        /// <summary>
        /// Get containers attached to an environment
        /// </summary>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(List<ContainerApiModel>))]
        [HttpGet]
        [Route("ByEnvId/{envId}")]
        public IActionResult GetByEnvId(int envId)
        {
            return Ok(_containersPersistentSource.GetForEnvironmentId(envId).Select(WithCapabilities).ToList());
        }

        [HttpGet("Capabilities/{envId}")]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(SharedResourceCapabilities))]
        public IActionResult Capabilities(int envId)
        {
            var environment = _environmentsPersistentSource.GetEnvironment(envId, User);
            if (environment == null)
                return NotFound($"Environment with id {envId} not found");
            return Ok(new SharedResourceCapabilities
            {
                CanCreateDefinition = _accessPolicy.CanCreateDefinition(User),
                CanManageMembership = _accessPolicy.CanManageMembership(User, environment.EnvironmentName)
            });
        }

        /// <summary>
        /// Create new container definition
        /// </summary>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(ContainerApiModel))]
        [SwaggerResponse(StatusCodes.Status403Forbidden, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status409Conflict, Type = typeof(string))]
        [HttpPost]
        public IActionResult Post([FromBody] ContainerApiModel model)
            => _containersPersistentSource.ExecuteMutation<IActionResult>(() =>
        {
            if (!_accessPolicy.CanCreateDefinition(User))
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
        });

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
            => _containersPersistentSource.ExecuteMutation<IActionResult>(() =>
        {
            var forbidden = CheckWritePermissionForItem(id, "modify");
            if (forbidden != null)
                return forbidden;

            var before = _containersPersistentSource.GetById(id);
            if (before == null)
                return NotFound($"Container with id {id} not found");
            if (model.Revision != before.Revision)
                return Conflict("The container definition has changed. Reload it before saving.");

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
        });

        [HttpPut("{id}/tags")]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(ContainerApiModel))]
        [SwaggerResponse(StatusCodes.Status403Forbidden, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status404NotFound, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status409Conflict, Type = typeof(string))]
        public IActionResult PutTags(int id, [FromBody] ResourceTagsUpdate model)
            => _containersPersistentSource.ExecuteMutation<IActionResult>(() =>
        {
            var forbidden = CheckWritePermissionForItem(id, "modify");
            if (forbidden != null)
                return forbidden;
            var before = _containersPersistentSource.GetById(id);
            if (before == null)
                return NotFound($"Container with id {id} not found");
            if (before.Tags != model.ExpectedTags)
                return Conflict("The container tags have changed. Reload them before saving.");
            var updated = _containersPersistentSource.UpdateTags(id, model.Tags);
            _containerAuditPersistentSource.InsertContainerAudit(
                _claimsPrincipalReader.GetUserFullDomainName(User), ActionType.Update, id,
                JsonSerializer.Serialize(before, _auditJsonOptions), JsonSerializer.Serialize(updated, _auditJsonOptions));
            return Ok(updated);
        });

        /// <summary>
        /// Delete container definition
        /// </summary>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(ApiBoolResult))]
        [SwaggerResponse(StatusCodes.Status403Forbidden, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status404NotFound, Type = typeof(string))]
        [HttpDelete]
        [Route("{id}")]
        public IActionResult Delete(int id)
            => _containersPersistentSource.ExecuteMutation<IActionResult>(() =>
        {
            var forbidden = CheckWritePermissionForItem(id, "delete");
            if (forbidden != null)
                return forbidden;

            var before = _containersPersistentSource.GetById(id);
            if (before == null)
                return NotFound($"Container with id {id} not found");

            var result = _containersPersistentSource.Delete(id);
            if (!result)
                return NotFound($"Container with id {id} not found");

            _containerAuditPersistentSource.InsertContainerAudit(
                _claimsPrincipalReader.GetUserFullDomainName(User),
                ActionType.Delete,
                id,
                fromValue: JsonSerializer.Serialize(before, _auditJsonOptions),
                toValue: null);

            return Ok(new ApiBoolResult { Result = true });
        });

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
            => _containersPersistentSource.ExecuteMutation<IActionResult>(() =>
        {
            var (environment, error) = CheckWritePermissionForEnvironment(envId, "attach to");
            if (error != null)
                return error;

            var outcome = _containersPersistentSource.AttachToEnvironment(id, envId);
            switch (outcome)
            {
                case EnvironmentAttachmentOutcome.ItemNotFound:
                    return NotFound($"Container with id {id} not found");
                case EnvironmentAttachmentOutcome.EnvironmentNotFound:
                    return NotFound($"Environment with id {envId} not found");
                case EnvironmentAttachmentOutcome.AlreadyAttached:
                    return Conflict("Container is already attached to this environment");
                case EnvironmentAttachmentOutcome.Attached:
                    _containerAuditPersistentSource.InsertContainerAudit(
                        _claimsPrincipalReader.GetUserFullDomainName(User),
                        ActionType.Attach,
                        id,
                        fromValue: null,
                        toValue: $"Attached to environment '{environment!.EnvironmentName}'");
                    return Ok(new ApiBoolResult { Result = true });
                default:
                    throw new InvalidOperationException($"Unexpected attach outcome: {outcome}");
            }
        });

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
            => _containersPersistentSource.ExecuteMutation<IActionResult>(() =>
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
                case EnvironmentAttachmentOutcome.Detached:
                    _containerAuditPersistentSource.InsertContainerAudit(
                        _claimsPrincipalReader.GetUserFullDomainName(User),
                        ActionType.Detach,
                        id,
                        fromValue: $"Attached to environment '{environment!.EnvironmentName}'",
                        toValue: null);
                    return Ok(new ApiBoolResult { Result = true });
                default:
                    throw new InvalidOperationException($"Unexpected detach outcome: {outcome}");
            }
        });

        private ContainerApiModel WithCapabilities(ContainerApiModel model)
        {
            model.CanEditDefinition = _accessPolicy.DefinitionAccess(User, model.EnvironmentNames).IsAllowed;
            return model;
        }

        // Update/Delete gating (HLPS §5.3): environment-write on every mapped
        // environment; items mapped to no environment fall back to PowerUser/Admin.
        private IActionResult? CheckWritePermissionForItem(int id, string action)
        {
            var environmentNames = _containersPersistentSource.GetEnvironmentNamesForId(id).ToList();

            var access = _accessPolicy.DefinitionAccess(User, environmentNames);
            if (access.IsAllowed)
                return null;
            return StatusCode(StatusCodes.Status403Forbidden, access.DeniedEnvironment == null
                ? $"Unattached containers can only be {action}d by PowerUsers or Admins!"
                : $"You need write permission on environment '{access.DeniedEnvironment}' to {action} this container");
        }

        private (EnvironmentApiModel? Environment, IActionResult? Error) CheckWritePermissionForEnvironment(
            int envId, string action)
        {
            var environment = _environmentsPersistentSource.GetEnvironment(envId, User);
            if (environment == null)
                return (null, NotFound($"Environment with id {envId} not found"));

            if (!_accessPolicy.CanManageMembership(User, environment.EnvironmentName))
                return (environment, StatusCode(StatusCodes.Status403Forbidden,
                    $"You need write permission on environment '{environment.EnvironmentName}' to {action} it"));

            return (environment, null);
        }
    }
}
