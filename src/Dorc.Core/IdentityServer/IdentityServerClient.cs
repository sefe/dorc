using log4net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Dorc.Core.IdentityServer
{
    public class IdentityServerClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _authority;
        private readonly string _clientId;
        private readonly string _clientSecret;
        private readonly ILog _log;
        private string? _accessToken;
        private DateTime _tokenExpiration = DateTime.MinValue;

        public IdentityServerClient(string authority, string clientId, string clientSecret, ILog log)
        {
            _authority = authority.TrimEnd('/');
            _clientId = clientId;
            _clientSecret = clientSecret;
            _log = log;

            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        private async Task<string> GetAccessTokenAsync()
        {
            if (!string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _tokenExpiration)
            {
                return _accessToken;
            }

            var tokenEndpoint = $"{_authority}/connect/token";
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials"),
                new KeyValuePair<string, string>("client_id", _clientId),
                new KeyValuePair<string, string>("client_secret", _clientSecret)
            });

            try
            {
                var response = await _httpClient.PostAsync(tokenEndpoint, content);
                response.EnsureSuccessStatusCode();

                var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>();
                if (tokenResponse == null)
                {
                    throw new Exception("Failed to deserialize token response");
                }

                _accessToken = tokenResponse.AccessToken;
                _tokenExpiration = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn - 60); // Subtract 60 seconds for safety
                return _accessToken;
            }
            catch (Exception ex)
            {
                _log.Error("Failed to obtain access token from IdentityServer", ex);
                throw;
            }
        }

        public async Task<List<ClientInfo>> SearchClientsAsync(string searchTerm, int page = 1, int pageSize = 10)
        {
            var token = await GetAccessTokenAsync();
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var searchEndpoint = $"{_authority}/api/Clients/search";
            var searchRequest = new
            {
                searchTerm,
                page,
                pageSize
            };

            try
            {
                var response = await _httpClient.PostAsJsonAsync(searchEndpoint, searchRequest);
                response.EnsureSuccessStatusCode();

                var result = await response.Content.ReadFromJsonAsync<List<ClientInfo>>();
                return result ?? new List<ClientInfo>();
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to search clients in IdentityServer: {ex.Message}", ex);
                throw;
            }
        }

        public async Task<ClientInfo?> GetClientByIdAsync(string clientId)
        {
            var token = await GetAccessTokenAsync();
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var endpoint = $"{_authority}/api/Clients/{clientId}";

            try
            {
                var response = await _httpClient.GetAsync(endpoint);
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return null;
                }
                response.EnsureSuccessStatusCode();

                return await response.Content.ReadFromJsonAsync<ClientInfo>();
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to get client from IdentityServer: {ex.Message}", ex);
                throw;
            }
        }

        private class TokenResponse
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

    public class ClientInfo
    {
        [JsonPropertyName("clientId")]
        public string ClientId { get; set; } = string.Empty;

        [JsonPropertyName("clientName")]
        public string ClientName { get; set; } = string.Empty;
    }
} 