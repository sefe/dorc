using Dorc.Api.WindowsWorker.Services;
using Dorc.ApiModel;
using Microsoft.AspNetCore.Mvc;

namespace Dorc.Api.WindowsWorker.Controllers
{
    // Windows-only daemon service control (S-005 — moved from Dorc.Core/DaemonStatusProbe).
    // Probing and state changes run under LogonUser impersonation of the environment's
    // deploy account, which the primary resolves and sends per-request; the loopback bind
    // plus X-Worker-Key are the transport boundary (HLPS D-1/D-3).
    [ApiController]
    [Route("daemons")]
    public class DaemonsController : ControllerBase
    {
        private readonly IDaemonServiceOperations _operations;
        private readonly ILogger<DaemonsController> _logger;

        public DaemonsController(IDaemonServiceOperations operations, ILogger<DaemonsController> logger)
        {
            _operations = operations;
            _logger = logger;
        }

        [HttpPost("probe")]
        public ActionResult<List<WorkerDaemonApiModel>> Probe([FromBody] WorkerDaemonProbeRequestApiModel request)
        {
            try
            {
                return Ok(_operations.Probe(request.Credential, request.Daemons));
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                // A failed LogonUser is a configuration problem (bad deploy credential), not
                // a worker fault — surface it as a rejection the primary can relay.
                _logger.LogError(ex, "Failed to log on as the supplied deploy account for a daemon probe");
                return BadRequest(new { error = $"Failed to log on as the supplied deploy account: {ex.Message}" });
            }
        }

        [HttpPost("change-state")]
        public ActionResult<WorkerDaemonApiModel> ChangeState([FromBody] WorkerDaemonStateChangeRequestApiModel request)
        {
            try
            {
                var result = _operations.ChangeState(request.Credential, request.Daemon);
                return Ok(result ?? new WorkerDaemonApiModel());
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                _logger.LogError(ex, "Failed to log on as the supplied deploy account for a daemon state change");
                return BadRequest(new { error = $"Failed to log on as the supplied deploy account: {ex.Message}" });
            }
            catch (InvalidOperationException ex)
            {
                // ServiceController raises this for unknown service/server names.
                _logger.LogError(ex, "Daemon state change failed");
                return BadRequest(new { error = $"Daemon state change failed: {ex.Message}" });
            }
            catch (System.ServiceProcess.TimeoutException ex)
            {
                _logger.LogError(ex, "Daemon state change timed out");
                return BadRequest(new { error = $"Daemon state change timed out: {ex.Message}" });
            }
        }
    }
}
