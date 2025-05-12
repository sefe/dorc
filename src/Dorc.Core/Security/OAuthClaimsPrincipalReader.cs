using System.Security.Claims;
using System.Security.Principal;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;

namespace Dorc.Core
{
    public class OAuthClaimsPrincipalReader : IClaimsPrincipalReader
    {
        private const string EmailClaimType = "email";
        private IUserGroupReader _userGroupReader;

        public OAuthClaimsPrincipalReader(IUserGroupReader userGroupReader)
        {
            _userGroupReader = userGroupReader;
        }

        public string GetUserName(IPrincipal user)
        {
            return user?.Identity?.Name ?? string.Empty;
        }

        public string GetUserFullDomainName(IPrincipal user)
        {
            return GetUserName(user);
        }

        public string GetUserEmail(ClaimsPrincipal user)
        {
            return user.FindFirst(EmailClaimType)?.Value ?? string.Empty;
        }

        public List<string> GetSidsForUser(IPrincipal user)
        {
            return _userGroupReader.GetSidsForUser(GetUserName(user));
        }
    }
}
