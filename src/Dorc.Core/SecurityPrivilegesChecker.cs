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
        private readonly IRolePrivilegesChecker _rolePrivilegesChecker;
        private readonly IClaimsPrincipalReader _claimsPrincipalReader;

        public SecurityPrivilegesChecker(IProjectsPersistentSource projectsPersistentSource,
            IEnvironmentsPersistentSource environmentsPersistentSource,
            ISecurityObjectFilter securityObjectFilter,
            IRolePrivilegesChecker rolePrivilegesChecker,
            IClaimsPrincipalReader claimsPrincipalReader
            )
        {
            _rolePrivilegesChecker = rolePrivilegesChecker;
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
            return string.IsNullOrEmpty(environmentName)
                ? CanModifyProperty(user)
                : CanModifyEnvironment(user, environmentName);
        }

        public bool IsEnvironmentOwnerOrAdmin(ClaimsPrincipal user, string environmentName)
        {
            return _rolePrivilegesChecker.IsAdmin(user) || _environmentsPersistentSource.IsEnvironmentOwner(environmentName, user);
        }

        public bool IsProjectOwnerOrAdmin(ClaimsPrincipal user, string projectName)
        {
            var project = _projectsPersistentSource.GetSecurityObject(projectName);
            return project == null
                ? _rolePrivilegesChecker.IsAdmin(user)
                : _rolePrivilegesChecker.IsAdmin(user) || _securityObjectFilter.HasPrivilege(project, user, AccessLevel.Owner);
        }

        /// <summary>
        /// Whether decrypted secret values may be retrieved for this environment.
        ///
        /// The privilege exists for service accounts belonging to live running systems. As
        /// implemented it did neither of the two things that requires: it was granted
        /// implicitly to every environment OWNER, and it could not be restricted to machines at
        /// all, so human owners received decrypted secret property values through the API and
        /// the web UI - the exact outcome it was designed to prevent.
        ///
        /// Both are closed here. A person fails regardless of what they have been granted or
        /// what they own, and ownership no longer implies the privilege, so holding it now
        /// requires an explicit grant and an audit of holders means something.
        ///
        /// Humans are not refused, they are redacted: the secure value is replaced with an
        /// empty string on the path that serves it. See PropertyValuesService.
        /// </summary>
        public bool CanReadSecrets(ClaimsPrincipal user, string environmentName)
        {
            if (!_claimsPrincipalReader.IsMachinePrincipal(user))
            {
                return false;
            }

            var env = _environmentsPersistentSource.GetSecurityObject(environmentName);
            return env != null && _securityObjectFilter.HasPrivilege(env, user, AccessLevel.ReadSecrets);
        }

        /// <summary>
        /// Whether this caller may GRANT the read-secrets privilege to someone else.
        ///
        /// Deliberately a different question from whether they may exercise it, and the reason
        /// the two cannot share a predicate. Administering the privilege is something people
        /// do; exercising it is something machines do. Had the grant guard kept using
        /// CanReadSecrets after the restriction above, no human could ever have granted
        /// ReadSecrets to anyone, and the privilege would have become unadministrable.
        ///
        /// Only admins and environment owners may administer it; holding ReadSecrets does not
        /// confer grant authority.
        /// </summary>
        public bool CanGrantReadSecrets(ClaimsPrincipal user, string environmentName)
        {
            var env = _environmentsPersistentSource.GetSecurityObject(environmentName);
            if (env == null)
            {
                return false;
            }

            return _rolePrivilegesChecker.IsAdmin(user) ||
                   _environmentsPersistentSource.IsEnvironmentOwner(environmentName, user);
        }

        public bool CanModifyProject(ClaimsPrincipal user, string projectName)
        {
            if (string.IsNullOrEmpty(projectName))
                return CanModifyProperty(user);

            var project = _projectsPersistentSource.GetSecurityObject(projectName);
            if (project == null) throw new ApplicationException($"Unable to locate an project called {projectName}");
            return _rolePrivilegesChecker.IsAdmin(user) ||
                   _securityObjectFilter.HasPrivilege(project, user, AccessLevel.Write | AccessLevel.Owner);
        }

        public bool CanModifyProject(ClaimsPrincipal user, int projectId)
        {
            var project = _projectsPersistentSource.GetSecurityObject(projectId);
            if (project == null) throw new ApplicationException($"Unable to locate an project with ID {projectId}");
            return _rolePrivilegesChecker.IsAdmin(user) ||
                   _securityObjectFilter.HasPrivilege(project, user, AccessLevel.Write | AccessLevel.Owner);
        }

        public bool CanModifyEnvironment(ClaimsPrincipal user, string envName)
        {
            var env = _environmentsPersistentSource.GetSecurityObject(envName);
            if (env == null)
            {
                return _rolePrivilegesChecker.IsAdmin(user);
            }

            if (_rolePrivilegesChecker.IsAdmin(user))
                return true;

            var isEnvironmentOwner = _environmentsPersistentSource.IsEnvironmentOwner(envName, user);
            var canModify = _securityObjectFilter.HasPrivilege(env, user, AccessLevel.Write | AccessLevel.Owner);
            return canModify || isEnvironmentOwner;
        }
    }
}
