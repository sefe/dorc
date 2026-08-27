using Dorc.Api.Exceptions;
using Dorc.Api.Interfaces;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Dorc.Api.Controllers
{
    // Platform-neutral since S-006: the caller is authorised here against Graph-backed
    // claims, the target database is resolved here, and the WindowsIdentity impersonation
    // half moved to the Windows worker (PasswordResetController), which runs the reset as
    // its own service account. The caller's identity travels for audit logging only.
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class ResetAppPasswordController : ControllerBase
    {
        private readonly IDatabasesPersistentSource _databasesPersistentSource;
        private readonly ILogger _logger;
        private readonly ISecurityPrivilegesChecker _securityPrivilegesChecker;
        private readonly IClaimsPrincipalReader _claimsPrincipalReader;
        private readonly IWindowsWorkerClient _windowsWorkerClient;

        public ResetAppPasswordController(IDatabasesPersistentSource databasesPersistentSource,
            ILogger<ResetAppPasswordController> logger,
            ISecurityPrivilegesChecker securityPrivilegesChecker,
            IClaimsPrincipalReader claimsPrincipalReader,
            IWindowsWorkerClient windowsWorkerClient)
        {
            _securityPrivilegesChecker = securityPrivilegesChecker;
            _logger = logger;
            _databasesPersistentSource = databasesPersistentSource;
            _claimsPrincipalReader = claimsPrincipalReader;
            _windowsWorkerClient = windowsWorkerClient;
        }

        /// <summary>
        /// Reset the password for the specified user
        /// </summary>
        /// <param name="envFilter"></param>
        /// <param name="envName"></param>
        /// <param name="username"></param>
        /// <returns></returns>
        [Route("ForUser")]
        [Produces(typeof(ApiBoolResult))]
        [HttpPut]
        public async Task<IActionResult> Put(string envFilter, string envName, string username)
        {
            return _securityPrivilegesChecker.CanModifyEnvironment(User, envName)
                ? await ResetPassword(envFilter, envName, username)
                : StatusCode(StatusCodes.Status403Forbidden,
                    $"You are not authorized to reset passwords for {envName}");
        }

        private async Task<IActionResult> ResetPassword(string envFilter, string envName, string username)
        {
            try
            {
                var db = _databasesPersistentSource.GetApplicationDatabaseForEnvFilter(username, envFilter, envName);
                if (db == null)
                    return Ok(new ApiBoolResult
                    { Message = $"No application database found for environment '{envName}' with users of login type '{envFilter}'", Result = false });

                var result = await _windowsWorkerClient.ResetAppPasswordAsync(new WorkerPasswordResetRequestApiModel
                {
                    ServerName = db.ServerName,
                    Username = username,
                    CallerIdentity = _claimsPrincipalReader.GetUserFullDomainName(User)
                });

                return Ok(result);
            }
            // Worker-state exceptions pass through to WorkerUnavailableExceptionFilter,
            // which renders the documented 503/400 bodies; everything else keeps the
            // pre-move contract of a logged 500.
            catch (Exception e) when (e is not WorkerUnavailableException and not WorkerRequestRejectedException)
            {
                _logger.LogError(e, "Failed to reset password for user '{Username}' in environment '{EnvName}' with filter '{EnvFilter}'", username, envName, envFilter);
                return StatusCode(StatusCodes.Status500InternalServerError, e);
            }
        }
    }
}
