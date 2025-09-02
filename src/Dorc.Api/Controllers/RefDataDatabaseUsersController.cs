using Dorc.Api.Interfaces;
using Dorc.ApiModel;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Dorc.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class RefDataDatabaseUsersController : ControllerBase
    {
        private readonly IManageUsers _manageUsers;
        private readonly IUserPermsPersistentSource _userPermsPersistentSource;
        private readonly IEnvironmentMapper _environmentMapper;

        public RefDataDatabaseUsersController(IManageUsers manageUsers, IEnvironmentMapper environmentMapper, IUserPermsPersistentSource userPermsPersistentSource)
        {
            _environmentMapper = environmentMapper;
            _manageUsers = manageUsers;
            _userPermsPersistentSource = userPermsPersistentSource;
        }

        /// <summary>
        /// Return database user list
        /// </summary>
        /// <param name="id">Database ID</param>
        /// <param name="user"></param>
        /// <param name="envId"></param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(IEnumerable<UserApiModel>))]
        [HttpGet]
        public IActionResult Get(int id, int envId = 0)
        {
            var etEnvironmentByDatabase = _environmentMapper.GetEnvironmentByDatabase(envId, id, User);
            if (etEnvironmentByDatabase == null)
                return StatusCode(StatusCodes.Status400BadRequest,
                    $"Environment doesn't have DB with Id {id}!");
            var users = _manageUsers.GetDatabaseUsers<UserApiModel>(id);

            return StatusCode(StatusCodes.Status200OK, users);
        }

        /// <summary>
        /// Returns the list of all users permissions for a given database on a specified server.
        /// </summary>
        /// <param name="serverName">The name of the database server.</param>
        /// <param name="databaseName">The name of the database.</param>
        /// <param name="dbType">The type of the database (optional).</param>
        /// <returns>A list of users with permissions for the specified database.</returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(IEnumerable<UserDbPermissionApiModel>))]
        [HttpGet]
        [Route("GetDbUsersPermissions")]
        public IActionResult GetDbUsersPermissions(string serverName, string databaseName, string? dbType = null)
        {
            var userPermissions = _userPermsPersistentSource.GetUserDbPermissions(serverName, databaseName, dbType);

            return StatusCode(StatusCodes.Status200OK, userPermissions);
        }
    }
}