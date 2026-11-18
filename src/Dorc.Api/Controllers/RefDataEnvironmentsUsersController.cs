using Dorc.Api.Services;
using Dorc.ApiModel;
using Dorc.Core;
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
    public class RefDataEnvironmentsUsersController : ControllerBase
    {
        private readonly ISecurityPrivilegesChecker _securityService;
        private readonly IEnvironmentsPersistentSource _environmentsPersistentSource;
        private readonly IActiveDirectorySearcher _activeDirectorySearcher;

        public RefDataEnvironmentsUsersController(
            ISecurityPrivilegesChecker securityService,
            IEnvironmentsPersistentSource environmentsPersistentSource,
            IActiveDirectorySearcher activeDirectorySearcher)
        {
            _activeDirectorySearcher = activeDirectorySearcher;
            _environmentsPersistentSource = environmentsPersistentSource;
            _securityService = securityService;
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
            var ownerId = _environmentsPersistentSource.GetEnvironmentOwnerId(id);

            var userIdActiveDirectory = _activeDirectorySearcher.GetUserDataById(ownerId);

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

            var userIdActiveDirectory = _activeDirectorySearcher.GetUserData(newOwner.DisplayName);

            var result = _environmentsPersistentSource.SetEnvironmentOwner(User, id, userIdActiveDirectory);
            return StatusCode(StatusCodes.Status200OK, result);
        }

        /// <summary>
        /// Search users or groups in identity provider
        /// </summary>
        /// <param name="search"></param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(List<UserElementApiModel>))]
        [HttpGet("SearchUsers/{search}")]
        public IActionResult SearchUsers(string search)
        {
            var results = _activeDirectorySearcher.Search(search);

            return StatusCode(StatusCodes.Status200OK, results);
        }
    }
}