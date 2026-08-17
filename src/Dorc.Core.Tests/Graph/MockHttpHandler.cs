using System.Net;
using System.Text;
using System.Web;

namespace Dorc.Core.Tests.Graph
{
    // Minimal HTTP test double for Microsoft.Graph 5.x (Kiota-backed). Match incoming
    // requests by predicate; return canned status + JSON. The point is to exercise the
    // real Kiota deserialization path so the test catches payload-shape mistakes that an
    // IActiveDirectorySearcher-boundary mock could not.
    //
    // Requests are captured as immutable records (not the HttpRequestMessage itself, whose
    // content stream is consumed and disposed downstream) so tests can assert on what the
    // production code actually SENT. Response-shape assertions alone let a completely wrong
    // $filter pass, which is how an earlier revision of this harness let mutations survive.
    internal sealed class MockHttpHandler : HttpMessageHandler
    {
        private readonly List<RouteRule> _rules = new();

        public List<CapturedRequest> Captured { get; } = new();

        /// <summary>
        /// When true, a request matching no rule throws instead of answering 404. 404 is a
        /// meaningful status in this domain (it drives the SID fallback chain and is swallowed
        /// by the GetSidsForUser self-lookup), so a silent 404 makes "you forgot to mock this"
        /// indistinguishable from "the code correctly probed and missed".
        /// </summary>
        public bool ThrowOnUnmatched { get; set; }

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

        /// <summary>
        /// Matches on the decoded <c>$filter</c> query parameter specifically — NOT on the raw
        /// query string. Matching the whole query is unsound here because the production
        /// <c>$select</c> lists frequently contain the same property names as the filter
        /// (e.g. onPremisesSecurityIdentifier), so a rule would match even when the filter was
        /// wrong or absent.
        /// </summary>
        public MockHttpHandler MapFilter(HttpMethod method, string pathContains, string filterContains, string jsonBody, HttpStatusCode status = HttpStatusCode.OK)
        {
            return Map(req =>
                req.Method == method &&
                req.RequestUri != null &&
                req.RequestUri.AbsolutePath.Contains(pathContains, StringComparison.OrdinalIgnoreCase) &&
                (FilterOf(req.RequestUri)?.Contains(filterContains, StringComparison.OrdinalIgnoreCase) ?? false),
                jsonBody, status);
        }

        /// <summary>Decoded <c>$filter</c> of the last captured request whose path contains <paramref name="pathContains"/>.</summary>
        public string? LastFilter(string pathContains) => LastRequest(pathContains)?.Filter;

        /// <summary>Decoded <c>$select</c> of the last captured request whose path contains <paramref name="pathContains"/>.</summary>
        public string? LastSelect(string pathContains) => LastRequest(pathContains)?.Select;

        public CapturedRequest? LastRequest(string pathContains) =>
            Captured.LastOrDefault(c => c.Path.Contains(pathContains, StringComparison.OrdinalIgnoreCase));

        public int CountFor(string pathContains) =>
            Captured.Count(c => c.Path.Contains(pathContains, StringComparison.OrdinalIgnoreCase));

        private static string? FilterOf(Uri uri) => QueryParam(uri, "$filter");
        private static string? SelectOf(Uri uri) => QueryParam(uri, "$select");

        private static string? QueryParam(Uri uri, string name)
        {
            if (string.IsNullOrEmpty(uri.Query)) return null;
            var parsed = HttpUtility.ParseQueryString(uri.Query);
            return parsed[name];
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri;
            var body = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();

            Captured.Add(new CapturedRequest(
                request.Method.Method,
                uri?.AbsolutePath ?? string.Empty,
                uri?.Query ?? string.Empty,
                uri == null ? null : FilterOf(uri),
                uri == null ? null : SelectOf(uri),
                body));

            // Returned HttpResponseMessage ownership transfers to the Kiota request adapter,
            // which disposes it after reading the body. We intentionally do not dispose it here.
            var rule = _rules.FirstOrDefault(r => r.Match(request));
            if (rule != null)
            {
                // Always emit a body, including for 404: real Graph 404s carry an OData error
                // payload and surface as ODataError (an ApiException subclass). A bodiless 404
                // yields a bare ApiException, so the suite would not exercise the real shape.
                var response = new HttpResponseMessage(rule.Status)
                {
                    Content = new StringContent(
                        string.IsNullOrEmpty(rule.Body) ? NotFoundBody(request) : rule.Body,
                        Encoding.UTF8,
                        "application/json")
                };
                return Task.FromResult(response);
            }

            if (ThrowOnUnmatched)
            {
                return Task.FromException<HttpResponseMessage>(
                    new InvalidOperationException($"Unmocked Graph call: {request.Method} {request.RequestUri}"));
            }

            var fallback = new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent(NotFoundBody(request), Encoding.UTF8, "application/json")
            };
            return Task.FromResult(fallback);
        }

        private static string NotFoundBody(HttpRequestMessage request) =>
            $"{{\"error\":{{\"code\":\"Request_ResourceNotFound\",\"message\":\"No mock for {request.Method} {request.RequestUri}\"}}}}";

        private sealed record RouteRule(Func<HttpRequestMessage, bool> Match, string Body, HttpStatusCode Status);
    }

    internal sealed record CapturedRequest(
        string Method,
        string Path,
        string Query,
        string? Filter,
        string? Select,
        string? Body);
}
