using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dorc.ApiModel;

namespace Tools.RequestCLI
{
    public interface IEphemeralEnvironmentCliClient
    {
        Task<int> ExecuteAsync(EphemeralCliArgs cliArgs, CancellationToken cancellationToken = default);
    }

    public sealed class EphemeralEnvironmentCliClient : IEphemeralEnvironmentCliClient
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private static readonly JsonSerializerOptions IndentedJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private static readonly HashSet<string> TerminalFailureStatuses = new(StringComparer.OrdinalIgnoreCase)
        {
            "Errored", "Cancelled", "Failed", "Abandoned", "WaitingConfirmation"
        };

        private static readonly HashSet<string> TerminalFailedEphemeralStates = new(StringComparer.OrdinalIgnoreCase)
        {
            EphemeralEnvironmentStates.ValidationFailed,
            EphemeralEnvironmentStates.ProvisionFailed,
            EphemeralEnvironmentStates.TeardownFailed,
            EphemeralEnvironmentStates.TornDown
        };

        private static readonly DateTimeOffset MonotonicOrigin = DateTimeOffset.UtcNow;
        private static readonly long MonotonicOriginTimestamp = Stopwatch.GetTimestamp();

        private readonly HttpClient _httpClient;
        private readonly Action<string> _output;
        private readonly TimeSpan _pollInterval;
        private readonly Func<DateTimeOffset> _timeProvider;

        public EphemeralEnvironmentCliClient(
            HttpClient httpClient,
            Action<string>? output = null,
            TimeSpan? pollInterval = null,
            Func<DateTimeOffset>? timeProvider = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _output = output ?? (_ => { });
            _pollInterval = pollInterval ?? TimeSpan.FromSeconds(5);
            _timeProvider = timeProvider ?? (() => MonotonicOrigin.Add(Stopwatch.GetElapsedTime(MonotonicOriginTimestamp)));
        }

        public async Task<int> ExecuteAsync(EphemeralCliArgs cliArgs, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(cliArgs);

            if (cliArgs.Verb == EphemeralCliVerb.Up)
            {
                return await ExecuteProvisionAsync(cliArgs, cancellationToken);
            }

            if (cliArgs.Verb == EphemeralCliVerb.ExtendTtl)
            {
                return await ExecuteExtendTtlAsync(cliArgs, cancellationToken);
            }

            if (cliArgs.Verb == EphemeralCliVerb.BlueprintList)
            {
                return await ExecuteBlueprintListAsync(cliArgs, cancellationToken);
            }

            if (cliArgs.Verb == EphemeralCliVerb.BlueprintExport)
            {
                return await ExecuteBlueprintExportAsync(cliArgs, cancellationToken);
            }

            if (cliArgs.Verb == EphemeralCliVerb.BlueprintApply)
            {
                return await ExecuteBlueprintApplyAsync(cliArgs, cancellationToken);
            }

            return await ExecuteTeardownAsync(cliArgs, cancellationToken);
        }

        private async Task<int> ExecuteBlueprintListAsync(EphemeralCliArgs cliArgs, CancellationToken cancellationToken)
        {
            int projectId;
            try
            {
                projectId = await ResolveProjectIdAsync(cliArgs.Project, cancellationToken);
            }
            catch (Exception ex)
            {
                _output($"Error resolving project '{cliArgs.Project}': {ex.Message}");
                return 1;
            }

            using var response = await _httpClient.GetAsync($"EphemeralEnvironments/templates?projectId={projectId}", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _output($"Error listing blueprints for project '{cliArgs.Project}': {errorBody}");
                return 1;
            }

            var templates = await response.Content.ReadFromJsonAsync<List<EnvironmentTemplateApiModel>>(JsonOptions, cancellationToken)
                ?? new List<EnvironmentTemplateApiModel>();

            if (templates.Count == 0)
            {
                _output($"No blueprints found for project '{cliArgs.Project}'.");
                return 0;
            }

            foreach (var tmpl in templates)
            {
                var baseEnv = string.IsNullOrWhiteSpace(tmpl.BaseEnvironmentName) ? "none" : tmpl.BaseEnvironmentName;
                var provCount = tmpl.ProvisionComponentIds?.Count ?? 0;
                var valCount = tmpl.ValidationComponentIds?.Count ?? 0;
                _output($"{tmpl.Name} | Tier: {tmpl.DefaultTier} | BaseEnv: {baseEnv} | TTL: {tmpl.DefaultTtlMinutes}/{tmpl.MaxTtlMinutes}m | Components: {provCount} provision, {valCount} validation");
            }

            return 0;
        }

        private async Task<int> ExecuteBlueprintExportAsync(EphemeralCliArgs cliArgs, CancellationToken cancellationToken)
        {
            int projectId;
            int? templateId;
            try
            {
                projectId = await ResolveProjectIdAsync(cliArgs.Project, cancellationToken);
                templateId = await ResolveTemplateIdAsync(projectId, cliArgs.Template, cancellationToken);
            }
            catch (Exception ex)
            {
                _output($"Error resolving blueprint '{cliArgs.Template}' for project '{cliArgs.Project}': {ex.Message}");
                return 1;
            }

            if (templateId is null)
            {
                _output($"Error exporting blueprint: template '{cliArgs.Template}' was not specified or found.");
                return 1;
            }

            using var response = await _httpClient.GetAsync($"EphemeralEnvironments/templates/{templateId.Value}/export", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _output($"Error exporting blueprint '{cliArgs.Template}': {errorBody}");
                return 1;
            }

            var definition = await response.Content.ReadFromJsonAsync<EnvironmentBlueprintDefinitionApiModel>(JsonOptions, cancellationToken);
            if (definition is null)
            {
                _output($"Error exporting blueprint '{cliArgs.Template}': empty response.");
                return 1;
            }

            var json = JsonSerializer.Serialize(definition, IndentedJsonOptions);
            if (!string.IsNullOrWhiteSpace(cliArgs.FilePath))
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(cliArgs.FilePath));
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                await File.WriteAllTextAsync(cliArgs.FilePath, json, cancellationToken);
                _output($"Exported blueprint '{definition.Name}' to '{cliArgs.FilePath}'.");
            }
            else
            {
                _output(json);
            }

            return 0;
        }

        private async Task<int> ExecuteBlueprintApplyAsync(EphemeralCliArgs cliArgs, CancellationToken cancellationToken)
        {
            if (!File.Exists(cliArgs.FilePath))
            {
                _output($"Error applying blueprint: file '{cliArgs.FilePath}' does not exist.");
                return 1;
            }

            var rawJson = await File.ReadAllTextAsync(cliArgs.FilePath, cancellationToken);
            EnvironmentBlueprintImportRequestApiModel importRequest;
            try
            {
                importRequest = ParseBlueprintImportPayload(rawJson, cliArgs.Project);
            }
            catch (Exception ex)
            {
                _output($"Error parsing blueprint file '{cliArgs.FilePath}': {ex.Message}");
                return 1;
            }

            if (importRequest is null || importRequest.Blueprints.Count == 0)
            {
                _output($"Error applying blueprint: no blueprint definitions found in '{cliArgs.FilePath}'.");
                return 1;
            }

            using var response = await _httpClient.PostAsJsonAsync("EphemeralEnvironments/templates/import", importRequest, JsonOptions, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _output($"Error applying blueprints from '{cliArgs.FilePath}': {errorBody}");
                return 1;
            }

            var importResponse = await response.Content.ReadFromJsonAsync<EnvironmentBlueprintImportResponseApiModel>(JsonOptions, cancellationToken);
            if (importResponse is null)
            {
                _output("Error applying blueprints: empty response.");
                return 1;
            }

            _output($"Applied {importResponse.ImportedTemplates.Count} blueprint(s) ({importResponse.CreatedCount} created, {importResponse.UpdatedCount} updated).");
            return 0;
        }

        private static EnvironmentBlueprintImportRequestApiModel ParseBlueprintImportPayload(string rawJson, string cliProject)
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            var importRequest = new EnvironmentBlueprintImportRequestApiModel();
            if (root.ValueKind == JsonValueKind.Array)
            {
                var list = JsonSerializer.Deserialize<List<EnvironmentBlueprintDefinitionApiModel>>(rawJson, JsonOptions)
                    ?? new List<EnvironmentBlueprintDefinitionApiModel>();
                importRequest.Blueprints = list;
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                var hasBlueprintsProp = root.EnumerateObject()
                    .Any(p => string.Equals(p.Name, "blueprints", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.Array);

                if (hasBlueprintsProp)
                {
                    importRequest = JsonSerializer.Deserialize<EnvironmentBlueprintImportRequestApiModel>(rawJson, JsonOptions)
                        ?? new EnvironmentBlueprintImportRequestApiModel();
                }
                else
                {
                    var single = JsonSerializer.Deserialize<EnvironmentBlueprintDefinitionApiModel>(rawJson, JsonOptions);
                    if (single is not null)
                    {
                        importRequest.Blueprints.Add(single);
                    }
                }
            }
            else
            {
                throw new InvalidOperationException("Expected a JSON object or JSON array of blueprint definitions.");
            }

            if (!string.IsNullOrWhiteSpace(cliProject))
            {
                if (int.TryParse(cliProject, out var parsedProjectId) && parsedProjectId > 0)
                {
                    importRequest.ProjectId = parsedProjectId;
                }
                else
                {
                    importRequest.ProjectName = cliProject;
                }
            }
            else if (string.IsNullOrWhiteSpace(importRequest.ProjectName) && importRequest.ProjectId is not > 0)
            {
                var firstProjectName = importRequest.Blueprints
                    .FirstOrDefault(b => !string.IsNullOrWhiteSpace(b?.ProjectName))?.ProjectName;
                if (!string.IsNullOrWhiteSpace(firstProjectName))
                {
                    importRequest.ProjectName = firstProjectName;
                }
            }

            return importRequest;
        }

        private async Task<int> ExecuteExtendTtlAsync(EphemeralCliArgs cliArgs, CancellationToken cancellationToken)
        {
            var extendRequest = new EphemeralEnvironmentExtendTtlRequestApiModel
            {
                AdditionalMinutes = cliArgs.TtlMinutes!.Value
            };

            var encodedEnv = Uri.EscapeDataString(cliArgs.TargetEnv);
            using var response = await _httpClient.PostAsJsonAsync($"EphemeralEnvironments/{encodedEnv}/extend-ttl", extendRequest, JsonOptions, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _output($"Error extending TTL for ephemeral environment '{cliArgs.TargetEnv}': {errorBody}");
                return 1;
            }

            _output($"Extended TTL for ephemeral environment '{cliArgs.TargetEnv}' by {extendRequest.AdditionalMinutes} minutes.");
            return 0;
        }

        private async Task<int> ExecuteProvisionAsync(EphemeralCliArgs cliArgs, CancellationToken cancellationToken)
        {
            var projectId = await ResolveProjectIdAsync(cliArgs.Project, cancellationToken);
            var templateId = await ResolveTemplateIdAsync(projectId, cliArgs.Template, cancellationToken);
            var baseEnvId = await ResolveBaseEnvironmentIdAsync(cliArgs.BaseEnv, cancellationToken);

            var requestModel = new EphemeralEnvironmentProvisionRequestApiModel
            {
                ProjectId = projectId,
                TemplateId = templateId,
                EnvironmentName = cliArgs.TargetEnv,
                EnvironmentTier = cliArgs.Tier,
                TtlMinutes = cliArgs.TtlMinutes,
                BaseEnvironmentId = baseEnvId,
                ParameterOverrides = new Dictionary<string, string>(cliArgs.Properties, StringComparer.OrdinalIgnoreCase),
                RunValidationComponents = cliArgs.RunValidationComponents,
                BuildNum = cliArgs.BuildNum,
                BuildUrl = cliArgs.BuildUrl,
                BuildText = cliArgs.BuildText
            };

            using var response = await _httpClient.PostAsJsonAsync("EphemeralEnvironments/provision", requestModel, JsonOptions, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _output($"Error provisioning ephemeral environment '{cliArgs.TargetEnv}': {errorBody}");
                return 1;
            }

            var provisionResponse = await response.Content.ReadFromJsonAsync<EphemeralEnvironmentProvisionResponseApiModel>(JsonOptions, cancellationToken);
            if (provisionResponse is null)
            {
                _output("Error provisioning ephemeral environment: empty response.");
                return 1;
            }

            _output($"Provisioned ephemeral environment '{provisionResponse.Environment?.EnvironmentName ?? cliArgs.TargetEnv}'.");

            if (!cliArgs.Block)
            {
                return 0;
            }

            var effectiveTimeoutMinutes = cliArgs.TimeoutMinutes > 0 ? cliArgs.TimeoutMinutes : 30;
            var deadline = _timeProvider().AddMinutes(effectiveTimeoutMinutes);

            var allRequestIds = provisionResponse.ProvisionRequestIds
                .Concat(provisionResponse.ValidationRequestIds)
                .ToList();

            foreach (var requestId in allRequestIds)
            {
                var waitResult = await WaitForRequestCompletionAsync(
                    requestId,
                    deadline,
                    effectiveTimeoutMinutes,
                    cancellationToken);
                if (waitResult != 0)
                {
                    return waitResult;
                }
            }

            if (cliArgs.RunValidationComponents)
            {
                return await WaitForEphemeralEnvironmentReadyAsync(
                    projectId,
                    cliArgs.TargetEnv,
                    deadline,
                    effectiveTimeoutMinutes,
                    cancellationToken);
            }

            return 0;
        }

        private async Task<int> ExecuteTeardownAsync(EphemeralCliArgs cliArgs, CancellationToken cancellationToken)
        {
            var teardownRequest = new EphemeralEnvironmentTeardownRequestApiModel
            {
                Reason = cliArgs.Reason,
                ForceDeleteMetadataOnNoComponents = cliArgs.ForceDeleteMetadataOnNoComponents
            };

            var encodedEnv = Uri.EscapeDataString(cliArgs.TargetEnv);
            using var response = await _httpClient.PostAsJsonAsync($"EphemeralEnvironments/{encodedEnv}/teardown", teardownRequest, JsonOptions, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _output($"Error tearing down ephemeral environment '{cliArgs.TargetEnv}': {errorBody}");
                return 1;
            }

            var teardownResponse = await response.Content.ReadFromJsonAsync<EphemeralEnvironmentTeardownResponseApiModel>(JsonOptions, cancellationToken);
            if (teardownResponse is null)
            {
                _output("Error tearing down ephemeral environment: empty response.");
                return 1;
            }

            _output($"Teardown initiated for ephemeral environment '{teardownResponse.EnvironmentName}' (state: {teardownResponse.EphemeralState}).");

            if (cliArgs.Block)
            {
                var destroyIds = teardownResponse.DestroyRequestIds.Count > 0
                    ? teardownResponse.DestroyRequestIds
                    : (teardownResponse.DestroyRequestId.HasValue
                        ? new List<int> { teardownResponse.DestroyRequestId.Value }
                        : new List<int>());

                var effectiveTimeoutMinutes = cliArgs.TimeoutMinutes > 0 ? cliArgs.TimeoutMinutes : 30;
                var deadline = _timeProvider().AddMinutes(effectiveTimeoutMinutes);

                if (destroyIds.Count > 0)
                {
                    foreach (var destroyId in destroyIds)
                    {
                        var waitResult = await WaitForRequestCompletionAsync(
                            destroyId,
                            deadline,
                            effectiveTimeoutMinutes,
                            cancellationToken);
                        if (waitResult != 0)
                        {
                            return waitResult;
                        }
                    }
                }
                else if (string.Equals(teardownResponse.EphemeralState, EphemeralEnvironmentStates.TearingDown, StringComparison.OrdinalIgnoreCase))
                {
                    return await WaitForEphemeralEnvironmentTeardownCompletionAsync(
                        teardownResponse.EnvironmentName,
                        deadline,
                        effectiveTimeoutMinutes,
                        cancellationToken);
                }
            }

            return 0;
        }

        private async Task<int> WaitForEphemeralEnvironmentTeardownCompletionAsync(
            string targetEnvName,
            DateTimeOffset deadline,
            int effectiveTimeoutMinutes,
            CancellationToken cancellationToken)
        {
            while (_timeProvider() < deadline)
            {
                using var envResponse = await _httpClient.GetAsync(
                    $"RefDataEnvironments?env={Uri.EscapeDataString(targetEnvName)}",
                    cancellationToken);
                if (envResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return 0;
                }

                envResponse.EnsureSuccessStatusCode();
                var environments = await envResponse.Content.ReadFromJsonAsync<List<EnvironmentApiModel>>(
                    JsonOptions,
                    cancellationToken);

                var match = environments!.FirstOrDefault(
                    e => string.Equals(e.EnvironmentName, targetEnvName, StringComparison.OrdinalIgnoreCase));
                var currentState = match?.EphemeralState;

                if (currentState is null || string.Equals(currentState, EphemeralEnvironmentStates.TornDown, StringComparison.OrdinalIgnoreCase))
                {
                    return 0;
                }

                if (string.Equals(currentState, EphemeralEnvironmentStates.TeardownFailed, StringComparison.OrdinalIgnoreCase))
                {
                    return 2;
                }

                await Task.Delay(_pollInterval, cancellationToken);
            }

            _output($"Timed out waiting for ephemeral environment '{targetEnvName}' teardown after {effectiveTimeoutMinutes} minutes.");
            return 2;
        }

        private async Task<int> ResolveProjectIdAsync(string projectIdentifier, CancellationToken cancellationToken)
        {
            if (int.TryParse(projectIdentifier, out var parsedId) && parsedId > 0)
            {
                return parsedId;
            }

            var project = await _httpClient.GetFromJsonAsync<ProjectApiModel>(
                $"RefDataProjects/{Uri.EscapeDataString(projectIdentifier)}",
                JsonOptions,
                cancellationToken);

            if (project is null || project.ProjectId <= 0)
            {
                throw new InvalidOperationException($"Project '{projectIdentifier}' was not found.");
            }

            return project.ProjectId;
        }

        private async Task<int?> ResolveTemplateIdAsync(int projectId, string templateIdentifier, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(templateIdentifier))
            {
                return null;
            }

            if (int.TryParse(templateIdentifier, out var parsedId) && parsedId > 0)
            {
                return parsedId;
            }

            var templates = await _httpClient.GetFromJsonAsync<List<EnvironmentTemplateApiModel>>(
                $"EphemeralEnvironments/templates?projectId={projectId}",
                JsonOptions,
                cancellationToken);

            var match = templates?.FirstOrDefault(t => string.Equals(t.Name, templateIdentifier, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                throw new InvalidOperationException($"Environment template '{templateIdentifier}' was not found for project {projectId}.");
            }

            return match.Id;
        }

        private async Task<int?> ResolveBaseEnvironmentIdAsync(string baseEnvIdentifier, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(baseEnvIdentifier))
            {
                return null;
            }

            if (int.TryParse(baseEnvIdentifier, out var parsedId) && parsedId > 0)
            {
                return parsedId;
            }

            var environments = await _httpClient.GetFromJsonAsync<List<EnvironmentApiModel>>(
                $"RefDataEnvironments?env={Uri.EscapeDataString(baseEnvIdentifier)}",
                JsonOptions,
                cancellationToken);

            var match = environments?.FirstOrDefault(e => string.Equals(e.EnvironmentName, baseEnvIdentifier, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                throw new InvalidOperationException($"Base environment '{baseEnvIdentifier}' was not found.");
            }

            return match.EnvironmentId;
        }

        private async Task<int> WaitForRequestCompletionAsync(
            int requestId,
            DateTimeOffset deadline,
            int effectiveTimeoutMinutes,
            CancellationToken cancellationToken)
        {
            var previousStatus = string.Empty;
            while (true)
            {
                if (_timeProvider() >= deadline)
                {
                    _output($"Timed out waiting for request {requestId} after {effectiveTimeoutMinutes} minutes.");
                    return 2;
                }

                var statusDto = await _httpClient.GetFromJsonAsync<RequestStatusDto>(
                    $"Request?id={requestId}",
                    JsonOptions,
                    cancellationToken);

                var currentStatus = statusDto?.Status;
                if (string.IsNullOrWhiteSpace(currentStatus))
                {
                    return 2;
                }

                if (!string.Equals(previousStatus, currentStatus, StringComparison.Ordinal))
                {
                    _output($"Request {requestId} changed status to {currentStatus}");
                    previousStatus = currentStatus;
                }

                if (string.Equals(currentStatus, "Completed", StringComparison.OrdinalIgnoreCase))
                {
                    return 0;
                }

                if (TerminalFailureStatuses.Contains(currentStatus))
                {
                    return 2;
                }

                await Task.Delay(_pollInterval, cancellationToken);
            }
        }

        private async Task<int> WaitForEphemeralEnvironmentReadyAsync(
            int projectId,
            string targetEnvName,
            DateTimeOffset deadline,
            int effectiveTimeoutMinutes,
            CancellationToken cancellationToken)
        {
            var previousState = string.Empty;
            while (true)
            {
                if (_timeProvider() >= deadline)
                {
                    _output($"Timed out waiting for ephemeral environment '{targetEnvName}' to reach Ready after {effectiveTimeoutMinutes} minutes.");
                    return 2;
                }

                var environments = await _httpClient.GetFromJsonAsync<List<EnvironmentApiModel>>(
                    $"EphemeralEnvironments?projectId={projectId}",
                    JsonOptions,
                    cancellationToken);

                var match = environments?.FirstOrDefault(
                    e => string.Equals(e.EnvironmentName, targetEnvName, StringComparison.OrdinalIgnoreCase));
                var currentState = match?.EphemeralState;
                if (string.IsNullOrWhiteSpace(currentState))
                {
                    _output($"Ephemeral environment '{targetEnvName}' was not found or has no lifecycle state.");
                    return 2;
                }

                if (!string.Equals(previousState, currentState, StringComparison.Ordinal))
                {
                    _output($"Ephemeral environment '{targetEnvName}' is in state '{currentState}'");
                    previousState = currentState;
                }

                if (string.Equals(currentState, EphemeralEnvironmentStates.Ready, StringComparison.OrdinalIgnoreCase))
                {
                    return 0;
                }

                if (TerminalFailedEphemeralStates.Contains(currentState))
                {
                    _output($"Ephemeral environment '{targetEnvName}' entered failure/terminal state '{currentState}'.");
                    return 2;
                }

                await Task.Delay(_pollInterval, cancellationToken);
            }
        }
    }
}
