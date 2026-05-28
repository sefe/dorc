using Dorc.ApiModel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Win32;

namespace Dorc.Api.WindowsWorker.Controllers
{
    // Windows-only remote-server probing. Currently exposes registry-based OS
    // detection (S-004 — moved from Dorc.Api/Controllers/RefDataServersController).
    [ApiController]
    [Route("remote-server")]
    public class RemoteServerController : ControllerBase
    {
        private readonly ILogger<RemoteServerController> _logger;

        public RemoteServerController(ILogger<RemoteServerController> logger)
        {
            _logger = logger;
        }

        [HttpGet("operating-system")]
        public ActionResult<ServerOperatingSystemApiModel> GetOperatingSystem([FromQuery] string serverName)
        {
            if (string.IsNullOrWhiteSpace(serverName))
            {
                return BadRequest(new { error = "serverName is required" });
            }

            try
            {
                using var reg = RegistryKey.OpenRemoteBaseKey(RegistryHive.LocalMachine, serverName);
                using var key = reg.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\");
                if (key == null)
                {
                    return BadRequest(new { error = "Unable to open the target machine" });
                }

                return Ok(new ServerOperatingSystemApiModel
                {
                    ProductName = key.GetValue("ProductName")?.ToString() ?? string.Empty,
                    CurrentVersion = key.GetValue("CurrentVersion")?.ToString() ?? string.Empty
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read remote registry for server");
                return BadRequest(new { error = "Failed to read remote registry for server" });
            }
        }
    }
}
