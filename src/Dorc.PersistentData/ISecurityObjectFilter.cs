using Dorc.PersistentData.Model;
using System.Security.Principal;

namespace Dorc.PersistentData
{
    [Flags]
    public enum AccessLevel
    {
        None = 0,
        Write = 1,
        ReadSecrets = 2,
        Owner = 4
    }

    public interface ISecurityObjectFilter
    {
        bool HasPrivilege<T>(T securityObject, IPrincipal user, AccessLevel accessLevel) where T : SecurityObject;
        
        /// <summary>
        /// Checks if user has the specified privilege WITHOUT admin bypass.
        /// Use this for permissions that should apply equally to all users including admins.
        /// </summary>
        bool HasPrivilegeStrict<T>(T securityObject, IPrincipal user, AccessLevel accessLevel) where T : SecurityObject;
    }
}