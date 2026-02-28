using Microsoft.AspNetCore.Http;

namespace Dorc.Api.Windows.Security
{
    public static class ContextExtensions
    {
        public static string? GetAuthenticationScheme(this HttpContext? httpContext)
        {
            return httpContext?.User?.Identity?.AuthenticationType;
        }
    }
}
