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
    public sealed class CloudResourceAuditController : ControllerBase
    {
        private readonly ICloudResourceAuditPersistentSource _cloudResourceAuditPersistentSource;

        public CloudResourceAuditController(ICloudResourceAuditPersistentSource cloudResourceAuditPersistentSource)
        {
            _cloudResourceAuditPersistentSource = cloudResourceAuditPersistentSource;
        }

        /// <summary>
        /// Get paged audit list. Returns audit history for a single cloud resource when <paramref name="cloudResourceId"/>
        /// is supplied, otherwise returns the cross-record feed across all cloud resources.
        /// Uses PUT (matching the existing DaemonAuditController convention) so a filter/sort body can accompany the request.
        /// </summary>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(GetComponentAuditListResponseDto))]
        [HttpPut]
        public IActionResult Put([FromBody] PagedDataOperators operators, int? cloudResourceId = null, int page = 1, int limit = 50)
        {
            var result = cloudResourceId.HasValue
                ? _cloudResourceAuditPersistentSource.GetCloudResourceAuditByCloudResourceId(cloudResourceId.Value, limit, page, operators)
                : _cloudResourceAuditPersistentSource.GetCloudResourceAudit(limit, page, operators);
            return StatusCode(StatusCodes.Status200OK, result);
        }
    }
}
