using Dorc.Api.Windows.Interfaces;
using Dorc.PersistentData;
using System.Security.Claims;
using System.Security.Principal;

namespace Dorc.Api.Windows.Security
{
    public class WinAuthClaimsPrincipalReader : IClaimsPrincipalReader
    {
        private IUserGroupReader _userGroupReader;

        public WinAuthClaimsPrincipalReader(IUserGroupReader userGroupReader)
        {
            _userGroupReader = userGroupReader;
        }

        public string GetUserName(IPrincipal user)
        {
            return GetUserFullDomainName(user).Split('\\')[1];
        }

        public string GetUserId(ClaimsPrincipal user)
        {
            return GetUserName(user); // returning name as this is the identifier was used in Windows auth
        }

        public string GetUserFullDomainName(IPrincipal user)
        {
            return user?.Identity?.Name ?? string.Empty;
        }

        public string GetUserLogin(IPrincipal user)
        {
            return GetUserName(user); // returning name as this is the identifier was used in Windows auth
        }

        public string GetUserEmail(ClaimsPrincipal user)
        {
            string userName = GetUserName(user);
            return _userGroupReader.GetUserMail(userName);
        }

        public List<string> GetSidsForUser(IPrincipal user)
        {
            return _userGroupReader.GetSidsForUser(GetUserName(user));
        }
    }
}