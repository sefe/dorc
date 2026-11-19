using System.Net;
using System.Net.Http.Headers;

namespace Dorc.McpServer;

public sealed class BearerTokenRefreshingHandler : DelegatingHandler
{
    private readonly IOAuthTokenAcquirer _tokenAcquirer;

    public BearerTokenRefreshingHandler(IOAuthTokenAcquirer tokenAcquirer)
    {
        _tokenAcquirer = tokenAcquirer ?? throw new ArgumentNullException(nameof(tokenAcquirer));
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Headers.Authorization is null)
        {
            var token = await _tokenAcquirer.GetAccessTokenAsync(forceRefresh: false, cancellationToken).ConfigureAwait(false);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        else
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        byte[]? bufferedContent = null;
        string? contentType = null;
        if (request.Content is not null)
        {
            contentType = request.Content.Headers.ContentType?.ToString();
            bufferedContent = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        response.Dispose();

        var refreshedToken = await _tokenAcquirer.GetAccessTokenAsync(forceRefresh: true, cancellationToken).ConfigureAwait(false);
        using var retryRequest = CloneHttpRequestMessage(request, bufferedContent, contentType);
        retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refreshedToken);

        return await base.SendAsync(retryRequest, cancellationToken).ConfigureAwait(false);
    }

    private static HttpRequestMessage CloneHttpRequestMessage(
        HttpRequestMessage original,
        byte[]? bufferedContent,
        string? contentType)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri)
        {
            Version = original.Version
        };

        foreach (var header in original.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (bufferedContent is not null)
        {
            clone.Content = new ByteArrayContent(bufferedContent);
            if (!string.IsNullOrEmpty(contentType))
            {
                clone.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
            }
        }

        return clone;
    }
}
