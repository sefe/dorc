using Dorc.Api.Interfaces;
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
        private readonly IWindowsWorkerClient _windowsWorkerClient;

        public RefDataAppServersController(IServersPersistentSource serversPersistentSource,
            IWindowsWorkerClient windowsWorkerClient)
        {
            _serversPersistentSource = serversPersistentSource;
            _windowsWorkerClient = windowsWorkerClient;
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
        [SwaggerResponse(StatusCodes.Status503ServiceUnavailable, Type = typeof(string))]
        [HttpPut]
        public async Task<IActionResult> PutServerReboot(string server)
        {
            // S-005: the WMI reboot moved to the Windows worker (WmiUtil now lives there).
            if (!string.IsNullOrWhiteSpace(server))
            {
                await _windowsWorkerClient.RebootServerAsync(server);
            }

            return StatusCode(StatusCodes.Status200OK, server);
        }

    }
}