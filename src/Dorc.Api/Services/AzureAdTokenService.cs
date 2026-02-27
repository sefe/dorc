using System.Text.Json;
using Dorc.Api.Interfaces;
using Dorc.Core.Configuration;

namespace Dorc.Api.Services
{
    /// <summary>
    /// Acquires Azure AD bearer tokens using the OAuth2 client credentials flow.
    /// Reused by ServiceNow integration and Microsoft Graph email sending.
    /// </summary>
    public class AzureAdTokenService : IAzureAdTokenService
    {
        private readonly ILogger<AzureAdTokenService> _logger;
        private readonly IConfigurationSettings _configurationSettings;

        public AzureAdTokenService(
            ILogger<AzureAdTokenService> logger,
            IConfigurationSettings configurationSettings)
        {
            _logger = logger;
            _configurationSettings = configurationSettings;
        }

        private static string Sanitize(string? value) =>
            (value ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);

        public async Task<string?> GetTokenAsync(string scope)
        {
            var safeScope = Sanitize(scope);

            var tenantId = _configurationSettings.GetAzureEntraTenantId();
            var clientId = _configurationSettings.GetAzureEntraClientId();
            var clientSecret = _configurationSettings.GetAzureEntraClientSecret();

            if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            {
                _logger.LogWarning("Azure AD credentials (TenantId/ClientId/ClientSecret) not fully configured");
                return null;
            }

            var tokenUrl = $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token";
            var body = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials"),
                new KeyValuePair<string, string>("client_id", clientId),
                new KeyValuePair<string, string>("client_secret", clientSecret),
                new KeyValuePair<string, string>("scope", scope)
            });

            using var httpClient = new HttpClient();
            var response = await httpClient.PostAsync(tokenUrl, body);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to acquire Azure AD token for scope '{Scope}'. Status: {Status}, Response: {Response}",
                    safeScope, response.StatusCode, content.Length > 500 ? content[..500] : content);
                return null;
            }

            var json = JsonSerializer.Deserialize<JsonElement>(content);
            if (json.TryGetProperty("access_token", out var accessToken))
            {
                _logger.LogDebug("Azure AD bearer token acquired for scope '{Scope}'", safeScope);
                return accessToken.GetString();
            }

            _logger.LogError("Azure AD token response missing access_token property");
            return null;
        }
    }
}
