using Dorc.ApiModel;
using Dorc.Core.Configuration;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Extensions;
using Dorc.PersistentData.Sources.Interfaces;
using log4net;
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
        private readonly ISqlUserPasswordReset _sqlUserPasswordReset;
        private readonly IConfigValuesPersistentSource _configValuesPersistentSource;
        private readonly ILog _logger;
        private readonly ISecurityPrivilegesChecker _securityPrivilegesChecker;
        private readonly IConfigurationSettings _configurationSettingsEngine;
        private readonly IClaimsPrincipalReader _claimsPrincipalReader;

        public ResetAppPasswordController(IDatabasesPersistentSource databasesPersistentSource,
            ISqlUserPasswordReset sqlUserPasswordReset,
            IConfigValuesPersistentSource configValuesPersistentSource,
            ILog logger,
            ISecurityPrivilegesChecker securityPrivilegesChecker,
            IConfigurationSettings configurationSettingsEngine,
            IClaimsPrincipalReader claimsPrincipalReader)
        {
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
            return _securityPrivilegesChecker.IsEnvironmentOwnerOrAdminOrDelegate(User, envName)
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
                var db = _databasesPersistentSource.GetApplicationDatabaseForEnvFilter(envFilter, envName);
                if (db == null)
                    return Ok(new ApiBoolResult
                    { Message = $"No application database found for environment '{envName}' with users of login type '{envFilter}'", Result = false });

                return Ok(ResetSqlServerPasswordForUser(username, db.ServerName));
            }
            catch (Exception e)
            {
                _logger.Error(e);
                return StatusCode(StatusCodes.Status500InternalServerError, e);
            }
        }

        private ApiBoolResult ResetSqlServerPasswordForUser(string username, string serverName)
        {
            var user = _configValuesPersistentSource.GetConfigValue("DORC_NonProdDeployUsername");
            var pwd = _configValuesPersistentSource.GetConfigValue("DORC_NonProdDeployPassword");

            var domainName = _configurationSettingsEngine.GetConfigurationDomainNameIntra();
            
            if (user == null || pwd == null)
                return new ApiBoolResult { Message = "Unable to retrieve DOrc Login details", Result = false };

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