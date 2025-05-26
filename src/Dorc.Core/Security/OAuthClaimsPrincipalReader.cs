using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using System.Security.Claims;
using System.Security.Principal;

namespace Dorc.Core
{
    public class OAuthClaimsPrincipalReader : IClaimsPrincipalReader
    {
        private const string EmailClaimType = "email";
        private const string PidClaimType = "oid";
        private const string SamAccountNameClaimType = "samAccountName";
        private IUserGroupReader _userGroupReader;

        public OAuthClaimsPrincipalReader(IUserGroupReader userGroupReader)
        {
            _userGroupReader = userGroupReader;
        }

        private ClaimsPrincipal GetClaimsPrincipal(IPrincipal user)
        {
            return user as ClaimsPrincipal ?? throw new ArgumentException("user is not a ClaimsPrincipal");
        }

        public string GetUserName(IPrincipal user)
        {
            return user?.Identity?.Name ?? string.Empty;
        }

        public string GetUserFullDomainName(IPrincipal user)
        {
            var cUser = GetClaimsPrincipal(user);
            return GetUserEmail(cUser);
        }

        public string GetUserLogin(IPrincipal user)
        {
            var cUser = GetClaimsPrincipal(user);
            return GetSamAccountName(cUser) ?? GetUserEmail(cUser); // for backward compatibility with AD return samAccountName if available
        }

        public string GetUserEmail(ClaimsPrincipal user)
        {
            return user.FindFirst(EmailClaimType)?.Value ?? string.Empty;
        }

        public string GetUserId(ClaimsPrincipal user)
        {
            return user?.FindFirst(PidClaimType)?.Value ?? string.Empty;
        }

        public string GetSamAccountName(ClaimsPrincipal user)
        {
            return user?.FindFirst(SamAccountNameClaimType)?.Value ?? string.Empty;
        }

        public List<string> GetSidsForUser(IPrincipal user)
        {
            var cUser = GetClaimsPrincipal(user);
            var pids = _userGroupReader.GetSidsForUser(GetUserId(cUser));

            // add samAccountName as one of pids for backward compatibility with AD
            var samAccountName = cUser?.FindFirst(SamAccountNameClaimType)?.Value;
            if (!String.IsNullOrEmpty(samAccountName)) 
                pids.Add(samAccountName);

            return pids;
        }
    }
}
