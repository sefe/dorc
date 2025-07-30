using System.Security.Claims;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Repositories;
using Dorc.PersistentData.Sources.Interfaces;

namespace Dorc.Core
{
    public class SecurityPrivilegesChecker : ISecurityPrivilegesChecker
    {
        private readonly IEnvironmentsPersistentSource _environmentsPersistentSource;
        private readonly IProjectsPersistentSource _projectsPersistentSource;
        private readonly ISecurityObjectFilter _securityObjectFilter;
        private readonly IUsersPersistentSource _usersPersistentSource;
        private readonly IRolePrivilegesChecker _rolePrivilegesChecker;
        private readonly IClaimsPrincipalReader _claimsPrincipalReader;

        public SecurityPrivilegesChecker(IProjectsPersistentSource projectsPersistentSource,
            IEnvironmentsPersistentSource environmentsPersistentSource,
            ISecurityObjectFilter securityObjectFilter,
            IUsersPersistentSource usersPersistentSource,
            IRolePrivilegesChecker rolePrivilegesChecker,
            IClaimsPrincipalReader claimsPrincipalReader
            )
        {
            _rolePrivilegesChecker = rolePrivilegesChecker;
            _usersPersistentSource = usersPersistentSource;
            _securityObjectFilter = securityObjectFilter;
            _environmentsPersistentSource = environmentsPersistentSource;
            _projectsPersistentSource = projectsPersistentSource;
            _claimsPrincipalReader = claimsPrincipalReader;
        }

        public bool CanModifyProperty(ClaimsPrincipal user)
        {
            return _rolePrivilegesChecker.IsAdmin(user) || _rolePrivilegesChecker.IsPowerUser(user);
        }

        public bool CanModifyPropertyValue(ClaimsPrincipal user, string environmentName)
        {
            return CanModifyEnvironment(user, environmentName);
        }

        public bool IsEnvironmentOwnerOrAdmin(ClaimsPrincipal user, string environmentName)
        {
            return _rolePrivilegesChecker.IsAdmin(user) || _environmentsPersistentSource.IsEnvironmentOwner(environmentName, user);
        }
        public bool IsEnvironmentOwnerOrAdminOrDelegate(ClaimsPrincipal user, string environmentName)
        {
            return _rolePrivilegesChecker.IsAdmin(user) || IsEnvironmentOwnerOrDelegate(user, environmentName);
        }

        public bool IsEnvironmentOwnerOrDelegate(ClaimsPrincipal user, string environmentName)
        {
            var env = _environmentsPersistentSource.GetEnvironment(environmentName);
            var userApiModels = _usersPersistentSource.GetEnvironmentUsers(env.EnvironmentId, UserAccountType.NotSet);
            string username = _claimsPrincipalReader.GetUserLogin(user);
            return _environmentsPersistentSource.IsEnvironmentOwner(environmentName, user) || userApiModels.Any(u => u.LanId == username);
        }

        public bool CanReadSecrets(ClaimsPrincipal user, string environmentName)
        {
            var env = _environmentsPersistentSource.GetSecurityObject(environmentName);
            return env != null && _securityObjectFilter.HasPrivilege(env, user, AccessLevel.ReadSecrets | AccessLevel.Owner);
        }

        public bool CanModifyProject(ClaimsPrincipal user, string projectName)
        {
            var project = _projectsPersistentSource.GetSecurityObject(projectName);
            if (project == null) throw new ApplicationException($"Unable to locate an project called {projectName}");
            return string.IsNullOrEmpty(projectName)
                ? CanModifyProperty(user)
                : _securityObjectFilter.HasPrivilege(project, user, AccessLevel.Write);
        }

        public bool CanModifyProject(ClaimsPrincipal user, int projectId)
        {
            var project = _projectsPersistentSource.GetSecurityObject(projectId);
            if (project == null) throw new ApplicationException($"Unable to locate an project with ID {projectId}");
            return _securityObjectFilter.HasPrivilege(project, user, AccessLevel.Write);
        }

        public bool CanModifyEnvironment(ClaimsPrincipal user, string envName)
        {
            var env = _environmentsPersistentSource.GetSecurityObject(envName);
            if (env == null)
            {
                return _rolePrivilegesChecker.IsAdmin(user);
            }

            var isEnvironmentOwner = _environmentsPersistentSource.IsEnvironmentOwner(envName, user);
            var isDelegatedUser = _usersPersistentSource.IsDelegatedUser(env.Id, user);
            var canModify = _securityObjectFilter.HasPrivilege(env, user, AccessLevel.Write);
            return canModify || isEnvironmentOwner || isDelegatedUser;
        }
    }
}
