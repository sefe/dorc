using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Dorc.Api.Interfaces;
using Dorc.Api.Models;
using Dorc.PersistentData.Sources.Interfaces;

namespace Dorc.Api.Services
{
    public class ServiceNowService : IServiceNowService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<ServiceNowService> _logger;
        private readonly IConfigValuesPersistentSource _config;
        private readonly IAzureAdTokenService _tokenService;

        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        public ServiceNowService(
            HttpClient httpClient,
            ILogger<ServiceNowService> logger,
            IConfigValuesPersistentSource configValuesPersistentSource,
            IAzureAdTokenService tokenService)
        {
            _httpClient = httpClient;
            _logger = logger;
            _config = configValuesPersistentSource;
            _tokenService = tokenService;
        }

        // ── Configuration helpers ──────────────────────────────────────────

        private bool IsEnabled() =>
            bool.TryParse(_config.GetConfigValue("UseServiceNow", "false"), out var v) && v;

        private string ApiUrl() => _config.GetConfigValue("ServiceNowApiUrl", string.Empty);
        private string SubscriptionKey() => _config.GetConfigValue("ServiceNowApiSubscriptionKey", string.Empty);
        private string AadScope() => _config.GetConfigValue("ServiceNowAadScope", string.Empty);

        private string ConfigOrDefault(string key, string fallback) =>
            _config.GetConfigValue(key, fallback) is { Length: > 0 } v ? v : fallback;

        // ── HTTP client bootstrap ──────────────────────────────────────────

        private async Task ConfigureClientAsync()
        {
            var apiUrl = ApiUrl();
            if (!string.IsNullOrEmpty(apiUrl))
            {
                if (!apiUrl.EndsWith('/')) apiUrl += '/';
                _httpClient.BaseAddress = new Uri(apiUrl);
            }

            var subKey = SubscriptionKey();
            if (!string.IsNullOrEmpty(subKey))
                _httpClient.DefaultRequestHeaders.Add("subscription-key", subKey);

            var scope = AadScope();
            if (!string.IsNullOrEmpty(scope))
            {
                var token = await _tokenService.GetTokenAsync(scope);
                if (!string.IsNullOrEmpty(token))
                    _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        // ── Validate ───────────────────────────────────────────────────────

        public async Task<ChangeRequestValidationResult> ValidateChangeRequestAsync(string crNumber)
        {
            crNumber = Sanitize(crNumber);
            if (string.IsNullOrEmpty(crNumber))
                return ValidationError("Change Request number is required", crNumber);

            var skip = CheckServiceNowAvailability(crNumber);
            if (skip != null) return skip;

            await ConfigureClientAsync();

            try
            {
                var cr = await FetchChangeRequestAsync(crNumber);
                if (cr == null)
                    return ValidationError($"Change Request {crNumber} not found in ServiceNow", crNumber);

                return EvaluateChangeRequest(cr, crNumber);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP error validating CR {CrNumber}", crNumber);
                return ValidationError("Failed to connect to ServiceNow. Please try again later or contact support.", crNumber);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating CR {CrNumber}", crNumber);
                return ValidationError("An unexpected error occurred while validating the Change Request. Please try again later or contact support.", crNumber);
            }
        }

        private ChangeRequestValidationResult? CheckServiceNowAvailability(string crNumber)
        {
            if (!IsEnabled())
            {
                _logger.LogDebug("ServiceNow is disabled — skipping validation for {CrNumber}", crNumber);
                return new ChangeRequestValidationResult
                {
                    IsValid = true,
                    Message = "ServiceNow validation skipped (not enabled)",
                    CrNumber = crNumber
                };
            }

            if (string.IsNullOrEmpty(ApiUrl()))
            {
                _logger.LogWarning("ServiceNow API URL not configured — skipping validation for {CrNumber}", crNumber);
                return new ChangeRequestValidationResult
                {
                    IsValid = true,
                    Message = "ServiceNow validation skipped (API URL not configured)",
                    CrNumber = crNumber
                };
            }

            return null; // ServiceNow is available
        }

        private async Task<SnChangeRequestResponse?> FetchChangeRequestAsync(string crNumber)
        {
            var endpoint = $"change_request?number={crNumber}" +
                           "&sysparm_fields=number,state,short_description,start_date,end_date,business_service,approval,assignment_group" +
                           "&sysparm_display_value=true";

            _logger.LogInformation("Validating CR {CrNumber} against ServiceNow", crNumber);
            var response = await _httpClient.GetAsync(endpoint);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("ServiceNow returned {StatusCode} for CR {CrNumber}. Response: {Response}",
                    response.StatusCode, crNumber, Truncate(body));
                return null;
            }

            if (body.TrimStart().StartsWith('<'))
            {
                _logger.LogError("ServiceNow returned HTML instead of JSON for CR {CrNumber}", crNumber);
                return null;
            }

            _logger.LogInformation("CR {CrNumber} raw validate response: {Response}", crNumber, body);

            var result = JsonSerializer.Deserialize<SnResultArray<SnChangeRequestResponse>>(body, JsonOptions);
            return result?.result is { Length: > 0 } arr ? arr[0] : null;
        }

        private ChangeRequestValidationResult EvaluateChangeRequest(SnChangeRequestResponse cr, string crNumber)
        {
            var state = cr.state?.Trim() ?? string.Empty;

            ChangeRequestValidationResult Result(bool valid, string msg) => new()
            {
                IsValid = valid, Message = msg, State = state, CrNumber = crNumber,
                ShortDescription = cr.short_description,
                StartDate = cr.start_date ?? string.Empty, EndDate = cr.end_date ?? string.Empty
            };

            // ── State check ──
            if (!state.Equals("Implement", StringComparison.OrdinalIgnoreCase))
            {
                var lower = state.ToLowerInvariant();

                // Post-implementation states are acceptable
                if (lower is "closed" or "review")
                {
                    _logger.LogInformation("CR {CrNumber} is in post-implementation state '{State}'", crNumber, state);
                    var msg = lower == "closed"
                        ? $"Change Request {crNumber} is closed. Deployment is permitted."
                        : $"Change Request {crNumber} is in Review state (post-implementation). Deployment is permitted.";
                    return Result(true, msg);
                }

                // Pre-implementation states block deployment
                _logger.LogWarning("CR {CrNumber} is in '{State}' state, not Implement", crNumber, state);
                return Result(false, GetPreImplementStateMessage(crNumber, lower, state));
            }

            // ── Approval check ──
            var approval = cr.approval?.Trim() ?? string.Empty;
            if (!approval.Equals("Approved", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("CR {CrNumber} approval status: {Approval}", crNumber, approval);
                return Result(false,
                    $"Change Request {crNumber} does not have ServiceNow approval " +
                    $"(current: {(string.IsNullOrEmpty(approval) ? "N/A" : approval)}). " +
                    "It must be 'Approved' before deployment can proceed.");
            }

            // ── Change window check ──
            if (DateTime.TryParse(cr.start_date, out var start) && DateTime.TryParse(cr.end_date, out var end))
            {
                var now = DateTime.UtcNow;
                if (now < start || now > end)
                {
                    _logger.LogWarning("CR {CrNumber} outside change window {Start}–{End}", crNumber, start, end);
                    return Result(false,
                        $"Change Request {crNumber} is outside its scheduled change window " +
                        $"({cr.start_date ?? "N/A"} — {cr.end_date ?? "N/A"}).");
                }
            }

            // ── All checks passed ──
            var biz = SnChangeRequestResponse.GetDisplayValue(cr.business_service);
            _logger.LogInformation("CR {CrNumber} validated: {Biz}, window {Start}–{End}", crNumber, biz, cr.start_date, cr.end_date);
            return Result(true, $"Change Request {crNumber} is valid and ready for deployment (state: Implement, within change window).");
        }

        private static string GetPreImplementStateMessage(string crNumber, string lowerState, string displayState) => lowerState switch
        {
            "cancelled" => $"Change Request {crNumber} has been cancelled and cannot be used for deployment.",
            "new" => $"Change Request {crNumber} is still in 'New' state. It must be progressed to 'Implement' before deployment can proceed. Use Auto-create CR or advance the CR in ServiceNow.",
            "assess" => $"Change Request {crNumber} is being assessed. It must reach 'Implement' state before deployment can proceed.",
            "authorize" => $"Change Request {crNumber} is pending authorization. It must reach 'Implement' state before deployment can proceed.",
            "scheduled" => $"Change Request {crNumber} is in 'Scheduled' state. It must be moved to 'Implement' in ServiceNow before deployment can proceed.",
            _ => $"Change Request {crNumber} is in '{displayState}' state. It must be in 'Implement' state to proceed with deployment."
        };

        // ── Create ─────────────────────────────────────────────────────────

        public async Task<CreateChangeRequestResult> CreateChangeRequestAsync(CreateChangeRequestInput input)
        {
            if (string.IsNullOrWhiteSpace(input.ShortDescription) && string.IsNullOrWhiteSpace(input.ProjectName))
                return CreateError("Either ShortDescription or ProjectName is required to create a CR");

            if (!IsEnabled())
                return CreateError("ServiceNow integration is not enabled. Set UseServiceNow=true in config.");

            if (string.IsNullOrEmpty(ApiUrl()))
                return CreateError("ServiceNow API URL is not configured");

            await ConfigureClientAsync();

            var safeProject = Sanitize(input.ProjectName);
            var safeEnv = Sanitize(input.Environment);

            try
            {
                var crBody = BuildCrBody(input);
                var assignmentGroup = crBody.GetValueOrDefault("assignment_group", "");

                _logger.LogInformation("Creating AutoCR for {Project} → {Environment}", safeProject, safeEnv);

                var response = await _httpClient.PostAsync("change_request",
                    new StringContent(JsonSerializer.Serialize(crBody), Encoding.UTF8, "application/json"));
                var responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("CR creation failed with {StatusCode}. Response: {Response}",
                        response.StatusCode, Truncate(responseContent));
                    return CreateError($"ServiceNow returned {response.StatusCode} when creating CR");
                }

                _logger.LogInformation("CR creation raw response: {Response}", responseContent);

                var result = JsonSerializer.Deserialize<SnResultObject<SnChangeRequestResponse>>(responseContent, JsonOptions);
                if (result?.result == null || string.IsNullOrEmpty(result.result.number))
                {
                    _logger.LogError("CR creation returned no number. Raw: {Response}", responseContent);
                    return CreateError("ServiceNow created a CR but did not return a CR number");
                }

                var cr = result.result;
                _logger.LogInformation("AutoCR {CrNumber} created (sys_id={SysId}, state={State})", cr.number, cr.sys_id, cr.state);

                var progressed = await TryProgressToImplementAsync(cr, assignmentGroup, input);

                var message = progressed
                    ? $"Change Request {cr.number} created and progressed to Implement"
                      + (input.CrInputsFetched ? " (using cr-inputs.json from ADO repo)" : "")
                    : $"Change Request {cr.number} created but could not advance to Implement state. "
                      + "Ensure assignment_group is set in the project's cr-inputs.json or the global ServiceNowCrAssignmentGroup config.";

                return new CreateChangeRequestResult { Success = true, CrNumber = cr.number, Message = message };
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP error creating AutoCR");
                return CreateError("Failed to connect to ServiceNow. Please try again later or contact support.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating AutoCR");
                return CreateError("An unexpected error occurred while creating the Change Request. Please try again later or contact support.");
            }
        }

        private Dictionary<string, string> BuildCrBody(CreateChangeRequestInput input)
        {
            var assignmentGroup = !string.IsNullOrEmpty(input.AssignmentGroup)
                ? input.AssignmentGroup
                : _config.GetConfigValue("ServiceNowCrAssignmentGroup", "");

            var businessService = !string.IsNullOrEmpty(input.BusinessService)
                ? input.BusinessService
                : _config.GetConfigValue("ServiceNowCrBusinessService", "");

            if (string.IsNullOrEmpty(assignmentGroup))
                _logger.LogWarning("No AssignmentGroup for project '{Project}'. ServiceNow may reject the CR.", Sanitize(input.ProjectName));

            var startDate = DateTime.UtcNow;
            var endDate = startDate.AddHours(1);
            const string dateFmt = "dd.MM.yyyy HH:mm:ss";

            if (DateTime.TryParse(input.StartDate, out var ps)) startDate = ps;
            if (DateTime.TryParse(input.EndDate, out var pe)) endDate = pe;

            var source = input.CrInputsFetched ? "cr-inputs.json" : "hardcoded defaults";
            _logger.LogInformation("Building CR body for '{Project}' using {Source}", Sanitize(input.ProjectName), source);

            var body = new Dictionary<string, string>
            {
                ["short_description"] = OrDefault(input.ShortDescription, $"[DOrc] Deploy {input.ProjectName} to {input.Environment}"),
                ["type"] = OrDefault(input.Type, "Standard"),
                ["chg_model"] = OrDefault(input.ChgModel, "Standard"),
                ["start_date"] = startDate.ToString(dateFmt),
                ["end_date"] = endDate.ToString(dateFmt),
                ["implementation_plan"] = OrDefault(input.ImplementationPlan, $"Execute DOrc deployment of {input.ProjectName} build {input.BuildNumber} to {input.Environment}"),
                ["backout_plan"] = OrDefault(input.BackoutPlan, "Re-run the previously successful production release version"),
                ["justification"] = OrDefault(input.Justification, $"Automated deployment via DOrc for {input.ProjectName}"),
                ["test_plan"] = OrDefault(input.TestPlan, "Tested through the CI/CD pipeline prior to production deployment"),
                ["risk_impact_analysis"] = OrDefault(input.RiskImpactAnalysis, "OutageOrRestrictedFunctionality: false; ServiceImpactedOnFailure: false; Criticality: Minor")
            };

            AddIfNotEmpty(body, "assignment_group", assignmentGroup);
            AddIfNotEmpty(body, "business_service", businessService);
            AddIfNotEmpty(body, "requested_by", input.RequestedBy);
            AddIfNotEmpty(body, "work_notes", input.WorkNotes);
            AddIfNotEmpty(body, "category", input.Category);
            AddIfNotEmpty(body, "correlation_id", input.CorrelationId);
            AddIfNotEmpty(body, "impact", input.Impact);
            AddIfNotEmpty(body, "priority", input.Priority);
            AddIfNotEmpty(body, "urgency", input.Urgency);

            return body;
        }

        // ── State progression ──────────────────────────────────────────────

        private async Task<bool> TryProgressToImplementAsync(
            SnChangeRequestResponse cr, string assignmentGroup, CreateChangeRequestInput input)
        {
            if (cr.state is "-1" or "Implement") return true;
            if (string.IsNullOrEmpty(cr.sys_id)) return false;

            if (string.IsNullOrEmpty(assignmentGroup))
            {
                _logger.LogWarning("No assignment_group — cannot advance CR {CrNumber} past '{State}'", cr.number, cr.state);
                return false;
            }

            var startDate = DateTime.UtcNow;
            var endDate = startDate.AddHours(1);
            if (DateTime.TryParse(input.StartDate, out var ps)) startDate = ps;
            if (DateTime.TryParse(input.EndDate, out var pe)) endDate = pe;

            // Progress: New → Scheduled (-2) → Implement (-1)
            foreach (var targetState in new[] { "-2", "-1" })
            {
                if (!await AdvanceCrStateAsync(cr.sys_id, targetState, assignmentGroup, startDate, endDate))
                {
                    _logger.LogWarning("Stopped progression of CR {CrNumber} at state {State}", cr.number, targetState);
                    return false;
                }
            }
            return true;
        }

        private async Task<bool> AdvanceCrStateAsync(
            string sysId, string targetState, string assignmentGroup, DateTime start, DateTime end)
        {
            const string dateFmt = "dd.MM.yyyy HH:mm:ss";
            try
            {
                var body = new Dictionary<string, string>
                {
                    ["state"] = targetState,
                    ["start_date"] = start.ToString(dateFmt),
                    ["end_date"] = end.ToString(dateFmt)
                };
                AddIfNotEmpty(body, "assignment_group", assignmentGroup);

                var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
                var response = await _httpClient.PutAsync($"change_request/{sysId}", content);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Failed to advance CR {SysId} to {State}. Status: {Status}", sysId, targetState, response.StatusCode);
                    return false;
                }

                // Verify ServiceNow actually changed the state (it can return 200 without changing)
                var result = JsonSerializer.Deserialize<SnResultObject<SnChangeRequestResponse>>(responseBody, JsonOptions);
                if (result?.result?.state is { } actual && actual != targetState)
                {
                    _logger.LogWarning("CR {SysId} state not advanced: wanted {Target}, got {Actual}", sysId, targetState, actual);
                    return false;
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

        // ── Helpers ────────────────────────────────────────────────────────

        private static ChangeRequestValidationResult ValidationError(string message, string crNumber) => new()
        {
            IsValid = false, Message = message, CrNumber = crNumber
        };

        private static CreateChangeRequestResult CreateError(string message) => new()
        {
            Success = false, Message = message
        };

        private static string Sanitize(string? value) =>
            (value ?? string.Empty).Replace("\r", "").Replace("\n", "").Trim().ToUpperInvariant();

        private static string OrDefault(string? value, string fallback) =>
            string.IsNullOrWhiteSpace(value) ? fallback : value;

        private static void AddIfNotEmpty(Dictionary<string, string> dict, string key, string? value)
        {
            if (!string.IsNullOrEmpty(value)) dict[key] = value;
        }

        private static string Truncate(string value, int maxLength = 500) =>
            value.Length > maxLength ? value[..maxLength] : value;
    }
}
