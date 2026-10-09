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
    public sealed class ApiRegistrationAuditController : ControllerBase
    {
        private readonly IApiRegistrationAuditPersistentSource _apiRegistrationAuditPersistentSource;

        public ApiRegistrationAuditController(IApiRegistrationAuditPersistentSource apiRegistrationAuditPersistentSource)
        {
            _apiRegistrationAuditPersistentSource = apiRegistrationAuditPersistentSource;
        }

        /// <summary>
        /// Get paged audit list. Returns audit history for a single API registration when <paramref name="apiRegistrationId"/>
        /// is supplied, otherwise returns the cross-record feed across all API registrations.
        /// Uses PUT (matching the existing DaemonAuditController convention) so a filter/sort body can accompany the request.
        /// </summary>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(GetComponentAuditListResponseDto))]
        [HttpPut]
        public IActionResult Put([FromBody] PagedDataOperators operators, int? apiRegistrationId = null, int page = 1, int limit = 50)
        {
            var result = apiRegistrationId.HasValue
                ? _apiRegistrationAuditPersistentSource.GetApiRegistrationAuditByApiRegistrationId(apiRegistrationId.Value, limit, page, operators)
                : _apiRegistrationAuditPersistentSource.GetApiRegistrationAudit(limit, page, operators);
            return StatusCode(StatusCodes.Status200OK, result);
        }
    }
}
