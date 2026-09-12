using Dorc.Api.Interfaces;
using Dorc.PersistentData;
using System.Security.Claims;
using System.Security.Principal;

namespace Dorc.Api.Security
{
    public class WinAuthClaimsPrincipalReader : IClaimsPrincipalReader
    {
        private IUserGroupReader _userGroupReader;

        public WinAuthClaimsPrincipalReader(IUserGroupReader userGroupReader)
        {
            _userGroupReader = userGroupReader;
        }

        /// <summary>
        /// Windows authentication carries no machine-versus-person discriminator: a service
        /// account and a person present the same kind of identity. Answering "no" is the only
        /// safe answer, and it means the ReadSecrets privilege cannot be exercised over Windows
        /// authentication at all. That is the intended shape - service accounts of live running
        /// systems authenticate as OAuth machine clients, which is where the discriminator
        /// exists - but it is a real restriction and worth knowing about before enabling
        /// Windows-only authentication for a consumer that reads secrets.
        /// </summary>
        public bool IsServicePrincipal(IPrincipal user)
        {
            return false;
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

        public string GetUserSafeIdentifier(IPrincipal user)
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