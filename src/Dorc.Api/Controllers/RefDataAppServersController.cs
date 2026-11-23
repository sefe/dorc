using Dorc.Api.Services;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Dorc.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class RefDataAppServersController : ControllerBase
    {
        private readonly IServersPersistentSource _serversPersistentSource;
        private readonly ISecurityPrivilegesChecker? _securityPrivilegesChecker;
        private readonly IRolePrivilegesChecker? _rolePrivilegesChecker;

        public RefDataAppServersController(
            IServersPersistentSource serversPersistentSource,
            ISecurityPrivilegesChecker? securityPrivilegesChecker = null,
            IRolePrivilegesChecker? rolePrivilegesChecker = null)
        {
            _serversPersistentSource = serversPersistentSource;
            _securityPrivilegesChecker = securityPrivilegesChecker;
            _rolePrivilegesChecker = rolePrivilegesChecker;
        }

        /// <summary>
        ///     Get app servers list 
        /// </summary>
        /// <param name="id">Environment ID</param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(IEnumerable<ServerApiModel>))]
        [HttpGet]
        [Route("{id}")]
        public IActionResult Get(int id)
        {
            var servers = _serversPersistentSource.GetEnvContentAppServersForEnvId(id);

            return StatusCode(StatusCodes.Status200OK, servers);
        }


        /// <summary>
        ///     Reboot server using WMI and current credentials
        /// </summary>
        /// <param name="server"></param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status403Forbidden, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status404NotFound, Type = typeof(string))]
        [HttpPut]
        public IActionResult PutServerReboot(string? server = null)
        {
            if (!string.IsNullOrWhiteSpace(server))
            {
                if (_securityPrivilegesChecker != null || _rolePrivilegesChecker != null)
                {
                    var isAdmin = _rolePrivilegesChecker?.IsAdmin(User) ?? false;
                    if (!isAdmin)
                    {
                        var serverModel = _serversPersistentSource.GetServer(server, User);
                        if (serverModel == null)
                        {
                            return NotFound($"Server '{server}' not found.");
                        }

                        var envNames = _serversPersistentSource.GetEnvironmentNamesForServerId(serverModel.ServerId)?.ToList()
                                       ?? new List<string>();
                        if (envNames.Count == 0 ||
                            _securityPrivilegesChecker == null ||
                            envNames.Any(envName => !_securityPrivilegesChecker.CanModifyEnvironment(User, envName)))
                        {
                            return StatusCode(StatusCodes.Status403Forbidden,
                                $"You do not have permission to reboot server '{server}'.");
                        }
                    }
                }

                var wmi = new WmiUtil(server);
                wmi.Reboot();
            }

            return StatusCode(StatusCodes.Status200OK, server);
        }

    }
}