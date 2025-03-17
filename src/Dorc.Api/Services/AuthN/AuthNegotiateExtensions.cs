using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dorc.Api.Services.AuthN
{
    public static class AuthNegotiateExtensions
    {
        public static AuthenticationBuilder AddAuthNegotiate(this AuthenticationBuilder builder)
            => builder.AddAuthNegotiate(AuthServerIntegratedAuth.Scheme, "AuthTestHandler", _ => { });

        public static AuthenticationBuilder AddAuthNegotiate(this AuthenticationBuilder builder, string authenticationScheme, string? displayName, Action<AuthNegotiateOptions> configureOptions)
        {
            //builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<AuthNegotiateOptions>, PostConfigureNegotiateOptions>());
            builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupFilter>(new NegotiateOptionsValidationStartupFilter(authenticationScheme)));
            return builder.AddScheme<AuthNegotiateOptions, AuthNegotiateHandler>(authenticationScheme, displayName, configureOptions);
        }
    }

    internal sealed class NegotiateOptionsValidationStartupFilter : IStartupFilter
    {
        private readonly string _authenticationScheme;

        public NegotiateOptionsValidationStartupFilter(string authenticationScheme)
        {
            _authenticationScheme = authenticationScheme;
        }

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return builder =>
            {
                // Resolve NegotiateOptions on startup to trigger post configuration and bind LdapConnection if needed
                var options = builder.ApplicationServices.GetRequiredService<IOptionsMonitor<AuthNegotiateOptions>>().Get(_authenticationScheme);
                next(builder);
            };
        }
    }
}
