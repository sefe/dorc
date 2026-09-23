using Microsoft.AspNetCore.Authentication;

namespace Dorc.Api.WindowsWorker.Authentication
{
    // Scheme constants and options for the X-Worker-Key authentication scheme.
    // Reserved as a class (rather than just a constants holder) so future per-route
    // options can be added without changing the registration site.
    public class WorkerKeyAuthenticationOptions : AuthenticationSchemeOptions
    {
        public const string SchemeName = "WorkerKey";
        public const string HeaderName = "X-Worker-Key";
    }
}
