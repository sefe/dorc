using System.Security.Claims;
using System.Security.Principal;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;

namespace Dorc.Core
{
    public class WinAuthClaimsPrincipalReader : IClaimsPrincipalReader
    {
        public string GetUserName(IPrincipal user)
        {
            return GetUserFullDomainName(user).Split('\\')[1];
        }

        public string GetUserFullDomainName(IPrincipal user)
        {
            return user?.Identity?.Name ?? string.Empty;
        }

        public string GetUserEmail(ClaimsPrincipal user, object externalReader)
        {
            string userName = GetUserName(user);
            if (externalReader is IActiveDirectoryUserGroupReader activeDirectoryUserGroupReader)
            {
                return activeDirectoryUserGroupReader.GetUserMail(userName);
            }
            return string.Empty;
        }
    }
}