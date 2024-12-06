using System.Security.Principal;

namespace Dorc.PersistentData
{
    public class RolePrivilegesChecker : IRolePrivilegesChecker
    {
        private const string Admin = "Admin";
        private const string PowerUser = "PowerUser";

        public bool IsAdmin(IPrincipal user)
        {
            return user.IsInRole(Admin);
        }

        public IEnumerable<string> GetRoles(IPrincipal user)
        {
            var allRoles = new List<string> { Admin, PowerUser };

            return allRoles.Where(user.IsInRole).ToList();
        }

        public bool IsPowerUser(IPrincipal user)
        {
            return user.IsInRole(PowerUser);
        }

        public bool IsAdminUser(IPrincipal user)
        {
            return user.IsInRole(Admin);
        }

    }
}
