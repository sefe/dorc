using System.Runtime.Versioning;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Win32;
using Swashbuckle.AspNetCore.Annotations;
using System.Net;

namespace Dorc.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class RefDataServersController : ControllerBase
    {
        private readonly ISecurityPrivilegesChecker _securityPrivilegesChecker;
        private readonly IServersPersistentSource _serversPersistentSource;
        private readonly IEnvironmentsPersistentSource _environmentsPersistentSource;

        public RefDataServersController(ISecurityPrivilegesChecker securityPrivilegesChecker,
            IServersPersistentSource serversPersistentSource, IEnvironmentsPersistentSource environmentsPersistentSource)
        {
            _environmentsPersistentSource = environmentsPersistentSource;
            _serversPersistentSource = serversPersistentSource;
            _securityPrivilegesChecker = securityPrivilegesChecker;
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
                       "Error while checking permissions, probably Environment missing in Deployment database" );
                }
                if (!_securityPrivilegesChecker.CanModifyEnvironment(User, env.EnvironmentName))
                {
                    return StatusCode((int)HttpStatusCode.Forbidden, $"You should have write permission on " + env.EnvironmentName + " to modify this server");
                }
            }

            if (id != server.ServerId)
                return BadRequest("'id' must be the same as server.ServerId" );

            if (id <= 0)
                return BadRequest( "'id' cannot be 0" );

            var serverApiModel = _serversPersistentSource.GetServer(server.Name, User);
            if (serverApiModel != null && serverApiModel.ServerId != id)
                return BadRequest("Cannot set the server name to the same as one that already exists!");

            var result = _serversPersistentSource.UpdateServer(id, server, User);
            return result != null
                ? Ok(result)
                : NotFound("Error updating entry" );
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
        [SupportedOSPlatform("windows")]
        public IActionResult GetServerOperatingFromTarget(string serverName)
        {
            var output = new ServerOperatingSystemApiModel();
            using (var reg = RegistryKey.OpenRemoteBaseKey(RegistryHive.LocalMachine, serverName))
            using (var key = reg.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\"))
            {
                if (key == null)
                    return BadRequest("Unable to open the target machine");

                output.ProductName = key.GetValue("ProductName")?.ToString() ?? string.Empty;
                output.CurrentVersion = key.GetValue("CurrentVersion")?.ToString() ?? string.Empty;
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
            return Ok(response);
        }

        /// <summary>
        ///     Delete Server entry
        /// </summary>
        /// <param name="serverName"></param>
        /// <param name="envId"></param>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(ApiBoolResult))]
        [HttpDelete]
        public ApiBoolResult Delete(int serverId)
        {
            var environmentNamesForServerName = _serversPersistentSource.GetEnvironmentNamesForServerId(serverId);

            foreach (var environmentName in environmentNamesForServerName)
            {
                var environmentApiModel = _environmentsPersistentSource.GetEnvironment(environmentName);
                if (environmentApiModel == null || !_securityPrivilegesChecker.CanModifyEnvironment(User, environmentApiModel.EnvironmentName))
                    return new ApiBoolResult
                    { Result = false, Message = "User doesn't have \"Write\" permission for this action on " + environmentApiModel?.EnvironmentName + "!" };
            }

            var result = _serversPersistentSource.DeleteServer(serverId);
            return new ApiBoolResult { Result = result };
        }
    }
}