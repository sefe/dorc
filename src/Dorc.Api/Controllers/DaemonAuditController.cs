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
    public sealed class DaemonAuditController : ControllerBase
    {
        private readonly IDaemonAuditPersistentSource _daemonAuditPersistentSource;

        public DaemonAuditController(IDaemonAuditPersistentSource daemonAuditPersistentSource)
        {
            _daemonAuditPersistentSource = daemonAuditPersistentSource;
        }

        /// <summary>
        /// Get paged audit list for a daemon.
        /// Uses PUT (matching the existing RefDataProjectAuditController convention) so a filter/sort body can accompany the request.
        /// </summary>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(GetDaemonAuditListResponseDto))]
        [HttpPut]
        public IActionResult Put(int daemonId, [FromBody] PagedDataOperators operators, int page = 1, int limit = 50)
        {
            var result = _daemonAuditPersistentSource.GetDaemonAuditByDaemonId(daemonId, limit, page, operators);
            return StatusCode(StatusCodes.Status200OK, result);
        }
    }
}
