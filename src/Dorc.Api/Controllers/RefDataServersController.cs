using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Win32;
using Swashbuckle.AspNetCore.Annotations;
using System.Net;
using System.Text.Json;

namespace Dorc.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class RefDataServersController : ControllerBase
    {
        private readonly ISecurityPrivilegesChecker _securityPrivilegesChecker;
        private readonly IServersPersistentSource _serversPersistentSource;
        private readonly IServersAuditPersistentSource _serversAuditPersistentSource;
        private readonly IEnvironmentsPersistentSource _environmentsPersistentSource;
        private readonly IDaemonsPersistentSource _daemonsPersistentSource;
        private readonly IClaimsPrincipalReader _claimsPrincipalReader;

        public RefDataServersController(
            ISecurityPrivilegesChecker securityPrivilegesChecker,
            IServersPersistentSource serversPersistentSource,
            IServersAuditPersistentSource serversAuditPersistentSource,
            IEnvironmentsPersistentSource environmentsPersistentSource,
            IDaemonsPersistentSource daemonsPersistentSource,
            IClaimsPrincipalReader claimsPrincipalReader)
        {
            _environmentsPersistentSource = environmentsPersistentSource;
            _serversPersistentSource = serversPersistentSource;
            _serversAuditPersistentSource = serversAuditPersistentSource;
            _securityPrivilegesChecker = securityPrivilegesChecker;
            _daemonsPersistentSource = daemonsPersistentSource;
            _claimsPrincipalReader = claimsPrincipalReader;
        }

        /// <summary>
        ///     Returns server detail
        /// </summary>
        /// <param name="server">Server Name</param>
        /// <returns>json object</returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(ServerApiModel))]
        [HttpGet]
        [Route("{server}")]
        public IActionResult Get(string server)
        {
            if (string.IsNullOrEmpty(server))
            {
                return BadRequest("No Server name requested!");
            }

            var srv = _serversPersistentSource.GetServer(server, User);
            return Ok(srv);
        }

        /// <summary>
        ///     Returns server detail
        /// </summary>
        /// <param name="server">Server NAme</param>
        /// <returns>json object</returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(List<ServerApiModel>))]
        [HttpGet]
        [Route("GetAll")]
        public IActionResult GetAll()
        {
            var srv = _serversPersistentSource.GetServers(User);
            return Ok(srv);
        }

        /// <summary>
        /// Get server by id
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(ServerApiModel))]
        [SwaggerResponse(StatusCodes.Status404NotFound, Type = typeof(string))]
        [HttpGet]
        [Route("ById/{id}")]
        public IActionResult Get(int id)
        {
            var srv = _serversPersistentSource.GetServer(id, User);
            if (srv != null && srv.ServerId > 0)
            {
                return Ok(srv);
            }

            return NotFound($"Server not found");
        }

        /// <summary>
        ///     Returns app servers for an environment by environment name, filtered to those with "appserv" in Tags
        /// </summary>
        /// <param name="envName">Environment name</param>
        /// <returns>List of ServerApiModel</returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(List<ServerApiModel>))]
        [HttpGet]
        [Route("AppServersByEnvName")]
        public IActionResult GetAppServersByEnvName([FromQuery] string envName)
        {
            var servers = _serversPersistentSource.GetAppServerDetails(envName);
            var result = servers.Select(s => new ServerApiModel
            {
                ServerId = s.Id,
                Name = s.Name ?? string.Empty,
                OsName = s.OsName ?? string.Empty,
                Tags = s.TagLinks != null ? s.TagLinks.Select(t => t.Tag).ToArray() : System.Array.Empty<string>()
            }).ToList();
            return Ok(result);
        }

        /// <summary>
        /// Edit server entry
        /// </summary>
        /// <param name="id"></param>
        /// <param name="server"></param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status404NotFound, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status400BadRequest, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status403Forbidden, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(ServerApiModel))]
        [HttpPut]
        public IActionResult Put([FromQuery] int id, [FromBody] ServerApiModel server)
        {
            var environmentIdsForServerName = _serversPersistentSource.GetEnvironmentNamesForServerId(server.ServerId);
            foreach (var i in environmentIdsForServerName)
            {
                var env = _environmentsPersistentSource.GetEnvironment(i, User);
                if (env == null)
                {
                    return BadRequest(
                       "Error while checking permissions, probably Environment missing in Deployment database");
                }
                if (!_securityPrivilegesChecker.CanModifyEnvironment(User, env.EnvironmentName))
                {
                    return StatusCode((int)HttpStatusCode.Forbidden, $"You should have write permission on " + env.EnvironmentName + " to modify this server");
                }
            }

            if (id != server.ServerId)
                return BadRequest("'id' must be the same as server.ServerId");

            if (id <= 0)
                return BadRequest("'id' cannot be 0");

            var serverApiModel = _serversPersistentSource.GetServer(server.Name, User);
            if (serverApiModel != null && serverApiModel.ServerId != id)
                return BadRequest("Cannot set the server name to the same as one that already exists!");

            // Capture before-state for the audit row
            var beforeServer = _serversPersistentSource.GetServer(id, User);
            var beforeJson = beforeServer != null
                ? JsonSerializer.Serialize(beforeServer, new JsonSerializerOptions { WriteIndented = true })
                : null;

            var result = _serversPersistentSource.UpdateServer(id, server, User);
            if (result == null)
                return NotFound("Error updating entry");

            var afterServer = _serversPersistentSource.GetServer(id, User);

            var afterJson = JsonSerializer.Serialize(afterServer, new JsonSerializerOptions { WriteIndented = true });

            _serversAuditPersistentSource.InsertServerAudit(
                _claimsPrincipalReader.GetUserFullDomainName(User),
                ActionType.Update,
                id,
                fromValue: beforeJson,
                toValue: afterJson);

            return Ok(result);
        }

        /// <summary>
        /// Get server operating system from target
        /// </summary>
        /// <param name="serverName"></param>
        /// <returns></returns>
        [HttpGet]
        [SwaggerResponse(StatusCodes.Status400BadRequest, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(ServerOperatingSystemApiModel))]
        [Route("GetServerOperatingFromTarget")]
        public IActionResult GetServerOperatingFromTarget(string serverName)
        {
            var output = new ServerOperatingSystemApiModel();
            using (var reg = RegistryKey.OpenRemoteBaseKey(RegistryHive.LocalMachine, serverName))
            using (var key = reg.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\"))
            {
                if (key == null)
                    return BadRequest("Unable to open the target machine");

                output.ProductName = key.GetValue("ProductName").ToString();
                output.CurrentVersion = key.GetValue("CurrentVersion").ToString();
            }
            return Ok(output);
        }

        /// <summary>
        /// Get Server by page
        /// </summary>
        /// <param name="operators"></param>
        /// <param name="page"></param>
        /// <param name="limit"></param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(GetServerApiModelListResponseDto))]
        [HttpPut]
        [Route("ByPage")]
        public IActionResult Put([FromBody] PagedDataOperators operators, int page = 1, int limit = 50)
        {
            var requestStatusesListResponseDto = _serversPersistentSource.GetServerApiModelByPage(limit,
                page, operators, User);

            return Ok(requestStatusesListResponseDto);
        }

        /// <summary>
        ///     Add new server to Environment 
        /// </summary>
        /// <param name="value">json object from request body</param>
        /// <returns>json with created object</returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(ServerApiModel))]
        [HttpPost]
        public IActionResult Post([FromBody] ServerApiModel value)
        {
            var serverApiModel = _serversPersistentSource.GetServer(value.Name, User);
            if (serverApiModel != null)
                return BadRequest(
                    $"A server with the name {value.Name} already exists!");
            var response = _serversPersistentSource.AddServer(value, User);

            _serversAuditPersistentSource.InsertServerAudit(
                _claimsPrincipalReader.GetUserFullDomainName(User),
                ActionType.Create,
                response.ServerId,
                fromValue: null,
                toValue: JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true }));

            return Ok(response);
        }

        /// <summary>
        ///     Delete Server entry
        ///     If daemons are linked to the server and <paramref name="confirmed"/> is false,
        ///     a warning is returned and nothing is deleted. When <paramref name="confirmed"/>
        ///     is true, all daemon links are removed first, then the server is deleted.
        /// </summary>
        /// <param name="serverId"></param>
        /// <param name="confirmed">Set to true to confirm deletion despite linked daemons</param>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(ApiBoolResult))]
        [HttpDelete]
        public ApiBoolResult Delete(int serverId, bool confirmed = false)
        {
            var environmentNamesForServerName = _serversPersistentSource.GetEnvironmentNamesForServerId(serverId);

            foreach (var environmentName in environmentNamesForServerName)
            {
                var environmentApiModel = _environmentsPersistentSource.GetEnvironment(environmentName);
                if (environmentApiModel == null || !_securityPrivilegesChecker.CanModifyEnvironment(User, environmentApiModel.EnvironmentName))
                    return new ApiBoolResult
                    { Result = false, Message = "User doesn't have \"Write\" permission for this action on " + environmentApiModel?.EnvironmentName + "!" };
            }

            // Check for daemons linked to this server
            var linkedDaemons = _daemonsPersistentSource.GetDaemonsForServer(serverId).ToList();
            if (linkedDaemons.Any() && !confirmed)
            {
                var daemonNames = string.Join(", ", linkedDaemons.Select(d => d.Name));
                return new ApiBoolResult
                {
                    Result = false,
                    RequiresConfirmation = true,
                    Message = $"The server has {linkedDaemons.Count} linked daemon(s): {daemonNames}. " +
                              "Confirm deletion to remove the links and delete the server."
                };
            }

            // Capture before-state for the audit row before deleting
            var beforeServer = _serversPersistentSource.GetServer(serverId, User);
            if (beforeServer == null)
                return new ApiBoolResult { Result = false, Message = $"Server {serverId} not found." };
            var beforeJson = JsonSerializer.Serialize(beforeServer, new JsonSerializerOptions { WriteIndented = true });

            var username = _claimsPrincipalReader.GetUserFullDomainName(User);

            // Deletion confirmed: detach all linked daemons first
            foreach (var daemon in linkedDaemons)
            {
                if (!_daemonsPersistentSource.DetachDaemonFromServer(serverId, daemon.Id))
                {
                    return new ApiBoolResult
                    {
                        Result = false,
                        Message = $"Failed to detach daemon '{daemon.Name}' (id {daemon.Id}) from server {serverId}. Server was not deleted."
                    };
                }

                _serversAuditPersistentSource.InsertServerAudit(
                    username,
                    ActionType.Detach,
                    serverId,
                    fromValue: JsonSerializer.Serialize(new { ServerId = serverId, DaemonId = daemon.Id }),
                    toValue: null);
            }

            var result = _serversPersistentSource.DeleteServer(serverId);

            if (result)
            {
                _serversAuditPersistentSource.InsertServerAudit(
                    _claimsPrincipalReader.GetUserFullDomainName(User),
                    ActionType.Delete,
                    serverId,
                    fromValue: beforeJson,
                    toValue: null);
            }

            return new ApiBoolResult { Result = result };
        }
    }
}