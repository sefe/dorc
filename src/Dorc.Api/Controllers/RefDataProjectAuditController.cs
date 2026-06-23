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
    public sealed class RefDataProjectAuditController : ControllerBase
    {
        private readonly IManageProjectsPersistentSource _manageProjectsPersistentSource;

        public RefDataProjectAuditController(IManageProjectsPersistentSource manageProjectsPersistentSource)
        {
            _manageProjectsPersistentSource = manageProjectsPersistentSource;
        }

        /// <summary>
        /// Get project audit list. Returns audit history for a single project when
        /// <paramref name="projectId"/> is supplied, otherwise returns the cross-record feed
        /// across all projects.
        /// </summary>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(GetRefDataAuditListResponseDto))]
        [HttpPut]
        public IActionResult Put([FromBody] PagedDataOperators operators, int? projectId = null, int page = 1, int limit = 50)
        {
            var projectAuditDto = projectId.HasValue
                ? _manageProjectsPersistentSource.GetRefDataAuditByProjectId(projectId.Value, limit, page, operators)
                : _manageProjectsPersistentSource.GetRefDataAudit(limit, page, operators);

            return StatusCode(StatusCodes.Status200OK, projectAuditDto);
        }
    }
}
