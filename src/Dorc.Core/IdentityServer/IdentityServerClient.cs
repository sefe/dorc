using Microsoft.Extensions.Logging;
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
        private readonly ILogger<IdentityServerClient> _log;
        private string? _accessToken;
        private DateTime _tokenExpiration = DateTime.MinValue;

        public IdentityServerClient(string authority, string clientId, string clientSecret, ILogger<IdentityServerClient> log)
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

            if (string.IsNullOrEmpty(_authority) || string.IsNullOrEmpty(_clientId) || string.IsNullOrEmpty(_clientSecret))
                throw new ArgumentNullException("IdentityServerClient: BaseUrl or ClientId or ClientSecret are not configured");

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
                _log.LogError(ex, "Failed to obtain access token from IdentityServer");
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

            var response = await _httpClient.PostAsJsonAsync(searchEndpoint, searchRequest);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<SearchClientsResult>();

            return result?.Items ?? new List<ClientInfo>();
        }

        public async Task<ClientInfo?> GetClientByIdAsync(string clientId)
        {
            var token = await GetAccessTokenAsync();
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var endpoint = $"{_authority}/api/Clients/{clientId}";

            var response = await _httpClient.GetAsync(endpoint);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<ClientInfo>();
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

    public class SearchClientsResult
    {
        [JsonPropertyName("totalCount")]
        public int TotalCount { get; set; }

        [JsonPropertyName("pageNumber")]
        public int PageNumber { get; set; }

        [JsonPropertyName("pageSize")]
        public int PageSize { get; set; }

        [JsonPropertyName("page")]
        public List<ClientInfo> Items { get; set; } = new List<ClientInfo>();
    }
} 