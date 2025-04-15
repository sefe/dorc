using System.Security.Claims;
using System.Security.Principal;
using Dorc.PersistentData;

namespace Dorc.Core
{
    public class OAuthClaimsPrincipalReader : IClaimsPrincipalReader
    {
        private const string EmailClaimType = "email";

        public string GetUserName(IPrincipal user)
        {
            return user?.Identity?.Name ?? string.Empty;
        }

        public string GetUserFullDomainName(IPrincipal user)
        {
            return GetUserName(user);
        }

        public string GetUserEmail(ClaimsPrincipal user, object activeDirectorySearcher)
        {
            return user.FindFirst(EmailClaimType)?.Value ?? string.Empty;
        }
    }
}
