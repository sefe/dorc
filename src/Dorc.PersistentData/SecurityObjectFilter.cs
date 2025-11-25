using Dorc.ApiModel;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.EntityFrameworkCore;
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

            var allow = IsBitSet((byte)allowed, (int)accessLevel);
            return denied <= 0 && allow;
        }

        #endregion

        private static bool IsBitSet(byte b, int pos)
        {
            return (b & pos) != 0;
        }

        private IEnumerable<AccessControlApiModel> GetUserAccessControls<T>(T securityObject, IPrincipal user)
            where T : SecurityObject
        {
            // Get access control entries for the object
            var accessControls = accessControlPersistentSource.GetAccessControls(securityObject.ObjectId).ToArray();

            var userSids = _claimsPrincipalReader.GetSidsForUser(user);

            var userAccessControls = accessControls.Where(
                accessControl => EF.Constant(userSids).Contains(accessControl.Sid) ||
                accessControl.Pid != null && EF.Constant(userSids).Contains(accessControl.Pid)
             ).ToList();

            return userAccessControls;
        }
    }
}