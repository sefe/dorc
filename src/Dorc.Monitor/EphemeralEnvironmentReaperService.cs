using System.Text.Json;
using Dorc.ApiModel;
using Dorc.Core;
using Dorc.Core.HighAvailability;
using Dorc.Core.Interfaces;
using Dorc.Monitor.Notifications;
using Dorc.Monitor.Notifications.Teams;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dorc.Monitor
{
    public interface ITeardownFailureAlertPublisher
    {
        Task<bool> PublishTeardownFailureAlertAsync(
            EnvironmentApiModel environment,
            string reason,
            CancellationToken cancellationToken = default);
    }

    public sealed class LoggingTeardownFailureAlertPublisher : ITeardownFailureAlertPublisher
    {
        private readonly ILogger<LoggingTeardownFailureAlertPublisher> _logger;
        private readonly Func<EnvironmentApiModel, string, CancellationToken, Task<bool>>? _externalChannelSender;

        public LoggingTeardownFailureAlertPublisher(
            ILogger<LoggingTeardownFailureAlertPublisher> logger,
            Func<EnvironmentApiModel, string, CancellationToken, Task<bool>>? externalChannelSender = null)
        {
            _logger = logger;
            _externalChannelSender = externalChannelSender;
        }

        internal static async Task<bool> SendTeamsTeardownAlertAsync(
            ITeamsConversationClient? conversationClient,
            IActiveDirectorySearcher? directorySearcher,
            TeamsBotOptions? options,
            EnvironmentApiModel env,
            string reason,
            CancellationToken cancellationToken = default,
            IDeploymentNotificationSink? fallbackNotificationSink = null,
            IDeploymentEventsPublisher? eventsPublisher = null)
        {
            var nonTeamsFallback = fallbackNotificationSink is Dorc.Monitor.Notifications.Teams.TeamsBotNotificationSink
                ? null
                : fallbackNotificationSink;

            var owner = env.Details?.EnvironmentOwner;
            if (conversationClient is null || directorySearcher is null || options is null || !options.Enabled || string.IsNullOrWhiteSpace(owner))
            {
                return await SendNotificationAlertAsync(nonTeamsFallback, env, reason, eventsPublisher);
            }

            var ownerLookup = owner.Contains('\\')
                ? owner[(owner.LastIndexOf('\\') + 1)..]
                : owner;

            UserElementApiModel? user;
            try
            {
                user = directorySearcher.Search(ownerLookup)?
                    .FirstOrDefault(u =>
                        !u.IsGroup &&
                        (string.Equals(u.Username, ownerLookup, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(u.Email, ownerLookup, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(u.DisplayName, ownerLookup, StringComparison.OrdinalIgnoreCase)))
                    ?? directorySearcher.GetUserData(ownerLookup);
            }
            catch
            {
                return nonTeamsFallback is not null &&
                       await SendNotificationAlertAsync(nonTeamsFallback, env, reason, eventsPublisher);
            }

            if (string.IsNullOrWhiteSpace(user?.Pid))
            {
                return await SendNotificationAlertAsync(nonTeamsFallback, env, reason, eventsPublisher);
            }

            try
            {

                var tier = !string.IsNullOrWhiteSpace(env.EnvironmentTier) ? env.EnvironmentTier : "unspecified";
                var state = !string.IsNullOrWhiteSpace(env.EphemeralState)
                    ? env.EphemeralState
                    : EphemeralEnvironmentStates.TeardownFailed;

                var body = new List<object>
                {
                    new Dictionary<string, object?>
                    {
                        ["type"] = "TextBlock",
                        ["text"] = $"🚨 Ephemeral Environment Teardown Alert ({env.EnvironmentName})",
                        ["weight"] = "Bolder",
                        ["size"] = "Medium",
                        ["wrap"] = true
                    },
                    new Dictionary<string, object?>
                    {
                        ["type"] = "FactSet",
                        ["facts"] = new object[]
                        {
                            new Dictionary<string, object?> { ["title"] = "Environment", ["value"] = env.EnvironmentName },
                            new Dictionary<string, object?> { ["title"] = "Tier", ["value"] = tier },
                            new Dictionary<string, object?> { ["title"] = "Owner", ["value"] = owner },
                            new Dictionary<string, object?> { ["title"] = "State", ["value"] = state },
                            new Dictionary<string, object?> { ["title"] = "Reason", ["value"] = reason }
                        }
                    }
                };

                var card = new Dictionary<string, object?>
                {
                    ["type"] = "AdaptiveCard",
                    ["version"] = "1.4",
                    ["fallbackText"] = $"Ephemeral environment '{env.EnvironmentName}' failed teardown: {reason}",
                    ["body"] = body,
                    ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json"
                };

                if (!string.IsNullOrWhiteSpace(options.DorcUiBaseUrl))
                {
                    card["actions"] = new object[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["type"] = "Action.OpenUrl",
                            ["title"] = "Open environment",
                            ["url"] = $"{options.DorcUiBaseUrl.TrimEnd('/')}/environment/{Uri.EscapeDataString(env.EnvironmentName)}/metadata"
                        }
                    };
                }

                var conversationId = await conversationClient.CreateConversationAsync(user.Pid, cancellationToken);
                await conversationClient.SendCardAsync(conversationId, JsonSerializer.Serialize(card), cancellationToken);
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static async Task<bool> SendNotificationAlertAsync(
            IDeploymentNotificationSink? notificationService,
            EnvironmentApiModel env,
            string reason,
            IDeploymentEventsPublisher? eventsPublisher = null)
        {
            if (eventsPublisher is not null)
            {
                await eventsPublisher.PublishRequestStatusChangedAsync(
                    new Dorc.Core.Events.DeploymentRequestEventData(
                        RequestId: -Math.Abs(env.EnvironmentId),
                        Status: $"TeardownFailed:{env.EnvironmentName}:{reason}",
                        StartedTime: null,
                        CompletedTime: DateTimeOffset.UtcNow,
                        Timestamp: DateTimeOffset.UtcNow));
            }

            if (notificationService is null or NoOpDeploymentNotificationSink)
            {
                return false;
            }

            var owner = env.Details?.EnvironmentOwner;
            if (string.IsNullOrWhiteSpace(owner))
            {
                return false;
            }

            var tier = !string.IsNullOrWhiteSpace(env.EnvironmentTier) ? env.EnvironmentTier : "unspecified";
            var state = !string.IsNullOrWhiteSpace(env.EphemeralState)
                ? env.EphemeralState
                : EphemeralEnvironmentStates.TeardownFailed;
            var formattedMessage =
                $"Environment: {env.EnvironmentName} | Tier: {tier} | Owner: {owner} | State: {state} | {reason}";

            try
            {
                var now = DateTimeOffset.UtcNow;
                await notificationService.NotifyRequestCompletedAsync(
                    new DeploymentRequestApiModel
                    {
                        Id = 0,
                        Project = "EphemeralReaper",
                        EnvironmentName = env.EnvironmentName,
                        BuildNumber = formattedMessage,
                        UserName = owner,
                        Log = formattedMessage
                    },
                    DeploymentRequestStatus.Failed.ToString(),
                    now,
                    now);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> PublishTeardownFailureAlertAsync(
            EnvironmentApiModel environment,
            string reason,
            CancellationToken cancellationToken = default)
        {
            _logger.LogCritical(
                "PERSISTENT TEARDOWN ALERT: Ephemeral environment '{EnvironmentName}' (Id: {EnvironmentId}) failed teardown: {Reason}",
                environment.EnvironmentName,
                environment.EnvironmentId,
                reason);

            if (_externalChannelSender is not null)
            {
                try
                {
                    return await _externalChannelSender(environment, reason, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "External teardown alert delivery failed for environment '{EnvironmentName}' (Id: {EnvironmentId}).",
                        environment.EnvironmentName,
                        environment.EnvironmentId);
                    return false;
                }
            }

            return true;
        }
    }

    public sealed class EphemeralEnvironmentReaperService : BackgroundService
    {
        public const string ReaperLockKey = "lock:ephemeral-environment-reaper";
        public const int ReaperLockLeaseTimeMs = 30000;
        public const string ReaperActor = "system:ephemeral-reaper";

        private readonly ILogger<EphemeralEnvironmentReaperService> _logger;
        private readonly IEnvironmentsPersistentSource _environmentsPersistentSource;
        private readonly IEnvironmentTemplatesPersistentSource _environmentTemplatesPersistentSource;
        private readonly IProjectsPersistentSource _projectsPersistentSource;
        private readonly IRequestsPersistentSource _requestsPersistentSource;
        private readonly IDistributedLockService _distributedLockService;
        private readonly ITeardownFailureAlertPublisher? _alertPublisher;
        private readonly TimeSpan _pollInterval;
        private readonly HashSet<int> _loggedTeardownFailedEnvironmentIds = new();
        private readonly HashSet<int> _alertedTeardownFailedEnvironmentIds = new();
        private readonly DeploymentRequestDetailSerializer _serializer = new();

        public Action<EnvironmentApiModel, string>? OnTeardownFailedAlert { get; set; }

        public EphemeralEnvironmentReaperService(
            ILogger<EphemeralEnvironmentReaperService> logger,
            IEnvironmentsPersistentSource environmentsPersistentSource,
            IEnvironmentTemplatesPersistentSource environmentTemplatesPersistentSource,
            IProjectsPersistentSource projectsPersistentSource,
            IRequestsPersistentSource requestsPersistentSource,
            IDistributedLockService distributedLockService,
            TimeSpan? pollInterval = null,
            ITeardownFailureAlertPublisher? alertPublisher = null)
        {
            _logger = logger;
            _environmentsPersistentSource = environmentsPersistentSource;
            _environmentTemplatesPersistentSource = environmentTemplatesPersistentSource;
            _projectsPersistentSource = projectsPersistentSource;
            _requestsPersistentSource = requestsPersistentSource;
            _distributedLockService = distributedLockService;
            _pollInterval = pollInterval ?? TimeSpan.FromMinutes(1);
            _alertPublisher = alertPublisher;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("EphemeralEnvironmentReaperService started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    try
                    {
                        await ReapExpiredEnvironmentsAsync(DateTime.UtcNow, stoppingToken);
                    }
                    catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                    {
                        _logger.LogError(ex, "Unhandled error during ephemeral environment reaper tick.");
                    }

                    await Task.Delay(_pollInterval, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }

            _logger.LogInformation("EphemeralEnvironmentReaperService stopped.");
        }

        public async Task ReapExpiredEnvironmentsAsync(DateTime utcNow, CancellationToken cancellationToken)
        {
            IDistributedLock? lockHandle = null;
            if (_distributedLockService.IsEnabled)
            {
                lockHandle = await _distributedLockService.TryAcquireLockAsync(
                    ReaperLockKey,
                    ReaperLockLeaseTimeMs,
                    cancellationToken);

                if (lockHandle is null)
                {
                    _logger.LogDebug("Skipping ephemeral reaper tick because distributed lock '{LockKey}' is held by another instance.", ReaperLockKey);
                    return;
                }
            }

            await using (lockHandle)
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    lockHandle?.LockLostToken ?? CancellationToken.None);

                var expiredEnvironments = _environmentsPersistentSource.GetExpiredEphemeralEnvironments(
                    utcNow,
                    allowReclaimStrandedTearingDown: true);

                foreach (var environment in expiredEnvironments)
                {
                    if (linkedCts.Token.IsCancellationRequested)
                    {
                        _logger.LogWarning("Aborting ephemeral reaper tick because cancellation or distributed lock loss was signalled.");
                        return;
                    }

                    if (!environment.IsEphemeral || environment.EnvironmentIsProd)
                    {
                        _logger.LogWarning(
                            "Skipping non-ephemeral or production environment '{EnvironmentName}' returned in expired ephemeral query.",
                            environment.EnvironmentName);
                        continue;
                    }

                    await ReapSingleEnvironmentAsync(environment, utcNow, linkedCts.Token);
                }

                await EmitAlertsForTeardownFailedEnvironmentsAsync(linkedCts.Token);
            }
        }

        private async Task ReapSingleEnvironmentAsync(
            EnvironmentApiModel environment,
            DateTime utcNow,
            CancellationToken cancellationToken)
        {
            try
            {
                var isStrandedTearingDown = string.Equals(
                    environment.EphemeralState,
                    EphemeralEnvironmentStates.TearingDown,
                    StringComparison.OrdinalIgnoreCase);

                if (isStrandedTearingDown && HasActiveDestroyRequest(environment.EnvironmentName))
                {
                    _logger.LogDebug(
                        "Skipping expired ephemeral environment '{EnvironmentName}' ({EnvironmentId}) in TearingDown because an active destroy request is already in flight.",
                        environment.EnvironmentName,
                        environment.EnvironmentId);
                    return;
                }

                var claimed = isStrandedTearingDown
                    ? _environmentsPersistentSource.TryClaimExpiredEphemeralEnvironmentForTeardown(
                        environment.EnvironmentId,
                        utcNow,
                        allowReclaimStrandedTearingDown: true)
                    : _environmentsPersistentSource.TryClaimExpiredEphemeralEnvironmentForTeardown(
                        environment.EnvironmentId,
                        utcNow);

                if (!claimed)
                {
                    _logger.LogInformation(
                        "Skipping expired ephemeral environment '{EnvironmentName}' ({EnvironmentId}) because atomic teardown claim returned false.",
                        environment.EnvironmentName,
                        environment.EnvironmentId);
                    return;
                }

                var hasInFlightCancellingRequests = CancelInFlightNonDestroyRequests(environment.EnvironmentName);

                var template = environment.TemplateId.HasValue
                    ? _environmentTemplatesPersistentSource.GetTemplateById(environment.TemplateId.Value)
                    : null;

                var (projectBatches, hasUnresolvedDurableComponents) =
                    ResolveTargetTerraformComponents(environment, template);

                if (hasUnresolvedDurableComponents)
                {
                    _environmentsPersistentSource.UpdateEphemeralState(
                        environment.EnvironmentId,
                        EphemeralEnvironmentStates.TeardownFailed);
                    await EmitTeardownFailedAlertOnceAsync(
                        environment,
                        $"Provisioned Terraform component IDs for ephemeral environment '{environment.EnvironmentName}' could not be resolved across any mapped project.",
                        cancellationToken);
                    return;
                }

                var retainMetadataAfterTeardown = environment.ExpiresAtUtc == DateTime.UnixEpoch;
                if (projectBatches.Count == 0)
                {
                    if (hasInFlightCancellingRequests)
                    {
                        if (!retainMetadataAfterTeardown)
                        {
                            _environmentsPersistentSource.UpdateEphemeralState(
                                environment.EnvironmentId,
                                EphemeralEnvironmentStates.TearingDown,
                                expiresAtUtc: DateTime.UnixEpoch.AddSeconds(1));
                        }
                        _logger.LogInformation(
                            "Deferring metadata deletion for expired ephemeral environment '{EnvironmentName}' while in-flight requests finish cancelling.",
                            environment.EnvironmentName);
                        return;
                    }

                    if (retainMetadataAfterTeardown || !_environmentsPersistentSource.DeleteEnvironmentInternal(environment.EnvironmentId))
                    {
                        _environmentsPersistentSource.UpdateEphemeralState(
                            environment.EnvironmentId,
                            EphemeralEnvironmentStates.TornDown);
                    }
                    _logger.LogInformation(
                        "Expired ephemeral environment '{EnvironmentName}' had no Terraform components and was cleaned up.",
                        environment.EnvironmentName);
                    return;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    _environmentsPersistentSource.UpdateEphemeralState(
                        environment.EnvironmentId,
                        EphemeralEnvironmentStates.TeardownFailed);
                    _logger.LogWarning(
                        "Aborting TTL teardown submission for '{EnvironmentName}' because cancellation or distributed lock loss was signalled before SubmitRequest.",
                        environment.EnvironmentName);
                    return;
                }

                var submittedDestroyIds = new List<int>();
                for (var batchIndex = 0; batchIndex < projectBatches.Count; batchIndex++)
                {
                    var (project, reversedTerraformComponents) = projectBatches[batchIndex];
                    var provisionedRequestProps = _requestsPersistentSource.GetProvisionedRequestPropertiesForEnvironment(
                        environment.EnvironmentId,
                        project.ProjectName);

                    var destroyProperties = new List<PropertyPair>(
                        provisionedRequestProps.Select(p => new PropertyPair(p.PropertyName, p.PropertyValue)
                        {
                            IsSensitive = p.IsSensitive
                        }))
                    {
                        new(TerraformDestroyOperationConstants.PropertyName, TerraformDestroyOperationConstants.DestroyValue),
                        new(TerraformDestroyOperationConstants.IsDestroyPropertyName, "true"),
                        new(TerraformDestroyOperationConstants.DestroyOrderPreReversedPropertyName, "true"),
                        new(
                            TerraformDestroyOperationConstants.DestroyComponentIdsPropertyName,
                            string.Join(",", reversedTerraformComponents.Select(c => c.ComponentId))),
                        new(TerraformDestroyOperationConstants.DeleteEnvironmentOnSuccessPropertyName, retainMetadataAfterTeardown ? "false" : "true"),
                        new(TerraformDestroyOperationConstants.EphemeralEnvironmentIdPropertyName, environment.EnvironmentId.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    };
                    if (projectBatches.Count > 1)
                    {
                        destroyProperties.Add(new PropertyPair(
                            TerraformDestroyOperationConstants.DestroyBatchCountPropertyName,
                            projectBatches.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                        if (submittedDestroyIds.Count > 0)
                        {
                            destroyProperties.Add(new PropertyPair(
                                TerraformDestroyOperationConstants.SiblingDestroyRequestIdsPropertyName,
                                string.Join(",", submittedDestroyIds)));
                        }
                    }

                    var nonCatalogComponent = reversedTerraformComponents.FirstOrDefault(
                        c => c.TerraformSourceType != TerraformSourceType.Catalog);
                    var priorRequest = nonCatalogComponent is not null
                        ? _requestsPersistentSource.GetLatestAppliedRequestForEnvironment(
                            environment.EnvironmentName,
                            project.ProjectName,
                            nonCatalogComponent.ComponentId)
                        : null;
                    var destroyBuildUri = priorRequest?.BuildUri;
                    var destroyDropLocation = priorRequest?.DropLocation ?? string.Empty;
                    var destroyBuildNumber = !string.IsNullOrWhiteSpace(priorRequest?.BuildNumber)
                        ? priorRequest.BuildNumber
                        : "ephemeral-ttl-destroy";

                    var requestDetail = new DeploymentRequestDetail
                    {
                        EnvironmentName = environment.EnvironmentName,
                        BuildDetail = new BuildDetail
                        {
                            Project = project.ProjectName,
                            Uri = destroyBuildUri ?? string.Empty,
                            DropLocation = destroyDropLocation,
                            BuildNumber = destroyBuildNumber
                        },
                        Components = reversedTerraformComponents.Select(c => c.ComponentName).ToList(),
                        Properties = destroyProperties
                    };

                    var requestId = _requestsPersistentSource.SubmitRequest(new DeploymentRequest
                    {
                        RequestDetails = _serializer.Serialize(requestDetail),
                        UserName = ReaperActor,
                        RequestedTime = DateTimeOffset.UtcNow,
                        Project = project.ProjectName,
                        Environment = environment.EnvironmentName,
                        BuildNumber = destroyBuildNumber,
                        Components = string.Join("|", requestDetail.Components),
                        IsProd = false
                    });

                    if (requestId <= 0)
                    {
                        throw new InvalidOperationException($"SubmitRequest returned invalid ID {requestId}.");
                    }

                    submittedDestroyIds.Add(requestId);
                    _logger.LogInformation(
                        "Submitted TTL teardown destroy request {RequestId} for expired ephemeral environment '{EnvironmentName}' (project '{ProjectName}').",
                        requestId,
                        environment.EnvironmentName,
                        project.ProjectName);
                }
            }
            catch (Exception ex)
            {
                _environmentsPersistentSource.UpdateEphemeralState(
                    environment.EnvironmentId,
                    EphemeralEnvironmentStates.TeardownFailed);

                await EmitTeardownFailedAlertOnceAsync(
                    environment,
                    $"TTL teardown submission failed for ephemeral environment '{environment.EnvironmentName}': {ex.Message}",
                    cancellationToken);
            }
        }

        private IEnumerable<DeploymentRequestApiModel> GetNonTerminalRequests()
        {
            var primary = _requestsPersistentSource.GetRequestsWithStatus(
                DeploymentRequestStatus.Pending,
                DeploymentRequestStatus.Running,
                DeploymentRequestStatus.Requesting,
                DeploymentRequestStatus.WaitingConfirmation,
                isProd: false);
            var secondary = _requestsPersistentSource.GetRequestsWithStatus(
                DeploymentRequestStatus.Confirmed,
                DeploymentRequestStatus.Paused,
                isProd: false);
            var cancelling = _requestsPersistentSource.GetRequestsWithStatus(
                DeploymentRequestStatus.Cancelling,
                isProd: false);
            return primary.Concat(secondary).Concat(cancelling);
        }

        private bool HasActiveDestroyRequest(string environmentName)
        {
            return GetNonTerminalRequests().Any(request =>
                string.Equals(request.EnvironmentName, environmentName, StringComparison.OrdinalIgnoreCase)
                && IsDestroyRequest(request.RequestDetails));
        }

        private bool CancelInFlightNonDestroyRequests(string environmentName)
        {
            var hasInFlightCancellingRequests = false;
            foreach (var request in GetNonTerminalRequests())
            {
                if (!string.Equals(request.EnvironmentName, environmentName, StringComparison.OrdinalIgnoreCase)
                    || IsDestroyRequest(request.RequestDetails))
                {
                    continue;
                }

                if (string.Equals(request.Status, DeploymentRequestStatus.Cancelling.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    hasInFlightCancellingRequests = true;
                    continue;
                }

                var isRunning = string.Equals(request.Status, DeploymentRequestStatus.Running.ToString(), StringComparison.OrdinalIgnoreCase)
                    || string.Equals(request.Status, DeploymentRequestStatus.Requesting.ToString(), StringComparison.OrdinalIgnoreCase);

                if (isRunning)
                {
                    hasInFlightCancellingRequests = true;
                }

                var nextStatus = isRunning
                    ? DeploymentRequestStatus.Cancelling
                    : DeploymentRequestStatus.Cancelled;

                _requestsPersistentSource.UpdateRequestStatus(
                    request.Id,
                    nextStatus,
                    ReaperActor,
                    DateTimeOffset.UtcNow);
            }

            hasInFlightCancellingRequests |= GetNonTerminalRequests().Any(r =>
                string.Equals(r.EnvironmentName, environmentName, StringComparison.OrdinalIgnoreCase)
                && !IsDestroyRequest(r.RequestDetails)
                && string.Equals(r.Status, DeploymentRequestStatus.Cancelling.ToString(), StringComparison.OrdinalIgnoreCase));

            return hasInFlightCancellingRequests;
        }

        private (List<(ProjectApiModel Project, List<ComponentApiModel> Components)> ProjectBatches, bool HasUnresolvedDurableComponents)
            ResolveTargetTerraformComponents(
                EnvironmentApiModel environment,
                EnvironmentTemplateApiModel? template)
        {
            var candidateProjects = new List<ProjectApiModel>();
            var seenProjectIds = new HashSet<int>();

            if (template is not null)
            {
                var templateProject = _projectsPersistentSource.GetProject(template.ProjectId);
                if (templateProject is not null)
                {
                    seenProjectIds.Add(templateProject.ProjectId);
                    candidateProjects.Add(templateProject);
                }
            }

            var mappedProjects = _environmentsPersistentSource.GetMappedProjects(environment.EnvironmentName);
            if (mappedProjects is not null)
            {
                foreach (var mapped in mappedProjects)
                {
                    if (seenProjectIds.Add(mapped.ProjectId))
                    {
                        candidateProjects.Add(mapped);
                    }
                }
            }

            var allComponents = new List<(ProjectApiModel Project, ComponentApiModel Component)>();
            var seenProjectComponents = new HashSet<(int ProjectId, int ComponentId)>();
            foreach (var proj in candidateProjects)
            {
                var projComponents = _projectsPersistentSource.GetComponentsForProject(proj.ProjectId);
                foreach (var comp in FlattenComponents(projComponents))
                {
                    if (!comp.ComponentId.HasValue || comp.ComponentType != ComponentType.Terraform || seenProjectComponents.Add((proj.ProjectId, comp.ComponentId.Value)))
                    {
                        allComponents.Add((proj, comp));
                    }
                }
            }

            foreach (var (pinnedProj, pinnedComp) in _requestsPersistentSource.GetProvisionedComponentDefinitionsForEnvironment(environment.EnvironmentId))
            {
                if (seenProjectComponents.Add((pinnedProj.ProjectId, pinnedComp.ComponentId.GetValueOrDefault())))
                {
                    allComponents.Add((pinnedProj, pinnedComp));
                }
            }

            var leafTerraformComponents = allComponents
                .Where(entry => IsLeafTerraformComponent(entry.Component))
                .ToList();
            var byId = allComponents
                .Where(entry => entry.Component.ComponentType == ComponentType.Terraform && entry.Component.ComponentId.HasValue)
                .ToLookup(entry => entry.Component.ComponentId!.Value);

            var durableOrderedIds = _requestsPersistentSource.GetProvisionedComponentIdsForEnvironment(environment.EnvironmentId) ?? Array.Empty<int>();
            if (durableOrderedIds.Any(id => !byId.Contains(id)))
            {
                return (new List<(ProjectApiModel, List<ComponentApiModel>)>(), true);
            }

            var selected = new List<(ProjectApiModel Project, ComponentApiModel Component)>();
            var selectedPairs = new HashSet<(int ProjectId, int ComponentId)>();

            var isUnprovisioned =
                string.Equals(environment.EphemeralState, EphemeralEnvironmentStates.Provisioning, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(environment.EphemeralState, EphemeralEnvironmentStates.ProvisionFailed, StringComparison.OrdinalIgnoreCase) ||
                environment.ExpiresAtUtc <= DateTime.UnixEpoch.AddSeconds(1);

            var inventoryIds = (_requestsPersistentSource.GetTerraformInventoryForEnvironment(environment.EnvironmentId) ?? Array.Empty<TerraformInventoryProvenanceApiModel>())
                .Select(i => i.ComponentId)
                .ToHashSet();
            var recordedStatusNames = (_environmentsPersistentSource.GetEnvironmentComponentStatuses(environment.EnvironmentId) ?? Array.Empty<EnvironmentComponentStatusModel>())
                .Where(s => !string.IsNullOrWhiteSpace(s.Component) && !string.Equals(s.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
                .Select(s => s.Component!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var fallbackComponentIds = leafTerraformComponents
                .Where(e => e.Component.ComponentId.HasValue)
                .Select(e => e.Component)
                .DistinctBy(c => c.ComponentId!.Value)
                .Where(c =>
                    inventoryIds.Contains(c.ComponentId!.Value) ||
                    recordedStatusNames.Contains(c.ComponentName))
                .Select(c => c.ComponentId!.Value);

            var orderedCandidateIds = (durableOrderedIds.Count > 0 ||
                                       template is null ||
                                       isUnprovisioned
                    ? durableOrderedIds
                    : template.ProvisionComponentIds.Where(byId.Contains))
                .Concat(fallbackComponentIds);

            foreach (var id in orderedCandidateIds)
            {
                var candidatesForId = byId[id].ToList();
                var appliedProjects = _requestsPersistentSource.GetProvisionedProjectsForComponent(environment.EnvironmentId, id);
                List<(ProjectApiModel Project, ComponentApiModel Component)> resolvedEntries;
                if (appliedProjects.Count > 0)
                {
                    resolvedEntries = new List<(ProjectApiModel Project, ComponentApiModel Component)>();
                    foreach (var appliedProject in appliedProjects)
                    {
                        var match = candidatesForId.FirstOrDefault(entry =>
                            string.Equals(entry.Project.ProjectName, appliedProject, StringComparison.OrdinalIgnoreCase));
                        if (match.Project is null)
                        {
                            return (new List<(ProjectApiModel, List<ComponentApiModel>)>(), true);
                        }
                        resolvedEntries.Add(match);
                    }
                }
                else
                {
                    var preferred = candidatesForId.FirstOrDefault(entry => entry.Project.ProjectId == template?.ProjectId);
                    resolvedEntries = new List<(ProjectApiModel Project, ComponentApiModel Component)>
                    {
                        preferred.Project is not null ? preferred : candidatesForId[0]
                    };
                }

                var takeSingleOccurrence = durableOrderedIds.Count(x => x == id) > 1;
                foreach (var entry in resolvedEntries)
                {
                    if (selectedPairs.Add((entry.Project.ProjectId, id)))
                    {
                        selected.Add(entry);
                        if (takeSingleOccurrence)
                        {
                            break;
                        }
                    }
                }
            }

            if (template is null && !isUnprovisioned && selected.Count == 0)
            {
                selected.AddRange(leafTerraformComponents.Where(e => e.Component.IsEnabled != false));
            }

            var batches = new List<(ProjectApiModel Project, List<ComponentApiModel> Components)>();
            string? currentBatchBuildIdentity = null;
            for (var i = selected.Count - 1; i >= 0; i--)
            {
                var entry = selected[i];
                var entryBuildIdentity = ResolveDestroyBuildIdentity(environment.EnvironmentName, entry.Project.ProjectName, entry.Component);
                if (batches.Count > 0 &&
                    batches[^1].Project.ProjectId == entry.Project.ProjectId &&
                    string.Equals(currentBatchBuildIdentity ?? entryBuildIdentity, entryBuildIdentity ?? currentBatchBuildIdentity, StringComparison.OrdinalIgnoreCase))
                {
                    batches[^1].Components.Add(entry.Component);
                    currentBatchBuildIdentity = entryBuildIdentity ?? currentBatchBuildIdentity;
                }
                else
                {
                    batches.Add((entry.Project, new List<ComponentApiModel> { entry.Component }));
                    currentBatchBuildIdentity = entryBuildIdentity;
                }
            }

            return (batches, false);
        }

        private string? ResolveDestroyBuildIdentity(
            string environmentName,
            string projectName,
            ComponentApiModel component)
        {
            if (component.TerraformSourceType == TerraformSourceType.Catalog)
            {
                return null;
            }

            var prior = _requestsPersistentSource.GetLatestAppliedRequestForEnvironment(
                environmentName,
                projectName,
                component.ComponentId);
            return prior is null
                ? null
                : $"{prior.BuildUri}|{prior.DropLocation}|{prior.BuildNumber}";
        }

        private static IEnumerable<ComponentApiModel> FlattenComponents(IEnumerable<ComponentApiModel> components)
        {
            foreach (var comp in components)
            {
                yield return comp;
                if (comp.Children is { Count: > 0 })
                {
                    foreach (var child in FlattenComponents(comp.Children))
                    {
                        yield return child;
                    }
                }
            }
        }

        private static bool IsLeafTerraformComponent(ComponentApiModel component)
        {
            return component.ComponentType == ComponentType.Terraform
                && (component.Children is null || !component.Children.Any());
        }

        private async Task EmitAlertsForTeardownFailedEnvironmentsAsync(CancellationToken cancellationToken)
        {
            var failedEnvironments = _environmentsPersistentSource
                .GetEphemeralEnvironments(state: EphemeralEnvironmentStates.TeardownFailed)
                .ToList();

            var currentFailedIds = failedEnvironments.Select(e => e.EnvironmentId).ToHashSet();
            _loggedTeardownFailedEnvironmentIds.RemoveWhere(id => !currentFailedIds.Contains(id));
            _alertedTeardownFailedEnvironmentIds.RemoveWhere(id => !currentFailedIds.Contains(id));

            foreach (var failedEnv in failedEnvironments)
            {
                await EmitTeardownFailedAlertOnceAsync(
                    failedEnv,
                    $"Ephemeral environment '{failedEnv.EnvironmentName}' is in TeardownFailed state and requires operator attention.",
                    cancellationToken);
            }
        }

        private async Task EmitTeardownFailedAlertOnceAsync(
            EnvironmentApiModel environment,
            string reason,
            CancellationToken cancellationToken)
        {
            if (_alertedTeardownFailedEnvironmentIds.Contains(environment.EnvironmentId))
            {
                return;
            }

            var isFirstNotificationForEpisode = _loggedTeardownFailedEnvironmentIds.Add(environment.EnvironmentId);
            if (isFirstNotificationForEpisode)
            {
                _logger.LogError(
                    "ALERT: Ephemeral environment '{EnvironmentName}' (Id: {EnvironmentId}) entered TeardownFailed state. {Reason}",
                    environment.EnvironmentName,
                    environment.EnvironmentId,
                    reason);
            }

            var delivered = true;
            if (_alertPublisher is not null)
            {
                try
                {
                    delivered = await _alertPublisher.PublishTeardownFailureAlertAsync(
                        environment,
                        reason,
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    delivered = false;
                    _logger.LogWarning(
                        ex,
                        "Failed to publish teardown failure alert for ephemeral environment '{EnvironmentName}' ({EnvironmentId}).",
                        environment.EnvironmentName,
                        environment.EnvironmentId);
                }
            }

            if (delivered)
            {
                _alertedTeardownFailedEnvironmentIds.Add(environment.EnvironmentId);
            }

            if (isFirstNotificationForEpisode)
            {
                OnTeardownFailedAlert?.Invoke(environment, reason);
            }
        }

        private static bool IsDestroyRequest(string? requestDetailsXml)
        {
            return RequestsPersistentSource.IsDestroyRequestXml(requestDetailsXml);
        }
    }
}
