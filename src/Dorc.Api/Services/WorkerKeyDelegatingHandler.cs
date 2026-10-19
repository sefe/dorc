using Microsoft.Extensions.Configuration;

namespace Dorc.Api.Services
{
    // DelegatingHandler that attaches the X-Worker-Key shared secret to every
    // outbound HTTP call to the Windows worker. Constant string read from
    // WindowsWorker:SharedKey config — never derived from caller state.
    public class WorkerKeyDelegatingHandler : DelegatingHandler
    {
        public const string HeaderName = "X-Worker-Key";

        private readonly string? _sharedKey;

        public WorkerKeyDelegatingHandler(IConfiguration configuration)
        {
            _sharedKey = configuration["WindowsWorker:SharedKey"];
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrEmpty(_sharedKey))
            {
                request.Headers.Remove(HeaderName);
                request.Headers.Add(HeaderName, _sharedKey);
            }
            return base.SendAsync(request, cancellationToken);
        }
    }
}
