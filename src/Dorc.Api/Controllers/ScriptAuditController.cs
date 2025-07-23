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
    public sealed class ScriptAuditController : ControllerBase
    {
        private readonly IScriptAuditPersistentSource _scriptAuditPersistentSource;

        public ScriptAuditController(IScriptAuditPersistentSource scriptAuditPersistentSource)
        {
            _scriptAuditPersistentSource = scriptAuditPersistentSource;
        }

        /// <summary>
        /// Get script audit list
        /// </summary>
        /// <param name="scriptId"></param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(GetScriptAuditListResponseDto))]
        [HttpPut]
        public IActionResult Put(int scriptId, [FromBody] PagedDataOperators operators, int page = 1, int limit = 50)
        {
            var scriptAuditDto = _scriptAuditPersistentSource.GetScriptAuditByScriptId(scriptId, limit,
                page, operators);

            return StatusCode(StatusCodes.Status200OK, scriptAuditDto);
        }
    }
}