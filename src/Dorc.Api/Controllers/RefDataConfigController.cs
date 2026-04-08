using Dorc.ApiModel;
using Dorc.PersistentData;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Dorc.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public sealed class RefDataConfigController : ControllerBase
    {
        private readonly IConfigValuesPersistentSource _configValuesPersistentSource;
        private readonly IRolePrivilegesChecker _rolePrivilegesChecker;

        public RefDataConfigController(IConfigValuesPersistentSource configValuesPersistentSource, IRolePrivilegesChecker rolePrivilegesChecker)
        {
            _rolePrivilegesChecker = rolePrivilegesChecker;
            _configValuesPersistentSource = configValuesPersistentSource;
        }

        /// <summary>
        /// Get all config values
        /// </summary>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(List<ConfigValueApiModel>))]
        [HttpGet]
        public IActionResult Get()
        {
            return !_rolePrivilegesChecker.IsAdmin(User)
                ? Forbid()
                : Ok(_configValuesPersistentSource.GetAllConfigValues(false).ToList());
        }

        /// <summary>
        /// Create new config value
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        [HttpPost]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(ConfigValueApiModel))]
        public IActionResult Post([FromBody] ConfigValueApiModel model)
        {
            if (!_rolePrivilegesChecker.IsAdmin(User))
                return Forbid();
            try
            {
                return Ok(_configValuesPersistentSource.Add(model));
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        /// <summary>
        /// Edit config value
        /// </summary>
        /// <param name="id"></param>
        /// <param name="model"></param>
        /// <returns></returns>
        [HttpPut]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(ConfigValueApiModel))]
        public IActionResult Put(int id, [FromBody] ConfigValueApiModel model)
        {
            if (!_rolePrivilegesChecker.IsAdmin(User))
                return Forbid();
            try
            {
                return Ok(_configValuesPersistentSource.UpdateConfigValue(model));
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        /// <summary>
        /// Delete config value
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(bool))]
        [SwaggerResponse(StatusCodes.Status403Forbidden)]
        [HttpDelete]
        public IActionResult Delete(int id)
        {
            return !_rolePrivilegesChecker.IsAdmin(User)
                ? Forbid()
                : Ok(_configValuesPersistentSource.RemoveConfigValue(id));
        }
    }
}
