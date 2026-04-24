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
        private readonly IRolePrivilegesChecker _rolePrivilegesChecker;
        private readonly IClaimsPrincipalReader _claimsPrincipalReader;

        public ServerDaemonsController(
            IDaemonsPersistentSource daemonsPersistentSource,
            IDaemonAuditPersistentSource daemonAuditPersistentSource,
            IRolePrivilegesChecker rolePrivilegesChecker,
            IClaimsPrincipalReader claimsPrincipalReader)
        {
            _daemonsPersistentSource = daemonsPersistentSource;
            _daemonAuditPersistentSource = daemonAuditPersistentSource;
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
                _daemonAuditPersistentSource.InsertDaemonAudit(
                    _claimsPrincipalReader.GetUserFullDomainName(User),
                    ActionType.Attach,
                    daemonId,
                    fromValue: null,
                    toValue: JsonSerializer.Serialize(new { ServerId = serverId, DaemonId = daemonId }));
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

            _daemonAuditPersistentSource.InsertDaemonAudit(
                _claimsPrincipalReader.GetUserFullDomainName(User),
                ActionType.Detach,
                daemonId,
                fromValue: JsonSerializer.Serialize(new { ServerId = serverId, DaemonId = daemonId }),
                toValue: null);

            return Ok(true);
        }
    }
}
