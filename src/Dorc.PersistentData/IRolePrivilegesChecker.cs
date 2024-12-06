using System.Security.Principal;

namespace Dorc.PersistentData
{
    public interface IRolePrivilegesChecker
    {
        bool IsAdmin(IPrincipal user);
        IEnumerable<string> GetRoles(IPrincipal user);
        bool IsPowerUser(IPrincipal user);
        bool IsAdminUser(IPrincipal user);
    }
}