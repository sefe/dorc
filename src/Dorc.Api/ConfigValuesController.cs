using Dorc.PersistentData;
using Dorc.PersistentData.Sources;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Dorc.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class ConfigValuesController : ControllerBase
    {
        private readonly IConfigValuesPersistentSource _configValuesPersistentSource;
        private readonly IRolePrivilegesChecker _rolePrivilegesChecker;

        public ConfigValuesController(
            IConfigValuesPersistentSource configValuesPersistentSource,
            IRolePrivilegesChecker rolePrivilegesChecker)
        {
            _configValuesPersistentSource = configValuesPersistentSource;
            _rolePrivilegesChecker = rolePrivilegesChecker;
        }

        /// <summary>
        /// Get a single non-secure configuration value by name. Only administrators may read
        /// configuration values through this endpoint, and secure (secret) values are never
        /// returned here regardless of role.
        /// </summary>
        /// <param name="name">The name of the configuration value to retrieve</param>
        [HttpGet]
        public IActionResult GetConfigValue([FromQuery] string name)
        {
            // The caller is authenticated (authorized) but only administrators have permission
            // to read configuration values through this endpoint.
            if (!_rolePrivilegesChecker.IsAdmin(User))
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    "You do not have permission to read configuration values; this requires the Admin role.");
            }

            string? value;
            try
            {
                value = _configValuesPersistentSource.GetNonSecureConfigValue(name);
            }
            catch (SecureConfigValueRequestedException ex)
            {
                // Secure values are never exposed through this endpoint.
                return StatusCode(StatusCodes.Status403Forbidden, ex.Message);
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                return NotFound($"Config value '{name}' not found");
            }
            return Ok(value);
        }
    }
}
