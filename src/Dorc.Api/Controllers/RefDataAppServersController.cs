using System.Runtime.Versioning;
using Dorc.Api.Services;
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
    public class RefDataAppServersController : ControllerBase
    {
        private readonly IServersPersistentSource _serversPersistentSource;

        public RefDataAppServersController(IServersPersistentSource serversPersistentSource)
        {
            _serversPersistentSource = serversPersistentSource;
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
        [HttpPut]
        [SupportedOSPlatform("windows")]
        public IActionResult PutServerReboot(string server)
        {
            if (!string.IsNullOrWhiteSpace(server))
            {
                var wmi = new WmiUtil(server);
                wmi.Reboot();
            }

            return StatusCode(StatusCodes.Status200OK, server);
        }

    }
}