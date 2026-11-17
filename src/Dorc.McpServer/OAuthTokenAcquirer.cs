using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Dorc.McpServer;

public sealed class OAuthTokenAcquirer : IOAuthTokenAcquirer, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly DorcApiOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _syncLock = new(1, 1);

    private string? _cachedToken;
    private DateTimeOffset _expiresAtUtc = DateTimeOffset.MinValue;
    private Uri? _cachedTokenEndpoint;

    public OAuthTokenAcquirer(HttpClient httpClient, DorcApiOptions options, TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        await _syncLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = _timeProvider.GetUtcNow();
            if (!forceRefresh && !string.IsNullOrEmpty(_cachedToken) && now < _expiresAtUtc)
            {
                return _cachedToken;
            }

            if (string.IsNullOrWhiteSpace(_options.BaseUrl))
            {
                throw new InvalidOperationException("DorcApi:BaseUrl (or DORC_API_BASE_URL) is not configured.");
            }

            if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
            {
                throw new InvalidOperationException("DorcApi:ClientId and DorcApi:ClientSecret (or DORC_API_CLIENT_ID / DORC_API_CLIENT_SECRET) must be configured.");
            }

            var tokenEndpoint = await DiscoverTokenEndpointAsync(cancellationToken).ConfigureAwait(false);
            var (accessToken, expiresInSeconds) = await RequestClientCredentialsTokenAsync(tokenEndpoint, cancellationToken).ConfigureAwait(false);

            _cachedToken = accessToken;
            var safetyMarginSeconds = Math.Min(60, Math.Max(0, expiresInSeconds / 2));
            _expiresAtUtc = _timeProvider.GetUtcNow().AddSeconds(Math.Max(1, expiresInSeconds - safetyMarginSeconds));

            return _cachedToken;
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private async Task<Uri> DiscoverTokenEndpointAsync(CancellationToken cancellationToken)
    {
        if (_cachedTokenEndpoint is not null)
        {
            return _cachedTokenEndpoint;
        }

        var baseUri = new Uri(_options.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        var metadataUri = new Uri(baseUri, "Metadata");

        using var metadataResponse = await _httpClient.GetAsync(metadataUri, cancellationToken).ConfigureAwait(false);
        metadataResponse.EnsureSuccessStatusCode();

        var metadataJson = await metadataResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var metadataDoc = JsonDocument.Parse(metadataJson);
        if (!metadataDoc.RootElement.TryGetProperty("Authority", out var authorityProp) ||
            string.IsNullOrWhiteSpace(authorityProp.GetString()))
        {
            throw new InvalidOperationException("DOrc /Metadata response did not contain a valid 'Authority' property.");
        }

        var authorityBase = new Uri(authorityProp.GetString()!.TrimEnd('/') + "/", UriKind.Absolute);
        var discoveryUri = new Uri(authorityBase, ".well-known/openid-configuration");

        using var discoveryResponse = await _httpClient.GetAsync(discoveryUri, cancellationToken).ConfigureAwait(false);
        discoveryResponse.EnsureSuccessStatusCode();

        var discoveryJson = await discoveryResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var discoveryDoc = JsonDocument.Parse(discoveryJson);
        if (!discoveryDoc.RootElement.TryGetProperty("token_endpoint", out var tokenEndpointProp) ||
            string.IsNullOrWhiteSpace(tokenEndpointProp.GetString()))
        {
            throw new InvalidOperationException("OpenID Connect discovery document did not contain a valid 'token_endpoint'.");
        }

        _cachedTokenEndpoint = new Uri(tokenEndpointProp.GetString()!, UriKind.Absolute);
        return _cachedTokenEndpoint;
    }

    private async Task<(string AccessToken, int ExpiresInSeconds)> RequestClientCredentialsTokenAsync(
        Uri tokenEndpoint,
        CancellationToken cancellationToken)
    {
        var scope = string.IsNullOrWhiteSpace(_options.Scope) ? "dorc-api.manage" : _options.Scope.Trim();
        var formFields = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["scope"] = scope
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint)
        {
            Content = new FormUrlEncodedContent(formFields)
        };

        var credentialsBytes = Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(credentialsBytes));

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var tokenJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var tokenDoc = JsonDocument.Parse(tokenJson);

        if (!tokenDoc.RootElement.TryGetProperty("access_token", out var accessTokenProp) ||
            string.IsNullOrWhiteSpace(accessTokenProp.GetString()))
        {
            throw new InvalidOperationException("OAuth token endpoint response did not contain a valid 'access_token'.");
        }

        var expiresIn = 3600;
        if (tokenDoc.RootElement.TryGetProperty("expires_in", out var expiresInProp) &&
            expiresInProp.ValueKind == JsonValueKind.Number &&
            expiresInProp.TryGetInt32(out var parsedExpiresIn) &&
            parsedExpiresIn > 0)
        {
            expiresIn = parsedExpiresIn;
        }

        return (accessTokenProp.GetString()!, expiresIn);
    }

    public void Dispose()
    {
        _syncLock.Dispose();
    }
}
