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

            foreach (var rule in _rules)
            {
                if (rule.Match(request))
                {
                    var response = new HttpResponseMessage(rule.Status);
                    if (rule.Status != HttpStatusCode.NotFound)
                    {
                        response.Content = new StringContent(rule.Body, Encoding.UTF8, "application/json");
                    }
                    return Task.FromResult(response);
                }
            }

            // No rule matched — return 404 so unmatched calls surface as a failed test, not a hang.
            var fallback = new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent($"{{\"error\":{{\"code\":\"itemNotFound\",\"message\":\"No mock for {request.Method} {request.RequestUri}\"}}}}", Encoding.UTF8, "application/json")
            };
            return Task.FromResult(fallback);
        }

        private sealed record RouteRule(Func<HttpRequestMessage, bool> Match, string Body, HttpStatusCode Status);
    }
}
