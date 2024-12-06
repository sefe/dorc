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
    public sealed class RefDataScriptsController : ControllerBase
    {
        private readonly IScriptsPersistentSource _scriptsPersistentSource;
        private readonly IRolePrivilegesChecker _rolePrivilegesChecker;

        public RefDataScriptsController(IScriptsPersistentSource scriptsPersistentSource, IRolePrivilegesChecker rolePrivilegesChecker)
        {
            _rolePrivilegesChecker = rolePrivilegesChecker;
            _scriptsPersistentSource = scriptsPersistentSource;
        }

        /// <summary>
        /// Get list of Scripts by page
        /// </summary>
        /// <param name="operators"></param>
        /// <param name="page"></param>
        /// <param name="limit"></param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(GetScriptsListResponseDto))]
        [HttpPut]
        public IActionResult Put([FromBody] PagedDataOperators operators, int page = 1, int limit = 50)
        {
            var getScriptsListResponseDto = _scriptsPersistentSource.GetScriptsByPage(limit,
                page, operators);

            return StatusCode(StatusCodes.Status200OK, getScriptsListResponseDto);
        }

        /// <summary>
        /// Get list of Scripts
        /// </summary>
        /// <param name="script"></param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(bool))]
        [HttpPut("edit")]
        public IActionResult Put(ScriptApiModel script)
        {
            if (!_rolePrivilegesChecker.IsPowerUser(User) && !_rolePrivilegesChecker.IsAdmin(User))
                return StatusCode(StatusCodes.Status403Forbidden,
                    "Scripts can only be edited by PowerUsers or Admins!");

            var scriptApiModel = _scriptsPersistentSource.GetScript(script.Id);

            return scriptApiModel == null
                ? StatusCode(StatusCodes.Status400BadRequest, "Script must already exist in DOrc!")
                : StatusCode(StatusCodes.Status200OK, _scriptsPersistentSource.UpdateScript(script, User));
        }
    }
}
