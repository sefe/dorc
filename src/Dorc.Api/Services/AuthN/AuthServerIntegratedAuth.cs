using Microsoft.AspNetCore.Hosting.Server;

namespace Dorc.Api.Services.AuthN
{
    public class AuthServerIntegratedAuth : IServerIntegratedAuth
    {
        public static string Scheme = "AuthTestScheme";
        public bool IsEnabled => true;

        public string AuthenticationScheme => Scheme;
    }
}
