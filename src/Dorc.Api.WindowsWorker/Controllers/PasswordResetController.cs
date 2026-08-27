using Dorc.Api.WindowsWorker.Services;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Dorc.Api.WindowsWorker.Controllers
{
    // Windows-only SQL password reset (S-006 — the impersonation half moved from
    // Dorc.Api/Controllers/ResetAppPasswordController). The primary authorises the caller
    // against its own claims and forwards the request; this side runs the reset as the
    // worker's own service account (HLPS D-3), never as the caller. CallerIdentity is
    // audit-only.
    [ApiController]
    [Route("password-reset")]
    public class PasswordResetController : ControllerBase
    {
        private readonly ISqlUserPasswordReset _sqlUserPasswordReset;
        private readonly ServiceAccountImpersonator _impersonator;
        private readonly ILogger<PasswordResetController> _logger;

        public PasswordResetController(ISqlUserPasswordReset sqlUserPasswordReset,
            ServiceAccountImpersonator impersonator,
            ILogger<PasswordResetController> logger)
        {
            _sqlUserPasswordReset = sqlUserPasswordReset;
            _impersonator = impersonator;
            _logger = logger;
        }

        [HttpPost]
        public ActionResult<ApiBoolResult> Reset([FromBody] WorkerPasswordResetRequestApiModel request)
        {
            if (string.IsNullOrWhiteSpace(request.ServerName) || string.IsNullOrWhiteSpace(request.Username))
            {
                return BadRequest(new { error = "serverName and username are required" });
            }

            // Audit trail carries the CALLER the primary authorised, never the worker's
            // service-account identity (S-006 verification intent).
            _logger.LogInformation(
                "SQL password reset for {Username} on {ServerName} requested by {CallerIdentity}",
                request.Username, request.ServerName, request.CallerIdentity);

            try
            {
                var result = _impersonator.RunAsPasswordResetAccount(
                    () => _sqlUserPasswordReset.ResetSqlUserPassword(request.ServerName, request.Username));
                return Ok(result);
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                // The worker's own service-account logon failed — an install-time
                // configuration problem (S-008 provisions it), not a caller error, but
                // actionable, so surface the reason.
                _logger.LogError(ex, "Failed to log on as the password-reset service account");
                return BadRequest(new { error = $"Failed to log on as the password-reset service account: {ex.Message}" });
            }
        }
    }
}
