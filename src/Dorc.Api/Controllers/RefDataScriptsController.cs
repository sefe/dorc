using Dorc.ApiModel;
using Dorc.PersistentData;
using Dorc.PersistentData.Sources.Interfaces;
using Dorc.PersistentData.Sources;
using Dorc.Core.Interfaces;
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
        private readonly IDeploymentDefinitions _definitions;
        private readonly ISecurityPrivilegesChecker _security;
        private readonly IClaimsPrincipalReader _claims;

        public RefDataScriptsController(IScriptsPersistentSource scriptsPersistentSource, IRolePrivilegesChecker rolePrivilegesChecker,
            IDeploymentDefinitions definitions = null, ISecurityPrivilegesChecker security = null, IClaimsPrincipalReader claims = null)
        {
            _rolePrivilegesChecker = rolePrivilegesChecker;
            _scriptsPersistentSource = scriptsPersistentSource;
            _definitions = definitions;
            _security = security;
            _claims = claims;
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
            foreach (var script in getScriptsListResponseDto.Items)
                script.CanEdit = (_rolePrivilegesChecker.IsPowerUser(User) || _rolePrivilegesChecker.IsAdmin(User))
                    && (script.AffectedProjectIds ?? []).All(id => _security.CanModifyProject(User, id));

            return StatusCode(StatusCodes.Status200OK, getScriptsListResponseDto);
        }

        /// <summary>
        /// Get list of Scripts
        /// </summary>
        /// <param name="script"></param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(bool))]
        [HttpPut("edit")]
        [SwaggerResponse(428, Type = typeof(string))]
        [SwaggerResponse(409, Type = typeof(string))]
        public IActionResult Put(ScriptApiModel script)
        {
            if (!_rolePrivilegesChecker.IsPowerUser(User) && !_rolePrivilegesChecker.IsAdmin(User))
                return StatusCode(StatusCodes.Status403Forbidden,
                    "Scripts can only be edited by PowerUsers or Admins!");

            var scriptApiModel = _scriptsPersistentSource.GetScript(script.Id);

            if (scriptApiModel == null) return BadRequest("Script must already exist in DOrc!");
            try
            {
                return Ok(_definitions.EditScript(script, _claims.GetUserFullDomainName(User),
                    id => _security.CanModifyProject(User, id)));
            }
            catch (DefinitionPreconditionException e) { return StatusCode(e.StatusCode, e.Message); }
            catch (UnauthorizedAccessException) { return Forbid(); }
            catch (ArgumentException e) { return BadRequest(e.Message); }
        }
    }
}
