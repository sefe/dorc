using Dorc.PersistentData;
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
            IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
            _oauthReader = new OAuthClaimsPrincipalReader();
            _winAuthReader = new WinAuthClaimsPrincipalReader();
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

        public string GetUserEmail(ClaimsPrincipal user, object externalReader)
        {
            var reader = ResolveReader();
            return reader.GetUserEmail(user, externalReader);
        }

        private IClaimsPrincipalReader ResolveReader()
        {
            var httpContext = _httpContextAccessor.HttpContext;

            // Check the authentication scheme of the current request
            string? authHeader = httpContext?.Request.Headers["Authorization"].FirstOrDefault();
            if (authHeader != null && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return _oauthReader; // Use OAuth reader
            }

            return _winAuthReader; // Fallback to WinAuth reader
        }
    }
}
