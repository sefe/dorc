using System.Net;
using System.Text;

namespace Dorc.McpServer.Tests;

internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;
    private readonly List<HttpResponseMessage> _createdResponses = [];

    public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
    {
        _responder = responder;
    }

    public List<HttpRequestMessage> SentRequests { get; } = [];

    public static HttpResponseMessage CreateJsonResponse(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    public static HttpResponseMessage CreateTextResponse(
        string text,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string? mediaType = "text/plain",
        string? reasonPhrase = null)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(text, Encoding.UTF8, mediaType ?? "text/plain")
        };
        if (mediaType is null)
        {
            response.Content.Headers.ContentType = null;
        }

        if (reasonPhrase is not null)
        {
            response.ReasonPhrase = reasonPhrase;
        }

        return response;
    }

    public static HttpResponseMessage CreateStatusResponse(HttpStatusCode statusCode)
    {
        return new HttpResponseMessage(statusCode);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        SentRequests.Add(request);
        var response = await _responder(request, cancellationToken).ConfigureAwait(false);
        _createdResponses.Add(response);
        return response;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var response in _createdResponses)
            {
                response.Dispose();
            }

            _createdResponses.Clear();
        }

        base.Dispose(disposing);
    }
}
