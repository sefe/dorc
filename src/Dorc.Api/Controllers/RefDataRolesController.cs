using Dorc.PersistentData;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Dorc.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class RefDataRolesController : ControllerBase
    {
        private readonly IRolePrivilegesChecker _rolePrivilegesChecker;

        public RefDataRolesController(IRolePrivilegesChecker rolePrivilegesChecker)
        {
            _rolePrivilegesChecker = rolePrivilegesChecker;
        }

        /// <summary>
        /// Get list of Roles
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(IEnumerable<string>))]
        public IEnumerable<string> Get()
        {
            return _rolePrivilegesChecker.GetRoles(User);
        }
    }
}