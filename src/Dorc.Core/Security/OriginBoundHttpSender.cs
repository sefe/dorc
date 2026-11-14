using System.Net;
using System.Net.Http.Headers;
using Dorc.ApiModel;

namespace Dorc.Core.Security
{
    internal static class OriginBoundHttpSender
    {
        private const int MaxRedirects = 5;

        public static async Task<HttpResponseMessage> GetAsync(
            HttpClient client,
            Uri initialUri,
            AuthenticationHeaderValue? authorization,
            HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead,
            CancellationToken cancellationToken = default,
            Action<Uri>? onRedirect = null)
        {
            var currentUri = initialUri;

            for (var hop = 0; hop <= MaxRedirects; hop++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, currentUri);
                if (authorization != null && SourceHost.HasSameOrigin(currentUri.AbsoluteUri, initialUri.AbsoluteUri))
                {
                    request.Headers.Authorization = authorization;
                }

                var response = await client.SendAsync(request, completionOption, cancellationToken).ConfigureAwait(false);
                if (!IsRedirect(response.StatusCode) || response.Headers.Location == null)
                {
                    return response;
                }

                var location = response.Headers.Location;
                var nextUri = location.IsAbsoluteUri ? location : new Uri(currentUri, location);
                response.Dispose();

                if (string.Equals(currentUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(nextUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Refusing HTTPS-to-{nextUri.Scheme} redirect from '{currentUri.Host}' to '{nextUri}'.");
                }

                if (!string.Equals(nextUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(nextUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Refusing redirect to unsupported scheme '{nextUri.Scheme}'.");
                }

                onRedirect?.Invoke(nextUri);
                currentUri = nextUri;
            }

            throw new InvalidOperationException(
                $"Exceeded maximum of {MaxRedirects} HTTP redirects starting from '{initialUri.Host}'.");
        }

        private static bool IsRedirect(HttpStatusCode statusCode) =>
            statusCode is HttpStatusCode.MovedPermanently
                or HttpStatusCode.Redirect
                or HttpStatusCode.RedirectMethod
                or HttpStatusCode.TemporaryRedirect
                or HttpStatusCode.PermanentRedirect;
    }
}
