using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dorc.Api.WindowsWorker.Authentication
{
    // X-Worker-Key authentication. Per HLPS-api-split D-3:
    // - The worker binds loopback-only, so the in-scope adversary is a co-located process.
    // - The shared secret is a defence-in-depth check, validated in constant time.
    // - Missing OR wrong header => 401 with body {"error":"worker_key_invalid"}.
    public class WorkerKeyAuthenticationHandler : AuthenticationHandler<WorkerKeyAuthenticationOptions>
    {
        public const string CallerClaimType = "WorkerCaller";
        public const string CallerClaimValue = "primary";

        private readonly string? _configuredKey;

        public WorkerKeyAuthenticationHandler(
            IOptionsMonitor<WorkerKeyAuthenticationOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            IConfiguration configuration)
            : base(options, logger, encoder)
        {
            _configuredKey = configuration["WindowsWorker:SharedKey"];
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(WorkerKeyAuthenticationOptions.HeaderName, out var header) || header.Count == 0)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            if (string.IsNullOrEmpty(_configuredKey))
            {
                // Configuration missing the secret entirely — fail closed.
                return Task.FromResult(AuthenticateResult.Fail("worker_key_invalid"));
            }

            var incoming = header.ToString();
            var incomingBytes = Encoding.UTF8.GetBytes(incoming);
            var expectedBytes = Encoding.UTF8.GetBytes(_configuredKey);

            if (incomingBytes.Length != expectedBytes.Length ||
                !CryptographicOperations.FixedTimeEquals(incomingBytes, expectedBytes))
            {
                return Task.FromResult(AuthenticateResult.Fail("worker_key_invalid"));
            }

            var identity = new ClaimsIdentity(
                new[] { new Claim(CallerClaimType, CallerClaimValue) },
                WorkerKeyAuthenticationOptions.SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, WorkerKeyAuthenticationOptions.SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            Response.ContentType = "application/json";
            await Response.WriteAsync("{\"error\":\"worker_key_invalid\"}");
        }
    }
}
