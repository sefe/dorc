using Dorc.ApiModel;
using Dorc.Core.Configuration;
using Dorc.Core.Security;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Extensions;
using Dorc.PersistentData.Sources;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
            IEnvironmentsPersistentSource environmentsPersistentSource,
            ISqlUserPasswordReset sqlUserPasswordReset,
            IConfigValuesPersistentSource configValuesPersistentSource,
            ILogger<ResetAppPasswordController> logger,
            ISecurityPrivilegesChecker securityPrivilegesChecker,
            IConfigurationSettings configurationSettingsEngine,
            IClaimsPrincipalReader claimsPrincipalReader,
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
            if (string.IsNullOrWhiteSpace(envName))
            {
                return BadRequest("Environment name is required.");
            }

            var environment = _environmentsPersistentSource.GetEnvironment(envName, User);
            if (environment == null)
            {
                return NotFound($"Environment '{envName}' not found.");
            }

            if (environment.EnvironmentIsProd)
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    $"Password reset is not permitted for production environment '{environment.EnvironmentName}'.");
            }

            if (!_securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(User, environment.EnvironmentName))
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    $"You are not authorized to reset passwords for {environment.EnvironmentName}");
            }

            return ResetPassword(envFilter, environment, username);
        }

        private IActionResult ResetPassword(string envFilter, EnvironmentApiModel environment, string username)
        {
            try
            {
                var db = _databasesPersistentSource.GetApplicationDatabaseForEnvFilter(
                    username, envFilter, environment.EnvironmentName);
                if (db == null)
                    return Ok(new ApiBoolResult
                    { Message = $"No application database found for environment '{environment.EnvironmentName}' with users of login type '{envFilter}'", Result = false });

                if (!IsDatabaseMappedStrictlyToNonProdEnvironment(db, environment, username))
                {
                    return StatusCode(StatusCodes.Status403Forbidden,
                        $"Target database is not mapped exclusively to non-production environment '{environment.EnvironmentName}'.");
                }

                return Ok(ResetSqlServerPasswordForUser(
                    username,
                    db.ServerName,
                    environment.ExecutionIdentityReference));
            }
            catch (Exception e)
            {
                string safeUsername = (username ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);
                string safeEnvironmentName = (environment.EnvironmentName ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);
                string safeEnvFilter = (envFilter ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);
                _logger.LogError(
                    e,
                    "Failed to reset password for user '{Username}' in environment '{EnvName}' with filter '{EnvFilter}'",
                    safeUsername,
                    safeEnvironmentName,
                    safeEnvFilter);
                return StatusCode(StatusCodes.Status500InternalServerError, "Failed to reset password.");
            }
        }

        private bool IsDatabaseMappedStrictlyToNonProdEnvironment(
            DatabaseApiModel db,
            EnvironmentApiModel environment,
            string username)
        {
            if (db.Id <= 0 || string.IsNullOrWhiteSpace(db.ServerName))
            {
                return false;
            }

            var attachedDatabases = _databasesPersistentSource.GetDatabasesForEnvId(environment.EnvironmentId)?.ToList()
                ?? new List<DatabaseApiModel>();

            bool attachedToRequestedEnvironment = attachedDatabases.Any(attached =>
                attached.Id == db.Id
                && DatabasesPersistentSource.SqlServerInstancesMatch(attached.ServerName, db.ServerName));

            if (!attachedToRequestedEnvironment)
            {
                return false;
            }

            var instanceDatabases = (_databasesPersistentSource.GetDatabasesForServerName(db.ServerName)
                    ?? Enumerable.Empty<DatabaseApiModel>())
                .Where(candidate => candidate != null
                    && DatabasesPersistentSource.SqlServerInstancesMatch(candidate.ServerName, db.ServerName))
                .Append(db)
                .ToList();

            foreach (var instanceDb in instanceDatabases)
            {
                if (instanceDb.Id <= 0)
                {
                    return false;
                }

                var mappedEnvironmentNames = GetMappedEnvironmentNames(instanceDb);
                if (mappedEnvironmentNames.Any(mappedEnv => _environmentsPersistentSource.EnvironmentIsProd(mappedEnv)))
                {
                    return false;
                }
            }

            var loginDatabasesOnInstance = (_databasesPersistentSource.GetDatabasesForServerAndLogin(db.ServerName, username)
                    ?? Enumerable.Empty<DatabaseApiModel>())
                .Where(candidate => candidate != null
                    && DatabasesPersistentSource.SqlServerInstancesMatch(candidate.ServerName, db.ServerName))
                .Append(db)
                .GroupBy(candidate => candidate.Id)
                .Select(group => group.First())
                .ToList();

            foreach (var loginDb in loginDatabasesOnInstance)
            {
                if (loginDb.Id <= 0)
                {
                    return false;
                }

                var mappedEnvironmentNames = GetMappedEnvironmentNames(loginDb);
                if (mappedEnvironmentNames.Count == 0)
                {
                    return false;
                }

                if (mappedEnvironmentNames.Any(mappedEnv =>
                    !_securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(User, mappedEnv)))
                {
                    return false;
                }
            }

            return true;
        }

        private HashSet<string> GetMappedEnvironmentNames(DatabaseApiModel database)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (database.EnvironmentNames != null)
            {
                foreach (var envName in database.EnvironmentNames.Where(name => !string.IsNullOrWhiteSpace(name)))
                {
                    names.Add(envName);
                }
            }

            var persistedNames = _databasesPersistentSource.GetEnvironmentNamesForDatabaseId(database.Id);
            if (persistedNames != null)
            {
                foreach (var envName in persistedNames.Where(name => !string.IsNullOrWhiteSpace(name)))
                {
                    names.Add(envName);
                }
            }

            return names;
        }

        private ApiBoolResult ResetSqlServerPasswordForUser(
            string username, string serverName, string? executionIdentityReference)
        {
            // Application SQL password resets are restricted strictly to non-production
            // environments above; an environment naming its own execution identity resets
            // passwords under that bound identity, while one naming none uses the non-production
            // tier default as before.
            using var credential = _credentialSource.Resolve(
                DeploymentTier.NonProduction,
                executionIdentityReference);

            var domainName = _configurationSettingsEngine.GetConfigurationDomainNameIntra();

            if (credential == null)
                return new ApiBoolResult { Message = "Unable to retrieve DOrc Login details", Result = false };

            return ExecuteImpersonated(
                credential,
                domainName,
                () => _sqlUserPasswordReset.ResetSqlUserPassword(serverName, username));
        }

        protected virtual ApiBoolResult ExecuteImpersonated(
            DeploymentCredential credential,
            string domainName,
            Func<ApiBoolResult> action)
        {
            using var token = WindowsLogon.LogOn(
                credential, domainName, LogonType.Interactive, LogonProvider.Default);

            return WindowsIdentity.RunImpersonated(token, action);
        }
    }
}