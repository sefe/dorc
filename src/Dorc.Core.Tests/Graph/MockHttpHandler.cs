using System.Net;
using System.Text;

namespace Dorc.Core.Tests.Graph
{
    // Minimal HTTP test double for Microsoft.Graph 5.x (Kiota-backed). Match incoming
    // requests by predicate; return canned status + JSON. The point is to exercise the
    // real Kiota deserialization path so the test catches payload-shape mistakes that an
    // IActiveDirectorySearcher-boundary mock could not.
    internal sealed class MockHttpHandler : HttpMessageHandler
    {
        private readonly List<RouteRule> _rules = new();
        private bool _disposed;

        // Captured for test introspection (e.g. asserting on the URL/headers of an outbound call).
        // Ownership of the HttpRequestMessage instances is retained by this handler and they are
        // disposed in Dispose(disposing).
        public List<HttpRequestMessage> Requests { get; } = new();

        public MockHttpHandler Map(Func<HttpRequestMessage, bool> match, string jsonBody, HttpStatusCode status = HttpStatusCode.OK)
        {
            _rules.Add(new RouteRule(match, jsonBody, status));
            return this;
        }

        public MockHttpHandler MapPath(HttpMethod method, string pathContains, string jsonBody, HttpStatusCode status = HttpStatusCode.OK)
        {
            return Map(req =>
                req.Method == method &&
                req.RequestUri != null &&
                req.RequestUri.AbsolutePath.Contains(pathContains, StringComparison.OrdinalIgnoreCase),
                jsonBody, status);
        }

        public MockHttpHandler MapFilter(HttpMethod method, string pathContains, string filterContains, string jsonBody, HttpStatusCode status = HttpStatusCode.OK)
        {
            return Map(req =>
                req.Method == method &&
                req.RequestUri != null &&
                req.RequestUri.AbsolutePath.Contains(pathContains, StringComparison.OrdinalIgnoreCase) &&
                (req.RequestUri.Query?.Contains(filterContains, StringComparison.OrdinalIgnoreCase) ?? false),
                jsonBody, status);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);

            // Returned HttpResponseMessage ownership transfers to the Kiota request adapter,
            // which disposes it after reading the body. We intentionally do not dispose it here.
            var rule = _rules.FirstOrDefault(r => r.Match(request));
            if (rule != null)
            {
                var response = new HttpResponseMessage(rule.Status);
                if (rule.Status != HttpStatusCode.NotFound)
                {
                    response.Content = new StringContent(rule.Body, Encoding.UTF8, "application/json");
                }
                return Task.FromResult(response);
            }

            // No rule matched — return 404 so unmatched calls surface as a failed test, not a hang.
            var fallback = new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent($"{{\"error\":{{\"code\":\"itemNotFound\",\"message\":\"No mock for {request.Method} {request.RequestUri}\"}}}}", Encoding.UTF8, "application/json")
            };
            return Task.FromResult(fallback);
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                foreach (var req in Requests)
                {
                    req?.Dispose();
                }
                Requests.Clear();
                _disposed = true;
            }
            base.Dispose(disposing);
        }

        private sealed record RouteRule(Func<HttpRequestMessage, bool> Match, string Body, HttpStatusCode Status);
    }
}
