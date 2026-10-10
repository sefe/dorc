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
    public sealed class ContainerAuditController : ControllerBase
    {
        private readonly IContainerAuditPersistentSource _containerAuditPersistentSource;

        public ContainerAuditController(IContainerAuditPersistentSource containerAuditPersistentSource)
        {
            _containerAuditPersistentSource = containerAuditPersistentSource;
        }

        /// <summary>
        /// Get paged audit list. Returns audit history for a single container when <paramref name="containerId"/>
        /// is supplied, otherwise returns the cross-record feed across all containers.
        /// Uses PUT (matching the existing DaemonAuditController convention) so a filter/sort body can accompany the request.
        /// </summary>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(GetComponentAuditListResponseDto))]
        [HttpPut]
        public IActionResult Put([FromBody] PagedDataOperators operators, int? containerId = null, int page = 1, int limit = 50)
        {
            var result = containerId.HasValue
                ? _containerAuditPersistentSource.GetContainerAuditByContainerId(containerId.Value, limit, page, operators)
                : _containerAuditPersistentSource.GetContainerAudit(limit, page, operators);
            return StatusCode(StatusCodes.Status200OK, result);
        }
    }
}
