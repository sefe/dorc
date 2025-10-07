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
    public class RefDataPermissionController : ControllerBase
    {
        private readonly IPermissionsPersistentSource _permissionsPersistentSource;

        public RefDataPermissionController(IPermissionsPersistentSource permissionsPersistentSource)
        {
            _permissionsPersistentSource = permissionsPersistentSource;
        }

        /// <summary>
        ///     Gets list of Permissions
        /// </summary>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(IEnumerable<PermissionDto>))]
        [HttpGet]

        public IActionResult Get()
        {
            var result = _permissionsPersistentSource.GetAllPermissions();
            return StatusCode(StatusCodes.Status200OK, result);
        }

        /// <summary>
        /// Create new Permission
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        [HttpPost]
        public IActionResult Post([FromBody] PermissionDto value)
        {
            if (!User.IsInRole("PowerUser") && !User.IsInRole("Admin"))
                return StatusCode(StatusCodes.Status403Forbidden,
                    "User must be part of the 'PowerUser' or 'Admin' group to create new Permissions");

            try
            {
                _permissionsPersistentSource.CreatePermission(value);
            }
            catch (ArgumentException ex)
            {
                return StatusCode(StatusCodes.Status400BadRequest,
                    ex.Message);
            }

            return StatusCode(StatusCodes.Status200OK);
        }

        /// <summary>
        /// Edit Permission
        /// </summary>
        /// <param name="id"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        [HttpPut]
        public IActionResult Put(int id, [FromBody] PermissionDto value)
        {
            if (!User.IsInRole("Admin"))
                return StatusCode(StatusCodes.Status403Forbidden,
                    "User must be part of the 'Admin' group to edit Permissions");
            try 
            { 
                _permissionsPersistentSource.UpdatePermission(id, value);
            }
            catch (ArgumentException ex)
            {
                return StatusCode(StatusCodes.Status400BadRequest,
                    ex.Message);
            }

            return StatusCode(StatusCodes.Status200OK);
        }

        /// <summary>
        /// Delete Permission
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [HttpDelete]
        public IActionResult Delete(int id)
        {
            if (!User.IsInRole("Admin"))
                return StatusCode(StatusCodes.Status403Forbidden,
                    "User must be part of the 'Admin' group to delete Permissions");

            _permissionsPersistentSource.DeletePermission(id);
            return StatusCode(StatusCodes.Status200OK);
        }
    }
}