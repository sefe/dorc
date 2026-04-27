using System.Net;
using Microsoft.Extensions.Logging;

namespace Dorc.Core.BuildServer
{
    /// <summary>
    /// DelegatingHandler that retries GitHub API requests on transient failures
    /// (429, 502, 503, 504) with exponential backoff. Respects GitHub's Retry-After
    /// header for rate limiting.
    /// </summary>
    public class GitHubRetryHandler : DelegatingHandler
    {
        private const int MaxRetries = 3;
        private static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(1);

        private readonly ILogger<GitHubRetryHandler> _logger;

        public GitHubRetryHandler(ILogger<GitHubRetryHandler> logger)
        {
            _logger = logger;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Only retry idempotent requests without a body — retrying POST/PUT with a consumed
            // content stream would send an empty body, silently corrupting the request.
            if (request.Content != null)
                return await base.SendAsync(request, cancellationToken);

            for (var attempt = 0; attempt <= MaxRetries; attempt++)
            {
                // HttpRequestMessage cannot be sent more than once — sending the same instance
                // twice throws InvalidOperationException. Clone for each attempt.
                using var attemptRequest = CloneRequest(request);
                var response = await base.SendAsync(attemptRequest, cancellationToken);

                if (!IsTransientFailure(response.StatusCode) || attempt == MaxRetries)
                    return response;

                var delay = GetRetryDelay(response, attempt);
                _logger.LogWarning("GitHub API returned {StatusCode}, retrying in {Delay}ms (attempt {Attempt}/{MaxRetries})",
                    (int)response.StatusCode, delay.TotalMilliseconds, attempt + 1, MaxRetries);

                response.Dispose();
                await Task.Delay(delay, cancellationToken);
            }

            // Unreachable, but satisfies compiler
            using var fallbackRequest = CloneRequest(request);
            return await base.SendAsync(fallbackRequest, cancellationToken);
        }

        private static HttpRequestMessage CloneRequest(HttpRequestMessage original)
        {
            var clone = new HttpRequestMessage(original.Method, original.RequestUri)
            {
                Version = original.Version,
                VersionPolicy = original.VersionPolicy
            };
            foreach (var header in original.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            foreach (var option in original.Options)
            {
                ((IDictionary<string, object?>)clone.Options)[option.Key] = option.Value;
            }
            return clone;
        }

        private static bool IsTransientFailure(HttpStatusCode statusCode)
        {
            return statusCode == HttpStatusCode.TooManyRequests ||
                   statusCode == HttpStatusCode.BadGateway ||
                   statusCode == HttpStatusCode.ServiceUnavailable ||
                   statusCode == HttpStatusCode.GatewayTimeout;
        }

        private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
        {
            // Respect Retry-After header if present (GitHub sends this with 429)
            if (response.Headers.RetryAfter?.Delta is { } retryAfter)
                return retryAfter;

            if (response.Headers.RetryAfter?.Date is { } retryDate)
            {
                var delay = retryDate - DateTimeOffset.UtcNow;
                if (delay > TimeSpan.Zero)
                    return delay;
            }

            // Exponential backoff: 1s, 2s, 4s
            return TimeSpan.FromTicks(BaseDelay.Ticks * (1L << attempt));
        }
    }
}
