using Dorc.Api.Interfaces;
using Dorc.Api.Exceptions;
using Dorc.Api.Services;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Dorc.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class RefDataUserPermissionsController : ControllerBase
    {
        private readonly IManageUsers _apiServices;
        private readonly IUserPermsPersistentSource _userPermsPersistentSource;
        private readonly ISecurityPrivilegesChecker _securityPrivilegesChecker;
        private readonly IEnvironmentMapper _environmentMapper;

        public RefDataUserPermissionsController(IManageUsers services,
            IUserPermsPersistentSource userPermsPersistentSource, ISecurityPrivilegesChecker securityPrivilegesChecker,
            IEnvironmentMapper environmentMapper)
        {
            _environmentMapper = environmentMapper;
            _securityPrivilegesChecker = securityPrivilegesChecker;
            _userPermsPersistentSource = userPermsPersistentSource;
            _apiServices = services;
        }

        /// <summary>
        /// Get user permissions for database
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <param name="databaseId">Database Id</param>
        /// <param name="envId"></param>
        /// <returns>Returns json that contains array of UserPermDto objects</returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(IEnumerable<UserPermDto>))]
        [HttpGet]
        public IActionResult Get([FromQuery] int userId, [FromQuery] int databaseId = 0, [FromQuery] int envId = 0)
        {
            if (databaseId == 0)
            {
                var allPermissions = _apiServices.GetUserPermissions<UserPermDto>(userId);
                return StatusCode(StatusCodes.Status200OK, allPermissions);
            }

            var etEnvironmentByDatabase = _environmentMapper.GetEnvironmentByDatabase(envId, databaseId, User);
            if (etEnvironmentByDatabase == null)
                return StatusCode(StatusCodes.Status400BadRequest,
                    $"Unable to find an environment with db Id {databaseId}!");
            var result = _userPermsPersistentSource.GetPermissions(userId, databaseId);
            return StatusCode(StatusCodes.Status200OK, result);
        }

        /// <summary>
        /// Add database permission
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <param name="permissionId">Permission ID</param>
        /// <param name="dbId">Database ID</param>
        /// <param name="envId"></param>
        /// <returns>true if success, false if fails</returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(bool))]
        [HttpPut]
        public IActionResult Put([FromQuery] int userId, [FromQuery] int permissionId, [FromQuery] int dbId, [FromQuery] int envId = 0)
        {
            var etEnvironmentByDatabase = _environmentMapper.GetEnvironmentByDatabase(envId, dbId, User);
            if (etEnvironmentByDatabase == null || !_securityPrivilegesChecker.CanModifyEnvironment(User, etEnvironmentByDatabase.EnvironmentName))
                return StatusCode(StatusCodes.Status403Forbidden,
                    new NonEnoughRightsException("User doesn't have \"Modify\" permission for this action!"));

            var result = _userPermsPersistentSource.AddUserPermission(userId, permissionId, dbId);
            return StatusCode(StatusCodes.Status200OK, result);
        }

        /// <summary>
        /// Remove database permission
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <param name="permissionId">Permission ID</param>
        /// <param name="dbId">Database ID</param>
        /// <param name="envId"></param>
        /// <returns>true if success, false if fails</returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(bool))]
        [HttpDelete]
        public IActionResult Delete([FromQuery] int userId, [FromQuery] int permissionId, [FromQuery] int dbId, [FromQuery] int envId = 0)
        {
            var etEnvironmentByDatabase = _environmentMapper.GetEnvironmentByDatabase(envId, dbId, User);
            if (etEnvironmentByDatabase == null || !_securityPrivilegesChecker.CanModifyEnvironment(User, etEnvironmentByDatabase.EnvironmentName))
                return StatusCode(StatusCodes.Status403Forbidden,
                    new NonEnoughRightsException("User doesn't have \"Modify\" permission for this action!"));
            var result = _userPermsPersistentSource.DeleteUserPermission(userId, permissionId, dbId);
            return StatusCode(StatusCodes.Status200OK, result);
        }
    }
}