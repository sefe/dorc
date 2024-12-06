using Dorc.Api.Interfaces;
using Dorc.ApiModel;
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
        private readonly IEnvironmentMapper _environmentMapper;

        public RefDataDatabaseUsersController(IManageUsers manageUsers, IEnvironmentMapper environmentMapper)
        {
            _environmentMapper = environmentMapper;
            _manageUsers = manageUsers;
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
    }
}