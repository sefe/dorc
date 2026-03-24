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
    public sealed class ServerDaemonsController : ControllerBase
    {
        private readonly IDaemonsPersistentSource _daemonsPersistentSource;

        public ServerDaemonsController(IDaemonsPersistentSource daemonsPersistentSource) =>
            _daemonsPersistentSource = daemonsPersistentSource;

        /// <summary>
        /// Get daemons mapped to a specific server
        /// </summary>
        /// <param name="serverId">Server ID</param>
        /// <returns></returns>
        [HttpGet("{serverId:int}")]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(List<DaemonApiModel>))]
        public IActionResult Get(int serverId)
        {
            var daemons = _daemonsPersistentSource.GetDaemonsForServer(serverId).ToList();
            return Ok(daemons);
        }

        /// <summary>
        /// Attach a daemon to a server
        /// </summary>
        /// <param name="serverId">Server ID</param>
        /// <param name="daemonId">Daemon ID</param>
        /// <returns></returns>
        [HttpPost]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(bool))]
        [SwaggerResponse(StatusCodes.Status404NotFound)]
        public IActionResult Attach(int serverId, int daemonId)
        {
            var result = _daemonsPersistentSource.AttachDaemonToServer(serverId, daemonId);
            return result
                ? Ok(true)
                : NotFound($"Server {serverId} or Daemon {daemonId} not found.");
        }

        /// <summary>
        /// Detach a daemon from a server
        /// </summary>
        /// <param name="serverId">Server ID</param>
        /// <param name="daemonId">Daemon ID</param>
        /// <returns></returns>
        [HttpDelete]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(bool))]
        [SwaggerResponse(StatusCodes.Status404NotFound)]
        public IActionResult Detach(int serverId, int daemonId)
        {
            var result = _daemonsPersistentSource.DetachDaemonFromServer(serverId, daemonId);
            return result
                ? Ok(true)
                : NotFound($"Server {serverId} or Daemon {daemonId} not found.");
        }
    }
}