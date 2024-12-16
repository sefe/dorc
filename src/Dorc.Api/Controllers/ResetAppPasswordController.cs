using Dorc.ApiModel;
using Dorc.Core.Configuration;
using Dorc.Core.Interfaces;
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
        private readonly IPropertyValuesPersistentSource _propertyValuesPersistentSource;
        private readonly ILog _logger;
        private readonly ISecurityPrivilegesChecker _securityPrivilegesChecker;
        private readonly IConfigurationSettings _configurationSettingsEngine;

        public ResetAppPasswordController(IDatabasesPersistentSource databasesPersistentSource,
            ISqlUserPasswordReset sqlUserPasswordReset,
            IPropertyValuesPersistentSource propertyValuesPersistentSource,
            ILog logger,
            ISecurityPrivilegesChecker securityPrivilegesChecker,
            IConfigurationSettings configurationSettingsEngine)
        {
            _securityPrivilegesChecker = securityPrivilegesChecker;
            _logger = logger;
            _propertyValuesPersistentSource = propertyValuesPersistentSource;
            _sqlUserPasswordReset = sqlUserPasswordReset;
            _databasesPersistentSource = databasesPersistentSource;
            _configurationSettingsEngine = configurationSettingsEngine;
        }

        /// <summary>
        /// Reset the password for the current user
        /// </summary>
        /// <param name="envFilter"></param>
        /// <param name="envName"></param>
        /// <returns></returns>
        [Produces(typeof(ApiBoolResult))]
        [HttpPut]
        public IActionResult Put(string envFilter, string envName)
        {
            var userSplit = User?.Identity?.Name?.Split('\\');
            if (userSplit == null)
                return Ok(new ApiBoolResult
                    { Message = "You are not currently setup in this environment", Result = false });
            var userName = userSplit[1];
            return ResetPassword(envFilter, envName, userName);
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
                var user = _propertyValuesPersistentSource.GetPropertyValues("DORC_NonProdDeployUsername", envName, true)
                    .FirstOrDefault();
                var pwd = _propertyValuesPersistentSource.GetPropertyValues("DORC_NonProdDeployPassword", envName, true)
                    .FirstOrDefault();

                var domainName = _configurationSettingsEngine.GetConfigurationDomainNameIntra();

                var db = _databasesPersistentSource.GetApplicationDatabaseForEnvFilter(username, envFilter, envName);
                if (db == null)
                    return Ok(new ApiBoolResult
                    { Message = "You are not currently setup in this environment", Result = false });

                if (user == null || pwd == null)
                    return Ok(new ApiBoolResult { Message = "Unable to retrieve DOrc Login details", Result = false });


                const int logon32ProviderDefault = 0;
                //This parameter causes LogonUser to create a primary token.   
                const int logon32LogonInteractive = 2;

                bool returnValue = LogonUser(user.Value, domainName, pwd.Value,
                    logon32LogonInteractive, logon32ProviderDefault,
                    out var safeAccessTokenHandle);

                if (false == returnValue)
                {
                    int ret = Marshal.GetLastWin32Error();
                    Console.WriteLine("LogonUser failed with error code : {0}", ret);
                    throw new System.ComponentModel.Win32Exception(ret);
                }

                return WindowsIdentity.RunImpersonated<IActionResult>(
                    safeAccessTokenHandle,
                    // User action  
                    () => Ok(_sqlUserPasswordReset.ResetSqlUserPassword(db.ServerName, username)));
            }
            catch (Exception e)
            {
                _logger.Error(e);
                return StatusCode(StatusCodes.Status500InternalServerError, e);
            }
        }
    }
}