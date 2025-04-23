using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.Negotiate;

namespace Dorc.Api.Security
{
    public static class ContextExtensions
    {
        public static string? GetAuthenticationScheme(this HttpContext? httpContext)
        {
            if (httpContext == null)
            {
                throw new ArgumentException("HttpContext must not be null");
            }

            string? authHeader = httpContext.Request.Headers["Authorization"].FirstOrDefault();

            if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return JwtBearerDefaults.AuthenticationScheme;
            }

            return NegotiateDefaults.AuthenticationScheme;
        }
    }
}
