using Dorc.ApiModel;
using Dorc.PersistentData.Extensions;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using log4net;
using System.Security.Claims;
using System.Security.Principal;

namespace Dorc.PersistentData
{
    public class SecurityObjectFilter : ISecurityObjectFilter
    {
        private readonly IAccessControlPersistentSource accessControlPersistentSource;
        private readonly ILog logger;

        public SecurityObjectFilter(
                   IAccessControlPersistentSource accessControlPersistentSource,
                   ILog logger)
        {
            this.accessControlPersistentSource = accessControlPersistentSource;
            this.logger = logger;
        }

        #region Implementation of ISecurityObjectFilter

        public bool HasPrivilege<T>(T securityObject, IPrincipal user, AccessLevel accessLevel) where T : SecurityObject
        {
            if (user.IsInRole("Admin"))
            {
                return true;
            }

            var userAccessControls = GetUserAccessControls(securityObject, user.Identity as ClaimsIdentity);

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

        private IEnumerable<AccessControlApiModel> GetUserAccessControls<T>(T securityObject, ClaimsIdentity identity)
            where T : SecurityObject
        {
            // Get access control entries for the object
            var accessControls = accessControlPersistentSource.GetAccessControls(securityObject.ObjectId).ToArray();

            var sidsForUser = identity.Name.Split('\\')[1].GetSidsForUser();

            var userAccessControls = accessControls.Where(accessControl => sidsForUser.Contains(accessControl.Sid)).ToList();

            return userAccessControls;
        }
    }
}