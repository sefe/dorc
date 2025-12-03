using Dorc.Api.Interfaces;
using Dorc.Core.Configuration;
using Dorc.PersistentData;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using System.Security.Claims;
using System.Security.Principal;

namespace Dorc.Api.Security
{
    public class ClaimsPrincipalReaderFactory : IClaimsPrincipalReader
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IConfigurationSettings _config;
        private readonly IUserGroupReader _adUserGroupsReader;
        private readonly OAuthClaimsPrincipalReader _oauthReader;
        private readonly WinAuthClaimsPrincipalReader _winAuthReader;

        public ClaimsPrincipalReaderFactory(
            IConfigurationSettings config,
            IHttpContextAccessor httpContextAccessor,
            IUserGroupProvider userGroupsReaderFactory
            )
        {
            _httpContextAccessor = httpContextAccessor;
            _config = config;

            _adUserGroupsReader = userGroupsReaderFactory.GetWinAuthUserGroupsReader();

            _oauthReader = new OAuthClaimsPrincipalReader(userGroupsReaderFactory.GetOAuthUserGroupsReader());
            _winAuthReader = new WinAuthClaimsPrincipalReader(_adUserGroupsReader);
        }

        public string GetUserName(IPrincipal user)
        {
            var reader = ResolveReader();
            return reader.GetUserName(user);
        }

        public string GetUserId(ClaimsPrincipal user)
        {
            if (_config.GetIsUseAdSidsForAccessControl())
            {
                var data = _adUserGroupsReader.GetUserData(GetUserName(user));
                return data.Sid;
            }

            var reader = ResolveReader();
            return reader.GetUserId(user);
        }

        public string GetUserLogin(IPrincipal user)
        {
            var reader = ResolveReader();
            return reader.GetUserLogin(user);
        }

        public string GetUserFullDomainName(IPrincipal user)
        {
            var reader = ResolveReader();
            return reader.GetUserFullDomainName(user);
        }

        public string GetUserEmail(ClaimsPrincipal user)
        {
            var reader = ResolveReader();
            return reader.GetUserEmail(user);
        }

        private IClaimsPrincipalReader ResolveReader()
        {
            var httpContext = _httpContextAccessor.HttpContext;

            var scheme = httpContext.GetAuthenticationScheme();
            if (scheme == JwtBearerDefaults.AuthenticationScheme)
            {
                return _oauthReader; // Use OAuth reader
            }

            return _winAuthReader; // Fallback to WinAuth reader
        }

        public List<string> GetSidsForUser(IPrincipal user)
        {
            if (_config.GetIsUseAdSidsForAccessControl())
            {
                return _adUserGroupsReader.GetSidsForUser(GetUserLogin(user));
            }

            var reader = ResolveReader();
            return reader.GetSidsForUser(user);
        }
    }
}
