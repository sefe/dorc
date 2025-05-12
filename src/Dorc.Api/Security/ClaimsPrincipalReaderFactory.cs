using Dorc.Api.Services;
using Dorc.Core;
using Dorc.Core.Configuration;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Caching.Memory;
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
            IConfigurationSettings config, 
            IMemoryCache cache, 
            IActiveDirectorySearcher activeDirectorySearcher
            )
        {
            _httpContextAccessor = httpContextAccessor;
            var adGroupReader = new ActiveDirectoryUserGroupReader(config, cache, activeDirectorySearcher);
            _oauthReader = new OAuthClaimsPrincipalReader(adGroupReader);
            _winAuthReader = new WinAuthClaimsPrincipalReader(adGroupReader);
        }

        public string GetUserName(IPrincipal user)
        {
            var reader = ResolveReader();
            return reader.GetUserName(user);
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
