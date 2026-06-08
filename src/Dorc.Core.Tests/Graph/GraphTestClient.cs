using Microsoft.Graph;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;

namespace Dorc.Core.Tests.Graph
{
    internal static class GraphTestClient
    {
        // Constructs a GraphServiceClient whose IRequestAdapter routes all HTTP through
        // the supplied mock handler. Uses AnonymousAuthenticationProvider — the mock never
        // checks for an Authorization header.
        public static GraphServiceClient Create(MockHttpHandler handler)
        {
            var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri("https://graph.microsoft.com/")
            };
            var authProvider = new AnonymousAuthenticationProvider();
            var adapter = new HttpClientRequestAdapter(authProvider, httpClient: httpClient);
            return new GraphServiceClient(adapter);
        }
    }
}
