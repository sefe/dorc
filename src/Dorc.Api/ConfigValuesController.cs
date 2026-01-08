using Dorc.Api.Interfaces;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dorc.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class ConfigValuesController : ControllerBase
    {
        private readonly IConfigValuesPersistentSource _configValuesPersistentSource;
        public ConfigValuesController(IConfigValuesPersistentSource configValuesPersistentSource)
        {
            _configValuesPersistentSource = configValuesPersistentSource;
        }

        /// <summary>
        /// Get configuration values
        /// </summary>
        /// <param name="name">The name of the configuration value to retrieve</param>
        [HttpGet("{name}")]
        public IActionResult GetConfigValue([FromQuery] string name)
        {
            var value = _configValuesPersistentSource.GetConfigValue(name);
            if (string.IsNullOrWhiteSpace(value))
            {
                return NotFound($"Config value '{name}' not found");
            }
            return Ok(value);
        }
    }
}