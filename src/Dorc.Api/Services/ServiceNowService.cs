using System.Net.Http.Headers;
using System.Text.Json;
using Dorc.Api.Interfaces;
using Dorc.Core.Configuration;
using Dorc.PersistentData.Sources.Interfaces;

namespace Dorc.Api.Services
{
    /// <summary>
    /// ServiceNow Change Request response model.
    /// With sysparm_display_value=true:
    ///   - Choice fields (state, approval) are returned as display strings
    ///   - Reference fields (business_service) may still be returned as objects
    ///     with "link" and "value" properties, so we use JsonElement to handle
    ///     both string and object shapes safely.
    /// </summary>
    public class SnChangeRequestResponseModel
    {
        public string number { get; set; } = string.Empty;
        public string state { get; set; } = string.Empty;
        public string short_description { get; set; } = string.Empty;
        public string? start_date { get; set; }
        public string? end_date { get; set; }
        public string? approval { get; set; }
        // Reference fields: may be a string OR an object { "link": "...", "value": "..." }
        public JsonElement? business_service { get; set; }

        /// <summary>
        /// Safely extracts a display string from a JsonElement that could be
        /// a plain string or a reference object with "display_value"/"value" keys.
        /// </summary>
        public static string GetDisplayValue(JsonElement? element, string fallback = "N/A")
        {
            if (element == null) return fallback;
            var el = element.Value;
            if (el.ValueKind == JsonValueKind.String)
            {
                var s = el.GetString();
                return string.IsNullOrWhiteSpace(s) ? fallback : s;
            }
            if (el.ValueKind == JsonValueKind.Object)
            {
                if (el.TryGetProperty("display_value", out var dv) && dv.ValueKind == JsonValueKind.String)
                {
                    var s = dv.GetString();
                    return string.IsNullOrWhiteSpace(s) ? fallback : s;
                }
                if (el.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String)
                {
                    var s = v.GetString();
                    return string.IsNullOrWhiteSpace(s) ? fallback : s;
                }
            }
            return fallback;
        }
    }

    public class ResultArray<T>
    {
        public T[] result { get; set; } = Array.Empty<T>();
    }

    /// <summary>
    /// ServiceNow integration service that uses DOrc configuration.
    /// Authentication uses Azure AD bearer token + APIM subscription key,
    /// matching the DBAChangeRequestPortal pattern.
    /// 
    /// Required DOrc Config Values (in ConfigValue table):
    /// - UseServiceNow: Set to "true" to enable CR validation
    /// - ServiceNowApiUrl: APIM gateway URL (e.g., https://apim-np-servicenow-uks.azure-api.net)
    /// - ServiceNowApiSubscriptionKey: APIM subscription key
    /// - ServiceNowAadScope: Azure AD scope for token acquisition (e.g., {app-id}/.default)
    /// 
    /// Azure AD credentials (TenantId, ClientId, ClientSecret) come from appsettings.json
    /// via IConfigurationSettings.
    /// </summary>
    public class ServiceNowService : IServiceNowService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<ServiceNowService> _logger;
        private readonly IConfigValuesPersistentSource _configValuesPersistentSource;
        private readonly IConfigurationSettings _configurationSettings;
        
        // DOrc Config Value Keys
        private const string ConfigKey_UseServiceNow = "UseServiceNow";
        private const string ConfigKey_ApiUrl = "ServiceNowApiUrl";
        private const string ConfigKey_SubscriptionKey = "ServiceNowApiSubscriptionKey";
        private const string ConfigKey_AadScope = "ServiceNowAadScope";

        public ServiceNowService(
            HttpClient httpClient,
            ILogger<ServiceNowService> logger,
            IConfigValuesPersistentSource configValuesPersistentSource,
            IConfigurationSettings configurationSettings)
        {
            _httpClient = httpClient;
            _logger = logger;
            _configValuesPersistentSource = configValuesPersistentSource;
            _configurationSettings = configurationSettings;
        }

        private bool IsServiceNowEnabled()
        {
            var enabled = _configValuesPersistentSource.GetConfigValue(ConfigKey_UseServiceNow, "false");
            return bool.TryParse(enabled, out var result) && result;
        }

        private string GetServiceNowApiUrl()
        {
            return _configValuesPersistentSource.GetConfigValue(ConfigKey_ApiUrl, string.Empty);
        }

        private string GetSubscriptionKey()
        {
            return _configValuesPersistentSource.GetConfigValue(ConfigKey_SubscriptionKey, string.Empty);
        }

        private string GetAadScope()
        {
            return _configValuesPersistentSource.GetConfigValue(ConfigKey_AadScope, string.Empty);
        }

        /// <summary>
        /// Acquires an Azure AD bearer token using client credentials flow.
        /// </summary>
        private async Task<string?> GetBearerTokenAsync()
        {
            var scope = GetAadScope();
            if (string.IsNullOrEmpty(scope))
            {
                _logger.LogWarning("ServiceNowAadScope not configured - cannot acquire bearer token");
                return null;
            }

            var tenantId = _configurationSettings.GetAzureEntraTenantId();
            var clientId = _configurationSettings.GetAzureEntraClientId();
            var clientSecret = _configurationSettings.GetAzureEntraClientSecret();

            if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            {
                _logger.LogWarning("Azure AD credentials (TenantId/ClientId/ClientSecret) not fully configured");
                return null;
            }

            var tokenUrl = $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token";
            var tokenRequestBody = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials"),
                new KeyValuePair<string, string>("client_id", clientId),
                new KeyValuePair<string, string>("client_secret", clientSecret),
                new KeyValuePair<string, string>("scope", scope)
            });

            // Use a separate HttpClient for token acquisition (don't use the one configured for ServiceNow)
            using var tokenClient = new HttpClient();
            var tokenResponse = await tokenClient.PostAsync(tokenUrl, tokenRequestBody);
            var tokenContent = await tokenResponse.Content.ReadAsStringAsync();

            if (!tokenResponse.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to acquire Azure AD token. Status: {Status}, Response: {Response}",
                    tokenResponse.StatusCode, tokenContent.Length > 500 ? tokenContent[..500] : tokenContent);
                return null;
            }

            var tokenResult = JsonSerializer.Deserialize<JsonElement>(tokenContent);
            if (tokenResult.TryGetProperty("access_token", out var accessToken))
            {
                _logger.LogDebug("Azure AD bearer token acquired successfully");
                return accessToken.GetString();
            }

            _logger.LogError("Azure AD token response missing access_token property");
            return null;
        }

        private async Task ConfigureHttpClientAsync(string apiUrl, string subscriptionKey)
        {
            if (!string.IsNullOrEmpty(apiUrl))
            {
                // BaseAddress MUST end with '/' for relative URI resolution to work correctly.
                if (!apiUrl.EndsWith('/'))
                    apiUrl += '/';
                _httpClient.BaseAddress = new Uri(apiUrl);
            }

            // Add subscription key header for APIM authentication
            if (!string.IsNullOrEmpty(subscriptionKey))
            {
                _httpClient.DefaultRequestHeaders.Add("subscription-key", subscriptionKey);
            }

            // Acquire and add Azure AD bearer token
            var bearerToken = await GetBearerTokenAsync();
            if (!string.IsNullOrEmpty(bearerToken))
            {
                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", bearerToken);
            }

            _httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public async Task<ChangeRequestValidationResult> ValidateChangeRequestAsync(string crNumber)
        {
            if (string.IsNullOrWhiteSpace(crNumber))
            {
                return new ChangeRequestValidationResult
                {
                    IsValid = false,
                    Message = "Change Request number is required",
                    CrNumber = crNumber ?? string.Empty
                };
            }

            // Sanitize and normalize CR number format to prevent log forging
            crNumber = crNumber
                .Replace("\r", string.Empty)
                .Replace("\n", string.Empty)
                .Trim()
                .ToUpperInvariant();

            // Check if ServiceNow integration is enabled
            if (!IsServiceNowEnabled())
            {
                _logger.LogDebug("ServiceNow CR validation is disabled - skipping validation for {CrNumber}", crNumber);
                return new ChangeRequestValidationResult
                {
                    IsValid = true,
                    Message = "ServiceNow validation skipped (not enabled)",
                    CrNumber = crNumber
                };
            }

            var apiUrl = GetServiceNowApiUrl();
            if (string.IsNullOrEmpty(apiUrl))
            {
                _logger.LogWarning("ServiceNow API URL not configured - skipping CR validation for {CrNumber}", crNumber);
                return new ChangeRequestValidationResult
                {
                    IsValid = true,
                    Message = "ServiceNow validation skipped (API URL not configured)",
                    CrNumber = crNumber
                };
            }

            // Configure HTTP client with ServiceNow settings (acquires AAD token)
            await ConfigureHttpClientAsync(apiUrl, GetSubscriptionKey());

            try
            {
                // Query ServiceNow for the change request with display values.
                // Authentication is via subscription-key header + Bearer token (configured in ConfigureHttpClient).
                // IMPORTANT: Do NOT use a leading '/' - that makes HttpClient resolve against the host root.
                // Using sysparm_display_value=true returns human-readable display values:
                //   - state: "Implement" instead of "-1"
                //   - approval: "Approved" instead of "approved"
                var endpoint = $"change_request?number={crNumber}&sysparm_fields=number,state,short_description,start_date,end_date,business_service,approval&sysparm_display_value=true";

                var safeApiUrl = apiUrl.Replace("\r", string.Empty).Replace("\n", string.Empty);
                _logger.LogInformation("Validating CR {CrNumber} against ServiceNow at {ApiUrl}", crNumber, safeApiUrl);

                var response = await _httpClient.GetAsync(endpoint);

                var responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("ServiceNow API returned {StatusCode} for CR {CrNumber}. Response: {Response}",
                        response.StatusCode, crNumber, responseContent.Length > 500 ? responseContent[..500] : responseContent);

                    return new ChangeRequestValidationResult
                    {
                        IsValid = false,
                        Message = $"Failed to validate Change Request. ServiceNow returned {response.StatusCode}",
                        CrNumber = crNumber
                    };
                }

                // Check if response is actually JSON (not an HTML error/login page)
                if (responseContent.TrimStart().StartsWith('<'))
                {
                    _logger.LogError("ServiceNow returned HTML instead of JSON for CR {CrNumber}. Response: {Response}",
                        crNumber, responseContent.Length > 500 ? responseContent[..500] : responseContent);

                    return new ChangeRequestValidationResult
                    {
                        IsValid = false,
                        Message = "ServiceNow returned an unexpected response (HTML instead of JSON). Check authentication configuration.",
                        CrNumber = crNumber
                    };
                }

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                var resultArray = JsonSerializer.Deserialize<ResultArray<SnChangeRequestResponseModel>>(responseContent, options);

                if (resultArray?.result == null || resultArray.result.Length == 0)
                {
                    _logger.LogWarning("CR {CrNumber} not found in ServiceNow", crNumber);

                    return new ChangeRequestValidationResult
                    {
                        IsValid = false,
                        Message = $"Change Request {crNumber} not found in ServiceNow",
                        CrNumber = crNumber
                    };
                }

                var cr = resultArray.result[0];
                var stateDisplay = cr.state?.Trim() ?? string.Empty;
                var currentDateUtc = DateTime.UtcNow;

                // Helper to build a populated result (avoids repeating field assignments)
                ChangeRequestValidationResult BuildResult(bool isValid, string message) => new()
                {
                    IsValid = isValid,
                    Message = message,
                    State = stateDisplay,
                    CrNumber = crNumber,
                    ShortDescription = cr.short_description,
                    StartDate = cr.start_date ?? string.Empty,
                    EndDate = cr.end_date ?? string.Empty
                };

                // With sysparm_display_value=true, state is the display name
                // CR lifecycle: New → Assess → Authorize → Scheduled → Implement → Review → Closed / Cancelled
                // "Implement" is the primary deployment state.
                // "Closed" and "Review" are post-implementation states — deployment already happened, so these are valid.
                // All other states mean the CR is not yet ready.
                if (!string.Equals(stateDisplay, "Implement", StringComparison.OrdinalIgnoreCase))
                {
                    var lowerState = stateDisplay.ToLowerInvariant();

                    // Post-implementation states: CR was already implemented, allow deployment
                    if (lowerState is "closed" or "review")
                    {
                        _logger.LogInformation("CR {CrNumber} is in post-implementation state '{State}'. Allowing deployment.",
                            crNumber, stateDisplay);

                        var postImplMessage = lowerState switch
                        {
                            "closed" => $"Change Request {crNumber} has been completed and closed.",
                            "review" => $"Change Request {crNumber} has been implemented and is under review.",
                            _ => string.Empty
                        };

                        return BuildResult(true, postImplMessage);
                    }

                    // Pre-implementation states: CR is not ready for deployment
                    _logger.LogWarning("CR {CrNumber} is not in Implement state. Current state: {State}",
                        crNumber, stateDisplay);

                    var stateMessage = lowerState switch
                    {
                        "cancelled" => $"Change Request {crNumber} has been cancelled and cannot be used for deployment.",
                        "new" => $"Change Request {crNumber} is still in 'New' state and has not been progressed yet.",
                        "assess" => $"Change Request {crNumber} is being assessed and is not yet ready for deployment.",
                        "authorize" => $"Change Request {crNumber} is awaiting authorization and is not yet ready for deployment.",
                        "scheduled" => $"Change Request {crNumber} is scheduled but has not moved to 'Implement' yet.",
                        _ => $"Change Request {crNumber} is in '{stateDisplay}' state. It must be in 'Implement' state to proceed with deployment."
                    };

                    return BuildResult(false, stateMessage);
                }

                // Check if the CR has been approved
                var approvalDisplay = cr.approval?.Trim() ?? string.Empty;
                if (!string.Equals(approvalDisplay, "Approved", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("CR {CrNumber} is not approved. Approval status: {ApprovalStatus}",
                        crNumber, approvalDisplay);

                    return BuildResult(false,
                        $"Change Request {crNumber} has not been approved yet (current status: {(string.IsNullOrEmpty(approvalDisplay) ? "N/A" : approvalDisplay)}).");
                }

                // Check if current time is within the approved change window
                if (DateTime.TryParse(cr.start_date, out var startDate) && DateTime.TryParse(cr.end_date, out var endDate))
                {
                    if (currentDateUtc < startDate || currentDateUtc > endDate)
                    {
                        _logger.LogWarning(
                            "CR {CrNumber} is not within the approved change window. Window: {StartDate} to {EndDate}, Current: {CurrentTime}",
                            crNumber, startDate, endDate, currentDateUtc);

                        return BuildResult(false,
                            $"Change Request {crNumber} is outside its approved change window.");
                    }
                }

                var businessService = SnChangeRequestResponseModel.GetDisplayValue(cr.business_service);

                var details = $"{cr.number} - {businessService} - " +
                             $"Window: {cr.start_date ?? "N/A"} to {cr.end_date ?? "N/A"}";
                _logger.LogInformation("CR {CrNumber} validated successfully - {Details}", crNumber, details);

                return BuildResult(true,
                    $"Change Request {crNumber} is valid and approved, within the change window.");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP error while validating CR {CrNumber}", crNumber);

                return new ChangeRequestValidationResult
                {
                    IsValid = false,
                    Message = $"Failed to connect to ServiceNow: {ex.Message}",
                    CrNumber = crNumber
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating CR {CrNumber}", crNumber);

                return new ChangeRequestValidationResult
                {
                    IsValid = false,
                    Message = $"Error validating Change Request: {ex.Message}",
                    CrNumber = crNumber
                };
            }
        }

    }
}
