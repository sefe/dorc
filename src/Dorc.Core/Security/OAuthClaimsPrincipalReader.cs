using System.Security.Claims;
using System.Security.Principal;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;

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

        public string GetUserId(ClaimsPrincipal user)
        {
            return user?.FindFirst(PidClaimType)?.Value ?? string.Empty;
        }

        public List<string> GetSidsForUser(IPrincipal user)
        {
            var pids = _userGroupReader.GetSidsForUser(GetUserId(user as ClaimsPrincipal));
            
            // add samAccountName as one of pids to support legacy
            var samAccountName = (user as ClaimsPrincipal)?.FindFirst(SamAccountNameClaimType)?.Value;
            if (!String.IsNullOrEmpty(samAccountName)) 
                pids.Add(samAccountName);

            return pids;
        }
    }
}
