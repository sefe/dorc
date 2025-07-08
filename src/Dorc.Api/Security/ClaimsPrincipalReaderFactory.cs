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
        private readonly IConfigurationSettings _config;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly OAuthClaimsPrincipalReader _oauthReader;
        private readonly WinAuthClaimsPrincipalReader _winAuthReader;

        public ClaimsPrincipalReaderFactory(
            IConfigurationSettings config,
            IHttpContextAccessor httpContextAccessor,
            IUserGroupsReaderFactory userGroupsReaderFactory
            )
        {
            _config = config;
            _httpContextAccessor = httpContextAccessor;

            _oauthReader = new OAuthClaimsPrincipalReader(userGroupsReaderFactory.GetOAuthUserGroupsReader());
            _winAuthReader = new WinAuthClaimsPrincipalReader(userGroupsReaderFactory.GetWinAuthUserGroupsReader());
        }

        public string GetUserName(IPrincipal user)
        {
            var reader = ResolveReader();
            return reader.GetUserName(user);
        }

        public string GetUserId(ClaimsPrincipal user)
        {
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
                return _winAuthReader.GetSidsForUser(user);
            }

            var reader = ResolveReader();
            return reader.GetSidsForUser(user);
        }
    }
}
