using Dorc.Api.Interfaces;
using Dorc.PersistentData;
using System.Security.Claims;
using System.Security.Principal;

namespace Dorc.Api.Security
{
    public class OAuthClaimsPrincipalReader : IClaimsPrincipalReader
    {
        private const string EmailClaimType = "email";
        private const string PidClaimType = "oid";
        private const string SamAccountNameClaimType = "samAccountName";
        private const string M2MClaimType = "m2m";
        private const string ClientIdClaimType = "client_id";
        private readonly IUserGroupReader _userGroupReader;

        public OAuthClaimsPrincipalReader(IUserGroupReader userGroupReader)
        {
            _userGroupReader = userGroupReader;
        }

        private ClaimsPrincipal GetClaimsPrincipal(IPrincipal user)
        {
            return user as ClaimsPrincipal ?? throw new ArgumentException("user is not a ClaimsPrincipal");
        }

        private bool IsM2MAuthentication(ClaimsPrincipal user)
        {
            return user?.FindFirst(M2MClaimType)?.Value?.ToLower() == "true";
        }

        private string GetClientId(ClaimsPrincipal user)
        {
            return user?.FindFirst(ClientIdClaimType)?.Value ?? string.Empty;
        }

        public string GetUserName(IPrincipal user)
        {
            var cUser = GetClaimsPrincipal(user);
            return IsM2MAuthentication(cUser) ? GetClientId(cUser) : (user?.Identity?.Name ?? string.Empty);
        }

        public string GetUserFullDomainName(IPrincipal user)
        {
            var cUser = GetClaimsPrincipal(user);
            return IsM2MAuthentication(cUser) ? GetClientId(cUser) : GetUserEmail(cUser);
        }

        public string GetUserLogin(IPrincipal user)
        {
            var cUser = GetClaimsPrincipal(user);
            return IsM2MAuthentication(cUser) ? GetClientId(cUser) : (GetSamAccountName(cUser) ?? GetUserEmail(cUser));
        }

        public string GetUserEmail(ClaimsPrincipal user)
        {
            return user.FindFirst(EmailClaimType)?.Value ?? string.Empty;
        }

        public string GetUserId(ClaimsPrincipal user)
        {
            return IsM2MAuthentication(user) ? GetClientId(user) : (user?.FindFirst(PidClaimType)?.Value ?? string.Empty);
        }

        private string? GetSamAccountName(ClaimsPrincipal user)
        {
            return user?.FindFirst(SamAccountNameClaimType)?.Value;
        }

        public List<string> GetSidsForUser(IPrincipal user)
        {
            var cUser = GetClaimsPrincipal(user);
            
            if (IsM2MAuthentication(cUser))
            {
                return new List<string> { GetClientId(cUser) };
            }

            var pids = _userGroupReader.GetSidsForUser(GetUserId(cUser));

            // add samAccountName as one of pids for backward compatibility with AD
            var samAccountName = cUser?.FindFirst(SamAccountNameClaimType)?.Value;
            if (!String.IsNullOrEmpty(samAccountName)) 
                pids.Add(samAccountName);

            return pids;
        }
    }
}
