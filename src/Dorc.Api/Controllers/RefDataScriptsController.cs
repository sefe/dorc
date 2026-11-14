using Dorc.ApiModel;
using Dorc.PersistentData;
using Dorc.PersistentData.Sources.Interfaces;
using Dorc.PersistentData.Sources;
using Dorc.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
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
        private readonly ILogger<RefDataScriptsController> _logger;

        public RefDataScriptsController(IScriptsPersistentSource scriptsPersistentSource, IRolePrivilegesChecker rolePrivilegesChecker,
            IDeploymentDefinitions definitions = null, ISecurityPrivilegesChecker security = null, IClaimsPrincipalReader claims = null,
            ILogger<RefDataScriptsController> logger = null)
        {
            _rolePrivilegesChecker = rolePrivilegesChecker;
            _scriptsPersistentSource = scriptsPersistentSource;
            _definitions = definitions;
            _security = security;
            _claims = claims;
            _logger = logger;
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

        /// <summary>
        /// Record, or clear, the content hash a script's decoded UTF-8 text is verified against.
        ///
        /// This is the route a gated promotion pipeline calls after promoting a new version of a
        /// script: without it, every legitimate promotion would look like tampering at the next
        /// deployment. Sending no hash clears the recorded one, which means the next execution
        /// records what it finds — the way to re-baseline a script whose new hash the caller does
        /// not know.
        ///
        /// Recording a hash requires either Admin privileges or write/owner permissions across
        /// EVERY project referencing that script (so a single-project owner cannot overwrite a
        /// baseline shared with another team's project). Clearing an existing baseline to null
        /// requires Admin privileges.
        /// </summary>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(bool))]
        [SwaggerResponse(StatusCodes.Status400BadRequest, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status403Forbidden, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status409Conflict, Type = typeof(string))]
        [HttpPut("{id}/content-hash")]
        public IActionResult PutContentHash(int id, [FromBody] ScriptContentHashApiModel? contentHash)
        {
            var requestedHash = contentHash?.ContentHash;
            var expectedPath = contentHash?.ExpectedPath;
            var isClearing = string.IsNullOrWhiteSpace(requestedHash);
            var isAdmin = _rolePrivilegesChecker.IsAdmin(User);
            var actor = _claims?.GetUserFullDomainName(User) ?? User?.Identity?.Name ?? "unknown";

            if (isClearing && !isAdmin)
            {
                _logger?.LogWarning(
                    "Refused attempt by {Actor} to clear recorded content hash on script #{ScriptId}: Admin role required.",
                    LogText.SingleLine(actor),
                    id);
                return StatusCode(StatusCodes.Status403Forbidden,
                    "Clearing a recorded script content hash requires Admin privileges!");
            }

            if (!isAdmin && _security == null)
            {
                _logger?.LogWarning(
                    "Refused attempt by {Actor} to record content hash {ContentHash} on script #{ScriptId}: caller lacks write/owner rights across all referencing projects.",
                    LogText.SingleLine(actor),
                    LogText.SingleLine(requestedHash ?? string.Empty),
                    id);
                return StatusCode(StatusCodes.Status403Forbidden,
                    "The script content hash can only be recorded by Admins or users with write/owner rights across all projects referencing the script!");
            }

            if (!ScriptContentHash.TryNormalize(requestedHash, out var normalizedHash))
            {
                _logger?.LogWarning(
                    "Rejected malformed content hash {ContentHash} from {Actor} for script #{ScriptId}.",
                    LogText.SingleLine(requestedHash),
                    LogText.SingleLine(actor),
                    id);
                return StatusCode(StatusCodes.Status400BadRequest,
                    "The script must exist in DOrc and the hash, if given, must be 64 hexadecimal"
                    + " characters (SHA-256).");
            }

            bool recorded;
            try
            {
                recorded = _scriptsPersistentSource.RecordContentHash(
                    id,
                    normalizedHash,
                    expectedPath,
                    User,
                    isAdmin ? null : projectId => _security!.CanModifyProject(User, projectId));
            }
            catch (UnauthorizedAccessException)
            {
                _logger?.LogWarning(
                    "Refused attempt by {Actor} to record content hash {ContentHash} on script #{ScriptId}: caller lacks write/owner rights across all referencing projects.",
                    LogText.SingleLine(actor),
                    LogText.SingleLine(requestedHash ?? string.Empty),
                    id);
                return StatusCode(StatusCodes.Status403Forbidden,
                    "The script content hash can only be recorded by Admins or users with write/owner rights across all projects referencing the script!");
            }
            catch (InvalidOperationException ex)
            {
                _logger?.LogWarning(
                    "Conflicting expected path {ExpectedPath} when {Actor} attempted to record content hash on script #{ScriptId}.",
                    LogText.SingleLine(expectedPath ?? string.Empty),
                    LogText.SingleLine(actor),
                    id);
                return StatusCode(StatusCodes.Status409Conflict, ex.Message);
            }

            if (!recorded)
            {
                _logger?.LogWarning(
                    "Rejected content hash update by {Actor} for nonexistent script #{ScriptId}.",
                    LogText.SingleLine(actor),
                    id);
                return StatusCode(StatusCodes.Status400BadRequest,
                    "The script must exist in DOrc and the hash, if given, must be 64 hexadecimal"
                    + " characters (SHA-256).");
            }

            _logger?.LogInformation(
                "Updated content hash baseline on script #{ScriptId} to {ContentHash} by {Actor}.",
                id,
                LogText.SingleLine(normalizedHash ?? "(cleared)"),
                LogText.SingleLine(actor));

            return StatusCode(StatusCodes.Status200OK, true);
        }
    }
}
