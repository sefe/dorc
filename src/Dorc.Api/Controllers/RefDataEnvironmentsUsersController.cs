using Dorc.Api.Interfaces;
using Dorc.Api.Services;
using Dorc.ApiModel;
using Dorc.Core;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Repositories;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Dorc.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class RefDataEnvironmentsUsersController : ControllerBase
    {
        private readonly IManageUsers _manageUsers;
        private readonly ISecurityPrivilegesChecker _securityService;
        private readonly IEnvironmentsPersistentSource _environmentsPersistentSource;
        private readonly IActiveDirectorySearcher _activeDirectorySearcher;
        private readonly IEnvironmentMapper _environmentMapper;

        public RefDataEnvironmentsUsersController(IManageUsers manageUsers, ISecurityPrivilegesChecker securityService,
            IEnvironmentsPersistentSource environmentsPersistentSource,
            IActiveDirectorySearcher activeDirectorySearcher, IEnvironmentMapper environmentMapper)
        {
            _environmentMapper = environmentMapper;
            _activeDirectorySearcher = activeDirectorySearcher;
            _environmentsPersistentSource = environmentsPersistentSource;
            _securityService = securityService;
            _manageUsers = manageUsers;
        }

        /// <summary>
        ///     Get users for environment id, it type=="Endur" gets Endur users
        /// </summary>
        /// <param name="id">Environment ID</param>
        /// <param name="type">"Endur" db type</param>
        /// <returns>Json string with array of  UserApiModel</returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(IEnumerable<UserApiModel>))]
        [HttpGet]
        public IActionResult Get([FromQuery] int id, [FromQuery] UserAccountType type)
        {
            var users = _manageUsers.GetUsersForEnvironment(id, type);
            return StatusCode(StatusCodes.Status200OK, users);
        }

        /// <summary>
        ///     Get users for Environment
        /// </summary>
        /// <param name="id">Environment ID</param>
        /// <returns>Json string with array of UserApiModel</returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(IEnumerable<UserApiModel>))]
        [HttpGet("{id}")]
        public IActionResult Get(int id)
        {
            var users = _manageUsers.GetUsersForEnvironment(id, UserAccountType.NotSet);
            return StatusCode(StatusCodes.Status200OK, users);
        }

        /// <summary>
        /// Get the owner of the environment
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(EnvironmentOwnerApiModel))]
        [Route("owner/{id:int}")]
        [HttpGet]
        public IActionResult GetOwner(int id)
        {
            // DO NOT use the USERS table here!
            var owner = _environmentsPersistentSource.GetEnvironmentOwner(id);

            var userIdActiveDirectory = _activeDirectorySearcher.GetUserIdActiveDirectory(owner);

            return StatusCode(StatusCodes.Status200OK, new EnvironmentOwnerApiModel { DisplayName = userIdActiveDirectory.DisplayName });
        }

        /// <summary>
        /// Update the owner of the environment
        /// </summary>
        /// <param name="id"></param>
        /// <param name="newOwner"></param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(bool))]
        [Route("owner/{id:int}")]
        [HttpPut]
        public IActionResult UpdateOwner(int id, [FromBody] EnvironmentOwnerApiModel newOwner)
        {
            var environment = _environmentsPersistentSource.GetEnvironment(id, User);
            if (environment == null || !_securityService.IsEnvironmentOwnerOrAdmin(User, environment.EnvironmentName))
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    new NonEnoughRightsException("User doesn't have \"Modify\" permission for this action!"));
            }

            var userIdActiveDirectory = _activeDirectorySearcher.GetUserIdActiveDirectory(newOwner.DisplayName);

            var result = _environmentsPersistentSource.SetEnvironmentOwner(User, id, userIdActiveDirectory);
            return StatusCode(StatusCodes.Status200OK, result);
        }

        /// <summary>
        /// Search users or groups in identity provider
        /// </summary>
        /// <param name="search"></param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(List<ActiveDirectoryElementApiModel>))]
        [HttpGet("SearchUsers/{search}")]
        public IActionResult SearchUsers(string search)
        {
            var results = _activeDirectorySearcher.Search(search);

            return StatusCode(StatusCodes.Status200OK, results);
        }
    }
}