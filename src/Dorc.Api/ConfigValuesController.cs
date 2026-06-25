using Dorc.PersistentData;
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
        /// Get a single non-secure configuration value by name. Restricted to administrators;
        /// secure (secret) values are never returned through this endpoint.
        /// </summary>
        /// <param name="name">The name of the configuration value to retrieve</param>
        [HttpGet]
        public IActionResult GetConfigValue([FromQuery] string name)
        {
            if (!_rolePrivilegesChecker.IsAdmin(User))
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    "You are not authorized to read configuration values.");
            }

            var value = _configValuesPersistentSource.GetNonSecureConfigValue(name);
            if (string.IsNullOrWhiteSpace(value))
            {
                return NotFound($"Config value '{name}' not found");
            }
            return Ok(value);
        }
    }
}
