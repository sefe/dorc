using System.Text.Json;
using Dorc.ApiModel;
using Dorc.PersistentData;
using Dorc.PersistentData.Model;
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
        private readonly IDaemonAuditPersistentSource _daemonAuditPersistentSource;
        private readonly IServersAuditPersistentSource _serversAuditPersistentSource;
        private readonly IRolePrivilegesChecker _rolePrivilegesChecker;
        private readonly IClaimsPrincipalReader _claimsPrincipalReader;

        public ServerDaemonsController(
            IDaemonsPersistentSource daemonsPersistentSource,
            IDaemonAuditPersistentSource daemonAuditPersistentSource,
            IServersAuditPersistentSource serversAuditPersistentSource,
            IRolePrivilegesChecker rolePrivilegesChecker,
            IClaimsPrincipalReader claimsPrincipalReader)
        {
            _daemonsPersistentSource = daemonsPersistentSource;
            _daemonAuditPersistentSource = daemonAuditPersistentSource;
            _serversAuditPersistentSource = serversAuditPersistentSource;
            _rolePrivilegesChecker = rolePrivilegesChecker;
            _claimsPrincipalReader = claimsPrincipalReader;
        }

        /// <summary>
        /// Get daemons mapped to a specific server
        /// </summary>
        [HttpGet("{serverId:int}")]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(List<DaemonApiModel>))]
        public IActionResult Get(int serverId)
        {
            var daemons = _daemonsPersistentSource.GetDaemonsForServer(serverId).ToList();
            return Ok(daemons);
        }

        /// <summary>
        /// Get servers a specific daemon is attached to
        /// </summary>
        [HttpGet("by-daemon/{daemonId:int}")]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(List<ServerApiModel>))]
        public IActionResult GetServersForDaemon(int daemonId)
        {
            var servers = _daemonsPersistentSource.GetServersForDaemon(daemonId).ToList();
            return Ok(servers);
        }

        /// <summary>
        /// Attach a daemon to a server
        /// </summary>
        [HttpPost]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(bool))]
        [SwaggerResponse(StatusCodes.Status403Forbidden, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status404NotFound, Type = typeof(string))]
        public IActionResult Attach(int serverId, int daemonId)
        {
            if (!_rolePrivilegesChecker.IsPowerUser(User) && !_rolePrivilegesChecker.IsAdmin(User))
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    "Daemons can only be attached to servers by PowerUsers or Admins!");
            }

            // Detect no-op attach (mapping already exists) so we don't emit a spurious audit row.
            var alreadyAttached = _daemonsPersistentSource
                .GetDaemonsForServer(serverId)
                .Any(d => d.Id == daemonId);

            if (!_daemonsPersistentSource.AttachDaemonToServer(serverId, daemonId))
            {
                return NotFound($"Server {serverId} or Daemon {daemonId} not found.");
            }

            if (!alreadyAttached)
            {
                var username = _claimsPrincipalReader.GetUserFullDomainName(User);
                var payload = JsonSerializer.Serialize(new { ServerId = serverId, DaemonId = daemonId });

                _daemonAuditPersistentSource.InsertDaemonAudit(
                    username,
                    ActionType.Attach,
                    daemonId,
                    fromValue: null,
                    toValue: payload);

                _serversAuditPersistentSource.InsertServerAudit(
                    username,
                    ActionType.Attach,
                    serverId,
                    fromValue: null,
                    toValue: payload);
            }

            return Ok(true);
        }

        /// <summary>
        /// Detach a daemon from a server
        /// </summary>
        [HttpDelete]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(bool))]
        [SwaggerResponse(StatusCodes.Status403Forbidden, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status404NotFound, Type = typeof(string))]
        public IActionResult Detach(int serverId, int daemonId)
        {
            if (!_rolePrivilegesChecker.IsPowerUser(User) && !_rolePrivilegesChecker.IsAdmin(User))
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    "Daemons can only be detached from servers by PowerUsers or Admins!");
            }

            if (!_daemonsPersistentSource.DetachDaemonFromServer(serverId, daemonId))
            {
                return NotFound($"Server {serverId} or Daemon {daemonId} not found.");
            }

            var username = _claimsPrincipalReader.GetUserFullDomainName(User);
            var payload = JsonSerializer.Serialize(new { ServerId = serverId, DaemonId = daemonId });

            _daemonAuditPersistentSource.InsertDaemonAudit(
                username,
                ActionType.Detach,
                daemonId,
                fromValue: payload,
                toValue: null);

            _serversAuditPersistentSource.InsertServerAudit(
                username,
                ActionType.Detach,
                serverId,
                fromValue: payload,
                toValue: null);

            return Ok(true);
        }
    }
}
