using Dorc.ApiModel;
using Dorc.PersistentData;
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
        private readonly IRolePrivilegesChecker? _rolePrivilegesChecker;

        public RefDataSqlPortsController(
            ISqlPortsPersistentSource sqlPortsPersistentSource,
            IRolePrivilegesChecker? rolePrivilegesChecker = null)
        {
            _sqlPortsPersistentSource = sqlPortsPersistentSource;
            _rolePrivilegesChecker = rolePrivilegesChecker;
        }

        /// <summary>
        ///     Gets list of SqlPortApiModel
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
        ///     Gets the SQL port for a specific instance
        /// </summary>
        /// <param name="instanceName">SQL Server instance name</param>
        /// <returns>The SQL port string</returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status404NotFound, Type = typeof(string))]
        [HttpGet]
        [Route("ByInstance")]
        public IActionResult GetByInstance([FromQuery] string instanceName)
        {
            var result = _sqlPortsPersistentSource.GetSqlPort(instanceName);
            if (string.IsNullOrEmpty(result))
                return NotFound($"No SQL port found for instance '{instanceName}'.");
            return Ok(result.TrimEnd());
        }

        /// <summary>
        /// Create new SQL Port entry
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        /// <exception cref="UnauthorizedAccessException"></exception>
        [HttpPost]
        public IActionResult Post([FromBody] SqlPortApiModel value)
        {
            var isAdmin = (_rolePrivilegesChecker?.IsAdmin(User) ?? false) || User.IsInRole("Admin");
            if (!isAdmin)
                throw new UnauthorizedAccessException("User must be part of the 'Admin' group to create new SQL Port");
            if (value == null || string.IsNullOrWhiteSpace(value.InstanceName) || string.IsNullOrWhiteSpace(value.SqlPort))
                return StatusCode(StatusCodes.Status400BadRequest, "InstanceName and SqlPort are required.");
            if (value.InstanceName.Length > 250 || value.SqlPort.Length > 10)
                return StatusCode(StatusCodes.Status400BadRequest, "InstanceName cannot exceed 250 characters and SqlPort cannot exceed 10 characters.");

            try
            {
                _sqlPortsPersistentSource.CreateSqlPort(value);

                return StatusCode(StatusCodes.Status200OK);
            }
            catch (ArgumentException ex)
            {
                return StatusCode(StatusCodes.Status400BadRequest, ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return StatusCode(StatusCodes.Status400BadRequest, ex.Message);
            }
        }

        /// <summary>
        /// Delete SQL Port entry
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        /// <exception cref="UnauthorizedAccessException"></exception>
        [HttpDelete]
        public IActionResult Delete([FromBody] SqlPortApiModel value)
        {
            var isAdmin = (_rolePrivilegesChecker?.IsAdmin(User) ?? false) || User.IsInRole("Admin");
            if (!isAdmin)
                throw new UnauthorizedAccessException("User must be part of the 'Admin' group to delete Permissions");

            try
            {
                _sqlPortsPersistentSource.DeleteSqlPort(value);
                return StatusCode(StatusCodes.Status200OK);
            }
            catch (ArgumentException ex)
            {
                return StatusCode(StatusCodes.Status400BadRequest, ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return StatusCode(StatusCodes.Status400BadRequest, ex.Message);
            }
        }
    }
}