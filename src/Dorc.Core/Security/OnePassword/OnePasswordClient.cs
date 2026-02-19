using System.Net.Http.Headers;
using System.Text.Json;

namespace Dorc.Core.Security.OnePassword
{
    public class OnePasswordClient
    {
        private readonly HttpClient _httpClient;

        public OnePasswordClient(string baseUrl, string apiKey)
        {
            if (string.IsNullOrEmpty(baseUrl))
            {
                throw new ArgumentNullException(nameof(baseUrl));
            }
            if (string.IsNullOrEmpty(apiKey))
            {
                throw new ArgumentNullException(nameof(apiKey));
            }

            _httpClient = new HttpClient()
            {
                BaseAddress = new Uri(baseUrl)
            };

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        public async Task<string> GetSecretValueAsync(string vaultId, string itemId)
        {
            OnePasswordItem? secretItem = await this.GetItemAsync(vaultId, itemId);
            if (secretItem != null)
            {
                switch (secretItem.Category?.ToUpper())
                {
                    case "API_CREDENTIAL":
                        return secretItem.GetFieldValue("credential");
                    case "LOGIN":
                        return secretItem.GetFieldValue("password");
                    default:
                        return string.Empty;
                }
            }
            return string.Empty;
        }

        public async Task<OnePasswordItem?> GetItemAsync(string vaultId, string itemId)
        {
            Uri uri = new($"{_httpClient.BaseAddress?.ToString().TrimEnd('/')}/v1/vaults/{vaultId}/items/{itemId}");

            HttpResponseMessage response = await _httpClient.GetAsync(uri);
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Error fetching secret: {response.StatusCode}");
            }

            string json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<OnePasswordItem>(json);
        }
    }
}
