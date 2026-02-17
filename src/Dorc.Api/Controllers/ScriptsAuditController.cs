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
    public sealed class ScriptsAuditController : ControllerBase
    {
        private readonly IScriptsAuditPersistentSource _scriptsAuditPersistentSource;

        public ScriptsAuditController(IScriptsAuditPersistentSource scriptsAuditPersistentSource)
        {
            _scriptsAuditPersistentSource = scriptsAuditPersistentSource;
        }

        /// <summary>
        /// Get scripts audit list, used for infinite scrolling in the UI
        /// </summary>
        /// <param name="operators"></param>
        /// <param name="page"></param>
        /// <param name="limit"></param>
        /// <param name="useAndLogic"></param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(GetScriptsAuditListResponseDto))]
        [HttpPut]
        public IActionResult Put([FromBody] PagedDataOperators operators, int page = 1, int limit = 50, bool useAndLogic = true)
        {
            var result = _scriptsAuditPersistentSource.GetScriptAuditsByPage(limit, page, operators, useAndLogic);
            return StatusCode(StatusCodes.Status200OK, result);
        }
    }
}