using System.Net.Http.Headers;
using System.Text.Json;
using Dorc.Api.Interfaces;
using Dorc.Core.Configuration;
using Dorc.PersistentData.Sources.Interfaces;

namespace Dorc.Api.Services
{
    /// <summary>ServiceNow CR response model. Uses sysparm_display_value=true so choice fields are display strings.</summary>
    public class SnChangeRequestResponseModel
    {
        public string sys_id { get; set; } = string.Empty;
        public string number { get; set; } = string.Empty;
        public string state { get; set; } = string.Empty;
        public string type { get; set; } = string.Empty;
        public string short_description { get; set; } = string.Empty;
        public string? start_date { get; set; }
        public string? end_date { get; set; }
        public string? approval { get; set; }
        // Reference fields: may be a string OR an object { "link": "...", "value": "..." }
        public JsonElement? business_service { get; set; }

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
                // sysparm_display_value=true returns human-readable state/approval values
                var endpoint = $"change_request?number={crNumber}&sysparm_fields=number,state,short_description,start_date,end_date,business_service,approval,assignment_group&sysparm_display_value=true";

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

                _logger.LogInformation("CR {CrNumber} raw validate response: {Response}", crNumber, responseContent);

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

                // "Implement" is the deployment state. "Closed" and "Review" are post-implementation — also valid.
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
                            "closed" => $"Change Request {crNumber} is closed. Deployment is permitted.",
                            "review" => $"Change Request {crNumber} is in Review state (post-implementation). Deployment is permitted.",
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
                        "new" => $"Change Request {crNumber} is still in 'New' state. " +
                                 "It must be progressed to 'Implement' before deployment can proceed. " +
                                 "Use Auto-create CR or advance the CR in ServiceNow.",
                        "assess" => $"Change Request {crNumber} is being assessed and is not yet ready for deployment. " +
                                    "It must reach 'Implement' state before deployment can proceed.",
                        "authorize" => $"Change Request {crNumber} is pending authorization and is not yet ready for deployment. " +
                                       "It must reach 'Implement' state before deployment can proceed.",
                        "scheduled" => $"Change Request {crNumber} is in 'Scheduled' state. " +
                                       "It must be moved to 'Implement' in ServiceNow before deployment can proceed.",
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
                        $"Change Request {crNumber} does not have ServiceNow approval (current approval status: " +
                        $"{(string.IsNullOrEmpty(approvalDisplay) ? "N/A" : approvalDisplay)}). " +
                        "The CR's approval field must be set to 'Approved' in ServiceNow before deployment can proceed.");
                }

                // Check if current time is within the approved change window
                if (DateTime.TryParse(cr.start_date, out var startDate) && DateTime.TryParse(cr.end_date, out var endDate))
                {
                    if (currentDateUtc < startDate || currentDateUtc > endDate)
                    {
                        _logger.LogWarning(
                            "CR {CrNumber} is not within the scheduled change window. Window: {StartDate} to {EndDate}, Current: {CurrentTime}",
                            crNumber, startDate, endDate, currentDateUtc);

                        return BuildResult(false,
                            $"Change Request {crNumber} is outside its scheduled change window. " +
                            $"The window is {cr.start_date ?? "N/A"} to {cr.end_date ?? "N/A"} but the current time is outside this range.");
                    }
                }

                var businessService = SnChangeRequestResponseModel.GetDisplayValue(cr.business_service);

                var details = $"{cr.number} - {businessService} - " +
                             $"Window: {cr.start_date ?? "N/A"} to {cr.end_date ?? "N/A"}";
                _logger.LogInformation("CR {CrNumber} validated successfully - {Details}", crNumber, details);

                return BuildResult(true,
                    $"Change Request {crNumber} is valid and ready for deployment (state: Implement, within change window).");
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

        public async Task<CreateChangeRequestResult> CreateChangeRequestAsync(CreateChangeRequestInput input)
        {
            if (string.IsNullOrWhiteSpace(input.ShortDescription) &&
                string.IsNullOrWhiteSpace(input.ProjectName))
            {
                return new CreateChangeRequestResult
                {
                    Success = false,
                    Message = "Either ShortDescription or ProjectName is required to create a CR"
                };
            }

            if (!IsServiceNowEnabled())
            {
                return new CreateChangeRequestResult
                {
                    Success = false,
                    Message = "ServiceNow integration is not enabled. Set UseServiceNow=true in config."
                };
            }

            var apiUrl = GetServiceNowApiUrl();
            if (string.IsNullOrEmpty(apiUrl))
            {
                return new CreateChangeRequestResult
                {
                    Success = false,
                    Message = "ServiceNow API URL is not configured"
                };
            }

            await ConfigureHttpClientAsync(apiUrl, GetSubscriptionKey());

            try
            {
                // Resolve assignment_group: cr-inputs.json → project DB field → global config
                var assignmentGroup = !string.IsNullOrEmpty(input.AssignmentGroup)
                    ? input.AssignmentGroup
                    : _configValuesPersistentSource.GetConfigValue("ServiceNowCrAssignmentGroup", "");

                // Resolve business_service: cr-inputs.json → global config
                var businessService = !string.IsNullOrEmpty(input.BusinessService)
                    ? input.BusinessService
                    : _configValuesPersistentSource.GetConfigValue("ServiceNowCrBusinessService", "");

                if (string.IsNullOrEmpty(assignmentGroup))
                {
                    _logger.LogWarning("No AssignmentGroup found for project '{Project}'. " +
                        "Add cr-inputs.json to the project's ADO repo " +
                        "or configure ServiceNowCrAssignmentGroup globally. " +
                        "ServiceNow may reject the CR.", input.ProjectName);
                }

                var startDate = DateTime.UtcNow;
                var endDate = startDate.AddHours(1);
                var dateFormat = "dd.MM.yyyy HH:mm:ss";

                // Use user-supplied change window dates if provided (ISO 8601 from date-time picker)
                if (!string.IsNullOrEmpty(input.StartDate) && DateTime.TryParse(input.StartDate, out var parsedStart))
                    startDate = parsedStart;
                if (!string.IsNullOrEmpty(input.EndDate) && DateTime.TryParse(input.EndDate, out var parsedEnd))
                    endDate = parsedEnd;

                var shortDesc = !string.IsNullOrWhiteSpace(input.ShortDescription)
                    ? input.ShortDescription
                    : $"[DOrc] Deploy {input.ProjectName} to {input.Environment}";

                var implPlan = !string.IsNullOrWhiteSpace(input.ImplementationPlan)
                    ? input.ImplementationPlan
                    : $"Execute DOrc deployment of {input.ProjectName} build {input.BuildNumber} to {input.Environment}";

                var backoutPlan = !string.IsNullOrWhiteSpace(input.BackoutPlan)
                    ? input.BackoutPlan
                    : "Re-run the previously successful production release version";

                var justification = !string.IsNullOrWhiteSpace(input.Justification)
                    ? input.Justification
                    : $"Automated deployment via DOrc for {input.ProjectName}";

                var testPlan = !string.IsNullOrWhiteSpace(input.TestPlan)
                    ? input.TestPlan
                    : "Tested through the CI/CD pipeline prior to production deployment";

                var riskImpact = !string.IsNullOrWhiteSpace(input.RiskImpactAnalysis)
                    ? input.RiskImpactAnalysis
                    : "OutageOrRestrictedFunctionality: false; ServiceImpactedOnFailure: false; Criticality: Minor";

                var crType = !string.IsNullOrWhiteSpace(input.Type) ? input.Type : "Standard";
                var chgModel = !string.IsNullOrWhiteSpace(input.ChgModel) ? input.ChgModel : "Standard";

                var crInputsSource = input.CrInputsFetched ? "cr-inputs.json" : "hardcoded defaults";
                _logger.LogInformation("Building CR body for project '{Project}' using {Source}",
                    input.ProjectName, crInputsSource);

                var crBody = new Dictionary<string, string>
                {
                    ["short_description"] = shortDesc,
                    ["type"] = crType,
                    ["chg_model"] = chgModel,
                    ["start_date"] = startDate.ToString(dateFormat),
                    ["end_date"] = endDate.ToString(dateFormat),
                    ["implementation_plan"] = implPlan,
                    ["backout_plan"] = backoutPlan,
                    ["justification"] = justification,
                    ["test_plan"] = testPlan,
                    ["risk_impact_analysis"] = riskImpact
                };

                if (!string.IsNullOrEmpty(assignmentGroup))
                    crBody["assignment_group"] = assignmentGroup;
                if (!string.IsNullOrEmpty(businessService))
                    crBody["business_service"] = businessService;
                if (!string.IsNullOrEmpty(input.RequestedBy))
                    crBody["requested_by"] = input.RequestedBy;

                if (!string.IsNullOrEmpty(input.WorkNotes))
                    crBody["work_notes"] = input.WorkNotes;
                if (!string.IsNullOrEmpty(input.Category))
                    crBody["category"] = input.Category;
                if (!string.IsNullOrEmpty(input.CorrelationId))
                    crBody["correlation_id"] = input.CorrelationId;
                if (!string.IsNullOrEmpty(input.Impact))
                    crBody["impact"] = input.Impact;
                if (!string.IsNullOrEmpty(input.Priority))
                    crBody["priority"] = input.Priority;
                if (!string.IsNullOrEmpty(input.Urgency))
                    crBody["urgency"] = input.Urgency;

                var jsonContent = JsonSerializer.Serialize(crBody);
                var content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

                var safeProject = (input.ProjectName ?? string.Empty)
                    .Replace("\r", string.Empty)
                    .Replace("\n", string.Empty);
                var safeEnvironment = (input.Environment ?? string.Empty)
                    .Replace("\r", string.Empty)
                    .Replace("\n", string.Empty);

                _logger.LogInformation("Creating AutoCR for project {Project} environment {Environment}",
                    safeProject, safeEnvironment);

                var response = await _httpClient.PostAsync("change_request", content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("ServiceNow CR creation failed with {StatusCode}", response.StatusCode);
                    return new CreateChangeRequestResult
                    {
                        Success = false,
                        Message = $"ServiceNow returned {response.StatusCode} when creating CR"
                    };
                }

                _logger.LogInformation("ServiceNow CR creation raw response: {Response}", responseContent);

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var result = JsonSerializer.Deserialize<ResultObject<SnChangeRequestResponseModel>>(responseContent, options);

                if (result?.result == null || string.IsNullOrEmpty(result.result.number))
                {
                    _logger.LogError("ServiceNow CR creation returned no CR number. Raw: {Response}", responseContent);
                    return new CreateChangeRequestResult
                    {
                        Success = false,
                        Message = "ServiceNow created a CR but did not return a CR number"
                    };
                }

                _logger.LogInformation("AutoCR created successfully: {CrNumber}, sys_id={SysId}, state={State}",
                    result.result.number, result.result.sys_id, result.result.state);

                // Progress CR from New → Scheduled → Implement so it's ready for deployment
                var sysId = result.result.sys_id;
                var createdState = result.result.state;

                var progressedToImplement = false;
                if (createdState != "-1" && createdState != "Implement" && !string.IsNullOrEmpty(sysId))
                {
                    if (string.IsNullOrEmpty(assignmentGroup))
                    {
                        _logger.LogWarning("ServiceNowCrAssignmentGroup config is not set. " +
                            "ServiceNow requires assignment_group to advance CR state. " +
                            "Set this value in DOrc config. CR {CrNumber} will remain in {State} state.",
                            result.result.number, createdState);
                    }

                    var stateProgression = new[] { "-2", "-1" }; // Scheduled, then Implement
                    progressedToImplement = true;
                    foreach (var targetState in stateProgression)
                    {
                        var advanced = await AdvanceCrStateAsync(sysId, targetState, assignmentGroup, startDate, endDate, dateFormat);
                        if (!advanced)
                        {
                            _logger.LogWarning("Could not advance CR {CrNumber} to state {State}, stopping progression",
                                result.result.number, targetState);
                            progressedToImplement = false;
                            break;
                        }
                    }
                }
                else if (createdState == "-1" || createdState == "Implement")
                {
                    progressedToImplement = true;
                }

                var message = progressedToImplement
                    ? $"Change Request {result.result.number} created and progressed to Implement" +
                      (input.CrInputsFetched ? " (using cr-inputs.json from ADO repo)" : "")
                    : $"Change Request {result.result.number} created but could not advance to Implement state. " +
                      "Ensure assignment_group is set in the project's cr-inputs.json " +
                      "or the global ServiceNowCrAssignmentGroup config.";

                return new CreateChangeRequestResult
                {
                    Success = true,
                    CrNumber = result.result.number,
                    Message = message
                };
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP error creating AutoCR");
                return new CreateChangeRequestResult
                {
                    Success = false,
                    Message = $"Failed to connect to ServiceNow: {ex.Message}"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating AutoCR");
                return new CreateChangeRequestResult
                {
                    Success = false,
                    Message = $"Error creating Change Request: {ex.Message}"
                };
            }
        }

        private async Task<bool> AdvanceCrStateAsync(
            string sysId, string targetState, string assignmentGroup,
            DateTime startDate, DateTime endDate, string dateFormat)
        {
            try
            {
                var body = new Dictionary<string, string> { ["state"] = targetState };
                if (!string.IsNullOrEmpty(assignmentGroup))
                    body["assignment_group"] = assignmentGroup;
                body["start_date"] = startDate.ToString(dateFormat);
                body["end_date"] = endDate.ToString(dateFormat);
                var jsonContent = new StringContent(
                    System.Text.Json.JsonSerializer.Serialize(body),
                    System.Text.Encoding.UTF8,
                    "application/json");

                var response = await _httpClient.PutAsync($"change_request/{sysId}", jsonContent);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Failed to advance CR {SysId} to state {State}. Status: {Status}, Response: {Response}",
                        sysId, targetState, response.StatusCode, responseBody);
                    return false;
                }

                // Verify actual state from response (ServiceNow can return 200 without changing state)
                try
                {
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var result = JsonSerializer.Deserialize<ResultObject<SnChangeRequestResponseModel>>(responseBody, options);
                    var actualState = result?.result?.state;
                    if (!string.IsNullOrEmpty(actualState) && actualState != targetState)
                    {
                        _logger.LogWarning("CR {SysId} state not advanced: requested {TargetState} but actual state is {ActualState}. Response: {Response}",
                            sysId, targetState, actualState, responseBody);
                        return false;
                    }
                }
                catch (Exception parseEx)
                {
                    _logger.LogDebug(parseEx, "Could not parse state from advance response, assuming success");
                }

                _logger.LogInformation("Advanced CR {SysId} to state {State}", sysId, targetState);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error advancing CR {SysId} to state {State}", sysId, targetState);
                return false;
            }
        }

    }

    public class ResultObject<T>
    {
        public T result { get; set; } = default!;
    }
}
