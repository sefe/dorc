using Dorc.PersistentData.Model;
using System.Security.Principal;

namespace Dorc.PersistentData
{
    public enum AccessLevel
    {
        None = 0,
        Write = 1,
        ReadSecrets = 2
    }

    public interface ISecurityObjectFilter
    {
        bool HasPrivilege<T>(T securityObject, IPrincipal user, AccessLevel accessLevel) where T : SecurityObject;
    }
}