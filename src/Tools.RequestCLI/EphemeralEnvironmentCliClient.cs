using System;
using System.Collections.Generic;
using System.Diagnostics;
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

            return await ExecuteTeardownAsync(cliArgs, cancellationToken);
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
