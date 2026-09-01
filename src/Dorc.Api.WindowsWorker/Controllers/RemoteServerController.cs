using Dorc.Api.WindowsWorker.Services;
using Dorc.ApiModel;
using Microsoft.AspNetCore.Mvc;

namespace Dorc.Api.WindowsWorker.Controllers
{
    // Windows-only remote-server probing. Currently exposes registry-based OS
    // detection (S-004 — moved from Dorc.Api/Controllers/RefDataServersController).
    [ApiController]
    [Route("remote-server")]
    public class RemoteServerController : ControllerBase
    {
        private readonly ILogger<RemoteServerController> _logger;
        private readonly IRemoteServerOperatingSystemReader _operatingSystemReader;

        public RemoteServerController(
            IRemoteServerOperatingSystemReader operatingSystemReader,
            ILogger<RemoteServerController> logger)
        {
            _operatingSystemReader = operatingSystemReader;
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
                var operatingSystem = _operatingSystemReader.Read(serverName);
                if (operatingSystem == null)
                {
                    return BadRequest(new { error = "Unable to open the target machine" });
                }

                return Ok(operatingSystem);
            }
            // Expected operational failures when connecting to / reading a remote registry.
            // Anything else is a bug in this endpoint and should surface as a 500.
            catch (System.IO.IOException ex)
            {
                _logger.LogError(ex, "Failed to read remote registry for server");
                return BadRequest(new { error = "Failed to read remote registry for server" });
            }
            catch (System.Security.SecurityException ex)
            {
                _logger.LogError(ex, "Failed to read remote registry for server");
                return BadRequest(new { error = "Failed to read remote registry for server" });
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "Failed to read remote registry for server");
                return BadRequest(new { error = "Failed to read remote registry for server" });
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "Failed to read remote registry for server");
                return BadRequest(new { error = "Failed to read remote registry for server" });
            }
        }
    }
}
