using Dorc.Core.Configuration;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dorc.Core
{
    public sealed class DorcApiTokenProvider : IAsyncDisposable
    {
        private readonly IOAuthClientConfiguration _config;
        private readonly HttpClient _http;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        private Uri? _authority;
        private string? _accessToken;
        private DateTime _expiresAtUtc;

        public DorcApiTokenProvider(IOAuthClientConfiguration config)
        {
            _config = config;
            _http = new HttpClient();
        }

        public async Task<string> GetTokenAsync()
        {
            if (_accessToken != null && DateTime.UtcNow < _expiresAtUtc)
            {
                return _accessToken;
            }

            await _lock.WaitAsync();
            try
            {
                if (_accessToken != null && DateTime.UtcNow < _expiresAtUtc)
                {
                    return _accessToken;
                }

                await EnsureAuthorityAsync(_config.BaseUrl);

                if (_authority is null)
                {
                    throw new InvalidOperationException("OAuth authority could not be resolved from RefData API.");
                }
                
                var tokenEndpoint = new Uri(_authority!, "/connect/token").ToString();
                using var form = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string,string>("grant_type", "client_credentials"),
                    new KeyValuePair<string,string>("client_id", _config.ClientId),
                    new KeyValuePair<string,string>("client_secret", _config.ClientSecret),
                    new KeyValuePair<string,string>("scope", _config.Scope)
                });

                HttpResponseMessage response;
                try
                {
                    response = await _http.PostAsync(tokenEndpoint, form);
                }
                catch (Exception ex)
                {
                    throw new ApplicationException($"Network error calling token endpoint '{tokenEndpoint}'", ex);
                }

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    throw new ApplicationException($"Token request failed ({(int)response.StatusCode}) body='{body}'");
                }

                var json = await response.Content.ReadAsStringAsync();
                TokenResponse? token;
                try
                {
                    token = JsonSerializer.Deserialize<TokenResponse>(json);
                }
                catch (Exception ex)
                {
                    throw new ApplicationException("Failed to deserialize token response JSON.", ex);
                    throw;
                }

                if (token == null || string.IsNullOrWhiteSpace(token.AccessToken))
                {
                    throw new InvalidOperationException("Token response missing access_token.");
                }

                _accessToken = token.AccessToken;
                var expiresIn = token.ExpiresIn > 120 ? token.ExpiresIn - 60 : token.ExpiresIn; // renew 60s early if possible
                _expiresAtUtc = DateTime.UtcNow.AddSeconds(expiresIn);
                return _accessToken;
            }
            finally
            {
                _lock.Release();
            }
        }

        private async Task EnsureAuthorityAsync(string dorcBaseApiUrl)
        {
            if (_authority is not null)
                return;

            var baseUri = new Uri(dorcBaseApiUrl, UriKind.Absolute);
            var refDataUrl = new Uri(baseUri, "ApiConfig").ToString();
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, refDataUrl);
                var resp = await _http.SendAsync(req);
                resp.EnsureSuccessStatusCode();

                var root = await resp.Content.ReadFromJsonAsync<RefDataRoot>();
                if (root == null || string.IsNullOrWhiteSpace(root.OAuthAuthority))
                {
                    throw new InvalidOperationException("RefData root response did not contain OAuthAuthority.");
                }
                _authority = new Uri(root.OAuthAuthority, UriKind.Absolute);
            }
            catch (Exception ex)
            {
                throw new ApplicationException($"Failed to resolve OAuthAuthority from '{refDataUrl}'", ex);
            }
        }

        public async ValueTask DisposeAsync()
        {
            _lock.Dispose();
            _http.Dispose();
            await Task.CompletedTask;
        }

        private sealed class RefDataRoot
        {
            public string? OAuthAuthority { get; set; }
        }

        private sealed class TokenResponse
        {
            [JsonPropertyName("access_token")]
            public string AccessToken { get; set; } = string.Empty;

            [JsonPropertyName("expires_in")]
            public int ExpiresIn { get; set; }

            [JsonPropertyName("token_type")]
            public string TokenType { get; set; } = string.Empty;

            [JsonPropertyName("scope")]
            public string Scope { get; set; } = string.Empty;

        }
    }
}


