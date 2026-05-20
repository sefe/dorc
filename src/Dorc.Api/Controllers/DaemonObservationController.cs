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
    public sealed class DaemonObservationController : ControllerBase
    {
        private readonly IDaemonObservationPersistentSource _daemonObservationPersistentSource;

        public DaemonObservationController(IDaemonObservationPersistentSource daemonObservationPersistentSource)
        {
            _daemonObservationPersistentSource = daemonObservationPersistentSource;
        }

        /// <summary>
        /// Get paged observation history for a daemon (optionally filtered by server).
        /// </summary>
        [HttpGet]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(GetDaemonObservationListResponseDto))]
        public IActionResult Get(int daemonId, int? serverId = null, int page = 1, int limit = 50)
        {
            var result = _daemonObservationPersistentSource.GetObservations(daemonId, serverId, limit, page);
            return StatusCode(StatusCodes.Status200OK, result);
        }
    }
}
