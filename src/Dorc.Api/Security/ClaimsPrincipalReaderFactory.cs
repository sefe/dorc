using Dorc.Api.Interfaces;
using Dorc.Core;
using Dorc.PersistentData;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using System.Security.Claims;
using System.Security.Principal;

namespace Dorc.Api.Security
{
    public class ClaimsPrincipalReaderFactory : IClaimsPrincipalReader
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly OAuthClaimsPrincipalReader _oauthReader;
        private readonly WinAuthClaimsPrincipalReader _winAuthReader;

        public ClaimsPrincipalReaderFactory(
            IHttpContextAccessor httpContextAccessor,
            IUserGroupsReaderFactory userGroupsReaderFactory
            )
        {
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
            var reader = ResolveReader();
            return reader.GetSidsForUser(user);
        }
    }
}
