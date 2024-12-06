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
    public class RefDataSqlPortsController : ControllerBase
    {
        private readonly ISqlPortsPersistentSource _sqlPortsPersistentSource;

        public RefDataSqlPortsController(ISqlPortsPersistentSource sqlPortsPersistentSource)
        {
            _sqlPortsPersistentSource = sqlPortsPersistentSource;
        }

        /// <summary>
        ///     Gets list of Permissions
        /// </summary>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(IEnumerable<SqlPortApiModel>))]
        [HttpGet]
        public IActionResult Get()
        {
            var result = _sqlPortsPersistentSource.GetSqlPorts();
            return StatusCode(StatusCodes.Status200OK, result);
        }

        /// <summary>
        /// Create new SQL Port entry
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        /// <exception cref="UnauthorizedAccessException"></exception>
        [HttpPost]
        public IActionResult Post([FromBody] PermissionDto value)
        {
            if (!User.IsInRole("Admin"))
                throw new UnauthorizedAccessException("User must be part of the 'Admin' group to create new Permissions");

            return StatusCode(StatusCodes.Status501NotImplemented);
        }

        /// <summary>
        /// Edit SQL Port entry
        /// </summary>
        /// <param name="id"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        /// <exception cref="UnauthorizedAccessException"></exception>
        [HttpPut]
        public IActionResult Put(int id, [FromBody] PermissionDto value)
        {
            if (!User.IsInRole("Admin"))
                throw new UnauthorizedAccessException("User must be part of the 'Admin' group to edit Permissions");

            return StatusCode(StatusCodes.Status501NotImplemented);
        }

        /// <summary>
        /// Delete SQL Port entry
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        /// <exception cref="UnauthorizedAccessException"></exception>
        [HttpDelete]
        public IActionResult Delete(int id)
        {
            if (!User.IsInRole("Admin"))
                throw new UnauthorizedAccessException("User must be part of the 'Admin' group to delete Permissions");

            return StatusCode(StatusCodes.Status501NotImplemented);
        }
    }
}