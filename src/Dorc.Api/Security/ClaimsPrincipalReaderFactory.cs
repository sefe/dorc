using Dorc.Api.Interfaces;
using Dorc.Core.Configuration;
using Dorc.PersistentData;
using System.Security.Claims;
using System.Security.Principal;

namespace Dorc.Api.Security
{
    // NOTE: as of S-007 this type is NOT registered anywhere — ConfigureBoth was its only
    // wiring and that was deleted. IClaimsPrincipalReader resolves to OAuthClaimsPrincipalReader.
    // Consequence: AppSettings:IsUseAdSidsForAccessControl is now read only from inside this
    // dead class, i.e. it is a silent no-op. Sites relying on it get Entra oids/pids instead of
    // AD SIDs, so AccessControl rows keyed on legacy AD SIDs stop matching (fails closed).
    // Decide before merge: delete this class and the flag, or register it — but note the AD
    // branch resolves names via GetUserName/GetUserLogin, which under OAuth yields a display
    // name or email that AzureEntraSearcher.ResolveUserIdFromName will not match.
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
