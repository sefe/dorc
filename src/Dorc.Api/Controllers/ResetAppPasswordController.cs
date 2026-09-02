using Dorc.ApiModel;
using Dorc.Core.Configuration;
using Dorc.Core.Security;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Extensions;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace Dorc.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    [SupportedOSPlatform("windows")]
    public class ResetAppPasswordController : ControllerBase
    {
        private readonly IDatabasesPersistentSource _databasesPersistentSource;
        private readonly IEnvironmentsPersistentSource _environmentsPersistentSource;
        private readonly IDeploymentCredentialSource _credentialSource;
        private readonly ISqlUserPasswordReset _sqlUserPasswordReset;
        private readonly IConfigValuesPersistentSource _configValuesPersistentSource;
        private readonly ILogger _logger;
        private readonly ISecurityPrivilegesChecker _securityPrivilegesChecker;
        private readonly IConfigurationSettings _configurationSettingsEngine;
        private readonly IClaimsPrincipalReader _claimsPrincipalReader;

        public ResetAppPasswordController(IDatabasesPersistentSource databasesPersistentSource,
            ISqlUserPasswordReset sqlUserPasswordReset,
            IConfigValuesPersistentSource configValuesPersistentSource,
            ILogger<ResetAppPasswordController> logger,
            ISecurityPrivilegesChecker securityPrivilegesChecker,
            IConfigurationSettings configurationSettingsEngine,
            IClaimsPrincipalReader claimsPrincipalReader,
            IEnvironmentsPersistentSource environmentsPersistentSource,
            IDeploymentCredentialSource credentialSource)
        {
            _environmentsPersistentSource = environmentsPersistentSource;
            _credentialSource = credentialSource;
            _securityPrivilegesChecker = securityPrivilegesChecker;
            _logger = logger;
            _configValuesPersistentSource = configValuesPersistentSource;
            _sqlUserPasswordReset = sqlUserPasswordReset;
            _databasesPersistentSource = databasesPersistentSource;
            _configurationSettingsEngine = configurationSettingsEngine;
            _claimsPrincipalReader = claimsPrincipalReader;
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
        public IActionResult Put(string envFilter, string envName, string username)
        {
            return _securityPrivilegesChecker.CanModifyEnvironment(User, envName)
                ? ResetPassword(envFilter, envName, username)
                : StatusCode(StatusCodes.Status403Forbidden,
                    $"You are not authorized to reset passwords for {envName}");
        }

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool LogonUser(string lpszUsername, string lpszDomain, string lpszPassword,
            int dwLogonType, int dwLogonProvider, out SafeAccessTokenHandle phToken);
        private IActionResult ResetPassword(string envFilter, string envName, string username)
        {
            try
            {
                var db = _databasesPersistentSource.GetApplicationDatabaseForEnvFilter(username, envFilter, envName);
                if (db == null)
                    return Ok(new ApiBoolResult
                    { Message = $"No application database found for environment '{envName}' with users of login type '{envFilter}'", Result = false });

                // The environment is read for its execution identity, not for authorization -
                // that has already been decided by the caller. A name that resolves to nothing
                // leaves the identity unset, which is the same as an environment that names none.
                var environment = _environmentsPersistentSource.GetEnvironment(envName, User);

                return Ok(ResetSqlServerPasswordForUser(
                    username,
                    db.ServerName,
                    environment?.ExecutionIdentityReference));
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to reset password for user '{Username}' in environment '{EnvName}' with filter '{EnvFilter}'", username, envName, envFilter);
                return StatusCode(StatusCodes.Status500InternalServerError, e);
            }
        }

        private ApiBoolResult ResetSqlServerPasswordForUser(
            string username, string serverName, string? executionIdentityReference)
        {
            // Environment-keyed, like every other resolution site: an environment naming its own
            // execution identity resets passwords under it, and one naming none behaves exactly as
            // before.
            //
            // The hard-coded TIER is still preserved rather than quietly corrected. This site
            // resolves the NON-PRODUCTION credential regardless of the server it is resetting a
            // password on; whether that is deliberate or a latent defect is a question for
            // whoever owns this endpoint, and changing it would start making production logons
            // where none were made before. Naming an identity on the environment is opt-in and
            // reversible, so it is the safe half of the binding to apply here.
            var credential = _credentialSource.Resolve(
                DeploymentTier.NonProduction,
                executionIdentityReference);

            var domainName = _configurationSettingsEngine.GetConfigurationDomainNameIntra();

            if (credential == null)
                return new ApiBoolResult { Message = "Unable to retrieve DOrc Login details", Result = false };

            var user = credential.UserName;
            var pwd = credential.Password;

            const int logon32ProviderDefault = 0;
            //This parameter causes LogonUser to create a primary token.   
            const int logon32LogonInteractive = 2;

            bool returnValue = LogonUser(user, domainName, pwd,
                logon32LogonInteractive, logon32ProviderDefault,
                out var safeAccessTokenHandle);

            if (false == returnValue)
            {
                int ret = Marshal.GetLastWin32Error();
                Console.WriteLine("LogonUser failed with error code : {0}", ret);
                throw new System.ComponentModel.Win32Exception(ret);
            }

            return WindowsIdentity.RunImpersonated(
                safeAccessTokenHandle,
                // User action  
                () => _sqlUserPasswordReset.ResetSqlUserPassword(serverName, username));
        }
    }
}