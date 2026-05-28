using Dorc.Api.Interfaces;
using Dorc.Core.Configuration;
using Dorc.PersistentData;
using System.Security.Claims;
using System.Security.Principal;

namespace Dorc.Api.Security
{
    // Post-S-007, only the OAuth reader is supported (WinAuth/Negotiate removed
    // per HLPS Scope E). The name "Factory" is now misleading — there's no
    // choice to make — but the type is preserved so consumers' DI registrations
    // and dependency declarations don't churn in this PR. Renaming belongs to a
    // separate, scoped naming pass (HLPS C-2).
    public class ClaimsPrincipalReaderFactory : IClaimsPrincipalReader
    {
        private readonly IConfigurationSettings _config;
        private readonly IUserGroupReader _adUserGroupsReader;
        private readonly OAuthClaimsPrincipalReader _oauthReader;

        public ClaimsPrincipalReaderFactory(
            IConfigurationSettings config,
            IHttpContextAccessor httpContextAccessor,
            IUserGroupReader userGroupReader)
        {
            _config = config;
            _adUserGroupsReader = userGroupReader;
            _oauthReader = new OAuthClaimsPrincipalReader(userGroupReader);
        }

        public string GetUserName(IPrincipal user) => _oauthReader.GetUserName(user);

        public string GetUserId(ClaimsPrincipal user)
        {
            if (_config.GetIsUseAdSidsForAccessControl())
            {
                var data = _adUserGroupsReader.GetUserData(GetUserName(user));
                return data.Sid;
            }
            return _oauthReader.GetUserId(user);
        }

        public string GetUserLogin(IPrincipal user) => _oauthReader.GetUserLogin(user);

        public string GetUserFullDomainName(IPrincipal user) => _oauthReader.GetUserFullDomainName(user);

        public string GetUserSafeIdentifier(IPrincipal user) => _oauthReader.GetUserSafeIdentifier(user);

        public string GetUserEmail(ClaimsPrincipal user) => _oauthReader.GetUserEmail(user);

        public List<string> GetSidsForUser(IPrincipal user)
        {
            if (_config.GetIsUseAdSidsForAccessControl())
            {
                return _adUserGroupsReader.GetSidsForUser(GetUserLogin(user));
            }
            return _oauthReader.GetSidsForUser(user);
        }
    }
}
