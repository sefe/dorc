using Dorc.ApiModel;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using System.Security.Principal;

namespace Dorc.PersistentData
{
    public class SecurityObjectFilter : ISecurityObjectFilter
    {
        private readonly IAccessControlPersistentSource accessControlPersistentSource;
        private readonly IClaimsPrincipalReader _claimsPrincipalReader;

        public SecurityObjectFilter(
                   IAccessControlPersistentSource accessControlPersistentSource,
                   IClaimsPrincipalReader claimsPrincipalReader
            )
        {
            this.accessControlPersistentSource = accessControlPersistentSource;
            this._claimsPrincipalReader = claimsPrincipalReader;
        }

        #region Implementation of ISecurityObjectFilter

        public bool HasPrivilege<T>(T securityObject, IPrincipal user, AccessLevel accessLevel) where T : SecurityObject
        {
            if (user.IsInRole("Admin"))
            {
                return true;
            }

            var userAccessControls = GetUserAccessControls(securityObject, user);

            var allowed = 0;
            var denied = 0;

            foreach (var accessControl in userAccessControls)
            {
                allowed |= accessControl.Allow;
                denied |= accessControl.Deny;
            }

            return IsAllowed(allowed, denied, accessLevel);
        }

        #endregion

        /// <summary>
        /// Decides whether the requested access level is granted, given the
        /// accumulated allow/deny masks. Deny is scoped to the requested level: an
        /// explicit deny only blocks the level(s) it applies to. Previously the code
        /// returned false whenever <em>any</em> deny bit was set (e.g. a Deny of
        /// Write also blocked ReadSecrets and Owner) — see finding F-2.
        /// AccessLevel is a [Flags]-style power-of-two set, so a mask (&amp;) test is
        /// the correct way to ask "does the granted set include any requested bit".
        /// </summary>
        internal static bool IsAllowed(int allowed, int denied, AccessLevel accessLevel)
        {
            var requested = (int)accessLevel;
            var allow = (allowed & requested) != 0;
            var deny = (denied & requested) != 0;
            return allow && !deny;
        }

        private IEnumerable<AccessControlApiModel> GetUserAccessControls<T>(T securityObject, IPrincipal user)
            where T : SecurityObject
        {
            // Get access control entries for the object
            var accessControls = accessControlPersistentSource.GetAccessControls(securityObject.ObjectId).ToArray();

            var userSids = _claimsPrincipalReader.GetSidsForUser(user);

            var userAccessControls = accessControls.Where(
                accessControl => userSids.Contains(accessControl.Sid) ||
                accessControl.Pid != null && userSids.Contains(accessControl.Pid)
             ).ToList();

            return userAccessControls;
        }
    }
}