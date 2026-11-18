using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;

namespace Dorc.McpServer;

public sealed class CallerAuthorizationForwardingHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CallerAuthorizationForwardingHandler(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is not null)
        {
            var rawAuthHeader = httpContext.Request.Headers.Authorization.ToString();
            if (!string.IsNullOrWhiteSpace(rawAuthHeader) &&
                AuthenticationHeaderValue.TryParse(rawAuthHeader.Trim(), out var parsedHeader))
            {
                request.Headers.Authorization = parsedHeader;
            }
        }

        return base.SendAsync(request, cancellationToken);
    }
}
