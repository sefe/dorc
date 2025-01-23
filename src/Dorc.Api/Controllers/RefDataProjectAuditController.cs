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
        /// Get project audit list
        /// </summary>
        /// <param name="projectId"></param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(GetRefDataAuditListResponseDto))]
        [HttpPut]
        public IActionResult Put(int projectId, [FromBody] PagedDataOperators operators, int page = 1, int limit = 50)
        {
            var projectAuditDto = _manageProjectsPersistentSource.GetRefDataAuditByProjectId(projectId, limit,
                page, operators);

            return StatusCode(StatusCodes.Status200OK, projectAuditDto);
        }
    }
}
