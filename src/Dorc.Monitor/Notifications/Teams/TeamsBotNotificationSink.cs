using Dorc.ApiModel;
using Dorc.Core;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Rest;
using Polly;
using Polly.Retry;
using Polly.Timeout;

namespace Dorc.Monitor.Notifications.Teams
{
    internal sealed class TeamsBotNotificationSink : IDeploymentNotificationSink
    {
        private static readonly string[] DefaultNotifyOnStatuses = { "Completed", "Failed", "Errored" };

        private readonly TeamsBotOptions _options;
        private readonly HashSet<string> _notifyOnStatuses;
        private readonly IActiveDirectorySearcher _directorySearcher;
        private readonly ITeamsConversationClient _conversationClient;
        private readonly IRequestsPersistentSource _requestsPersistentSource;
        private readonly DeploymentCompletionCardBuilder _cardBuilder;
        private readonly ILogger<TeamsBotNotificationSink> _logger;
        private readonly DeploymentRequestDetailSerializer _requestDetailSerializer = new();

        private enum BundleNotificationResult
        {
            NotApplicable,
            Deferred,
            Sent
        }

        // Internal (not private) so unit tests can collapse the exponential back-off between retries.
        internal Func<int, TimeSpan> RetryDelayProvider { get; set; } = attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt));

        public TeamsBotNotificationSink(
            IOptions<TeamsBotOptions> options,
            IActiveDirectorySearcher directorySearcher,
            ITeamsConversationClient conversationClient,
            IRequestsPersistentSource requestsPersistentSource,
            DeploymentCompletionCardBuilder cardBuilder,
            ILoggerFactory loggerFactory)
        {
            _options            = options.Value;
            _directorySearcher  = directorySearcher;
            _conversationClient = conversationClient;
            _requestsPersistentSource = requestsPersistentSource;
            _cardBuilder        = cardBuilder;
            _logger             = loggerFactory.CreateLogger<TeamsBotNotificationSink>();
            _notifyOnStatuses   = ParseNotifyOnStatuses(_options.NotifyOnStatuses);

            _logger.LogInformation(
                "Teams notifications will be sent for statuses: {Statuses}.",
                string.Join(", ", _notifyOnStatuses));

            foreach (var status in _notifyOnStatuses.Where(s => !Enum.TryParse<DeploymentRequestStatus>(s, true, out _)))
            {
                _logger.LogWarning(
                    "Configured NotifyOnStatuses entry '{Status}' is not a known deployment request status and will never match.",
                    status);
            }
        }

        public async Task NotifyRequestCompletedAsync(
            DeploymentRequestApiModel request,
            string finalStatus,
            DateTimeOffset startedTime,
            DateTimeOffset completedTime)
        {
            if (!_options.Enabled)
                return;

            if (!_notifyOnStatuses.Contains(finalStatus))
            {
                _logger.LogDebug(
                    "Skipping Teams notification for request {RequestId}: status '{Status}' is not in NotifyOnStatuses.",
                    request.Id, finalStatus);
                return;
            }

            if (string.IsNullOrWhiteSpace(request.UserName))
            {
                _logger.LogWarning(
                    "Cannot send Teams notification for request {RequestId}: UserName is empty.",
                    request.Id);
                return;
            }

            try
            {
                if (TryGetBundleRequestIds(request, out var bundleRequestIds) && bundleRequestIds.Count > 1)
                {
                    var bundleResult = await TryNotifyBundleAsync(request, finalStatus, bundleRequestIds, completedTime);
                    if (bundleResult is BundleNotificationResult.Sent or BundleNotificationResult.Deferred)
                    {
                        return;
                    }
                }

                var aadObjectId = ResolveAadObjectId(request.UserName);
                if (aadObjectId is null)
                {
                    _logger.LogWarning(
                        "Cannot send Teams notification for request {RequestId}: could not resolve AAD object ID for user '{UserName}'.",
                        request.Id, request.UserName);
                    return;
                }

                await DispatchAdaptiveCardAsync(request, finalStatus, startedTime, completedTime, aadObjectId);

                _logger.LogInformation(
                    "Teams notification sent for request {RequestId} to user '{UserName}' (status: {Status}).",
                    request.Id, request.UserName, finalStatus);
            }
            catch (Exception ex) when (!FatalExceptions.Is(ex))
            {
                _logger.LogError(ex,
                    "Failed to send Teams notification for request {RequestId} to user '{UserName}'.",
                    request.Id, request.UserName);
            }
        }

        public async Task NotifyRequestsCompletedAsync(
            IReadOnlyCollection<DeploymentRequestApiModel> requests,
            string finalStatus,
            DateTimeOffset completedTime)
        {
            if (!_options.Enabled || requests.Count == 0)
                return;

            if (!_notifyOnStatuses.Contains(finalStatus))
            {
                _logger.LogDebug(
                    "Skipping Teams notification for {Count} request(s): status '{Status}' is not in NotifyOnStatuses.",
                    requests.Count, finalStatus);
                return;
            }

            var orphans = new List<DeploymentRequestApiModel>();
            var standaloneRequests = new List<DeploymentRequestApiModel>();
            var completedBundles = new Dictionary<string, List<DeploymentRequestApiModel>>(StringComparer.Ordinal);

            foreach (var request in requests)
            {
                if (string.IsNullOrWhiteSpace(request.UserName))
                {
                    orphans.Add(request);
                    continue;
                }

                if (TryGetBundleRequestIds(request, out var bundleRequestIds) && bundleRequestIds.Count > 1)
                {
                    var orderedIds = bundleRequestIds.OrderBy(id => id).ToArray();
                    var bundleKey = string.Join(",", orderedIds);
                    if (!completedBundles.ContainsKey(bundleKey))
                    {
                        var bundleRequests = TryResolveBundleRequests(orderedIds, requests, finalStatus, completedTime);
                        if (bundleRequests is null)
                        {
                            // A sibling could not be loaded; fall back to normal batch handling for this request.
                            standaloneRequests.Add(request);
                        }
                        else if (bundleRequests.All(r => IsTerminalStatus(r.Status)))
                        {
                            // Only emit the bundle notification if the last-completed request in the bundle
                            // is part of the transitions this Monitor instance just won.
                            var winnerId = SelectBundleWinnerId(bundleRequests);
                            if (requests.Any(r => r.Id == winnerId))
                            {
                                completedBundles[bundleKey] = bundleRequests;
                            }
                        }
                    }

                    continue;
                }

                standaloneRequests.Add(request);
            }

            foreach (var orphan in orphans)
            {
                _logger.LogWarning(
                    "Cannot send Teams notification for request {RequestId}: UserName is empty.",
                    orphan.Id);
            }

            foreach (var bundleRequests in completedBundles.Values)
            {
                await DispatchBundleByRequesterAsync(bundleRequests, completedTime);
            }

            var byRequester = standaloneRequests
                .GroupBy(r => r.UserName!, StringComparer.OrdinalIgnoreCase);

            // One message per requester. Each recipient is isolated: an unresolvable user or a
            // Bot Connector failure for one person must not stop the others being told.
            foreach (var group in byRequester)
            {
                var userName = group.Key;
                var forUser = group.ToList();

                try
                {
                    var aadObjectId = ResolveAadObjectId(userName);
                    if (aadObjectId is null)
                    {
                        _logger.LogWarning(
                            "Cannot send Teams notification for requests [{RequestIds}]: could not resolve AAD object ID for user '{UserName}'.",
                            string.Join(",", forUser.Select(r => r.Id)), userName);
                        continue;
                    }

                    if (forUser.Count == 1)
                    {
                        // A batch of one is just the normal card - no reason to degrade it to a summary.
                        var only = forUser[0];
                        await DispatchAdaptiveCardAsync(
                            only,
                            finalStatus,
                            only.StartedTime ?? only.RequestedTime ?? completedTime,
                            completedTime,
                            aadObjectId);
                    }
                    else
                    {
                        await DispatchBatchCardAsync(forUser, finalStatus, completedTime, aadObjectId);
                    }

                    _logger.LogInformation(
                        "Teams notification sent to user '{UserName}' covering {Count} request(s) (status: {Status}).",
                        userName, forUser.Count, finalStatus);
                }
                catch (Exception ex) when (!FatalExceptions.Is(ex))
                {
                    _logger.LogError(ex,
                        "Failed to send Teams notification to user '{UserName}' for requests [{RequestIds}].",
                        userName, string.Join(",", forUser.Select(r => r.Id)));
                }
            }
        }

        private string? ResolveAadObjectId(string userName)
        {
            try
            {
                var results = _directorySearcher.Search(userName);
                var match = results.FirstOrDefault(u =>
                    !u.IsGroup &&
                    (string.Equals(u.Username,    userName, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(u.Email,       userName, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(u.DisplayName, userName, StringComparison.OrdinalIgnoreCase)));

                return match?.Pid;
            }
            catch (Exception ex) when (!FatalExceptions.Is(ex))
            {
                _logger.LogError(ex, "Error resolving AAD object ID for user '{UserName}'.", userName);
                return null;
            }
        }

        private async Task DispatchAdaptiveCardAsync(
            DeploymentRequestApiModel request,
            string finalStatus,
            DateTimeOffset startedTime,
            DateTimeOffset completedTime,
            string aadObjectId)
        {
            var policy = BuildResiliencePolicy($"request {request.Id}");

            var conversationId = await policy.ExecuteAsync(
                ct => _conversationClient.CreateConversationAsync(aadObjectId, ct),
                CancellationToken.None);

            _logger.LogDebug("Created conversation {ConversationId} for user {User} (request {RequestId})",
                conversationId, request.UserName, request.Id);

            var cardJson = _cardBuilder.Build(request, finalStatus, startedTime, completedTime);

            await policy.ExecuteAsync(
                ct => _conversationClient.SendCardAsync(conversationId, cardJson, ct),
                CancellationToken.None);

            _logger.LogDebug("Adaptive card message sent to conversation {ConversationId}", conversationId);
        }

        private async Task DispatchBatchCardAsync(
            IReadOnlyCollection<DeploymentRequestApiModel> requests,
            string finalStatus,
            DateTimeOffset completedTime,
            string aadObjectId)
        {
            var policy = BuildResiliencePolicy($"{requests.Count} requests");

            var conversationId = await policy.ExecuteAsync(
                ct => _conversationClient.CreateConversationAsync(aadObjectId, ct),
                CancellationToken.None);

            var cardJson = _cardBuilder.BuildBatch(requests, finalStatus, completedTime);

            await policy.ExecuteAsync(
                ct => _conversationClient.SendCardAsync(conversationId, cardJson, ct),
                CancellationToken.None);

            _logger.LogDebug("Batch adaptive card sent to conversation {ConversationId}", conversationId);
        }

        private async Task DispatchBundleCardAsync(
            IReadOnlyCollection<DeploymentRequestApiModel> requests,
            DateTimeOffset completedTime,
            string aadObjectId)
        {
            var policy = BuildResiliencePolicy($"bundle ({requests.Count} requests)");

            var conversationId = await policy.ExecuteAsync(
                ct => _conversationClient.CreateConversationAsync(aadObjectId, ct),
                CancellationToken.None);

            var cardJson = _cardBuilder.BuildBundle(requests, completedTime);

            await policy.ExecuteAsync(
                ct => _conversationClient.SendCardAsync(conversationId, cardJson, ct),
                CancellationToken.None);

            _logger.LogDebug("Bundle adaptive card sent to conversation {ConversationId}", conversationId);
        }

        private async Task<BundleNotificationResult> TryNotifyBundleAsync(
            DeploymentRequestApiModel currentRequest,
            string finalStatus,
            IReadOnlyCollection<int> bundleRequestIds,
            DateTimeOffset completedTime)
        {
            var orderedIds = bundleRequestIds.OrderBy(id => id).ToArray();
            var requests = TryResolveBundleRequests(
                orderedIds,
                new[] { currentRequest },
                finalStatus,
                completedTime);

            // If any sibling row is missing or cannot be loaded, fall back to a single-request
            // notification rather than deferring indefinitely and dropping the notification.
            if (requests is null)
                return BundleNotificationResult.NotApplicable;

            if (!requests.All(r => IsTerminalStatus(r.Status)))
                return BundleNotificationResult.Deferred;

            // Deterministic HA guard: each request in the bundle is transitioned to its terminal
            // status by at most one Monitor instance. By assigning bundle notification ownership
            // to the single request with the latest CompletedTime (tie-broken by highest Request.Id),
            // only the Monitor instance that completed that final request sends the bundle card —
            // with no in-memory state that could duplicate across HA nodes or leak over time.
            var winnerId = SelectBundleWinnerId(requests);
            if (currentRequest.Id != winnerId)
                return BundleNotificationResult.Deferred;

            await DispatchBundleByRequesterAsync(requests, completedTime);

            return BundleNotificationResult.Sent;
        }

        private List<DeploymentRequestApiModel>? TryResolveBundleRequests(
            IReadOnlyCollection<int> orderedIds,
            IReadOnlyCollection<DeploymentRequestApiModel> justCompletedRequests,
            string finalStatus,
            DateTimeOffset completedTime)
        {
            var overrides = justCompletedRequests.ToDictionary(r => r.Id);
            var requests = new List<DeploymentRequestApiModel>(orderedIds.Count);

            foreach (var id in orderedIds)
            {
                var loaded = TryGetRequest(id);
                if (loaded is null)
                {
                    if (!overrides.TryGetValue(id, out var fallback))
                        return null;

                    loaded = new DeploymentRequestApiModel
                    {
                        Id = fallback.Id,
                        UserName = fallback.UserName,
                        Project = fallback.Project,
                        EnvironmentName = fallback.EnvironmentName,
                        BuildNumber = fallback.BuildNumber,
                        Components = fallback.Components,
                        RequestDetails = fallback.RequestDetails,
                        RequestedTime = fallback.RequestedTime,
                        StartedTime = fallback.StartedTime,
                        CompletedTime = fallback.CompletedTime ?? completedTime,
                        Status = finalStatus
                    };
                }
                else if (overrides.ContainsKey(id))
                {
                    if (!IsTerminalStatus(loaded.Status))
                        loaded.Status = finalStatus;
                    loaded.CompletedTime ??= completedTime;
                }

                requests.Add(loaded);
            }

            return requests;
        }

        private static int SelectBundleWinnerId(IReadOnlyCollection<DeploymentRequestApiModel> requests)
        {
            return requests
                .OrderByDescending(r => r.CompletedTime ?? DateTimeOffset.MinValue)
                .ThenByDescending(r => r.Id)
                .First()
                .Id;
        }

        private async Task DispatchBundleByRequesterAsync(
            IReadOnlyCollection<DeploymentRequestApiModel> requests,
            DateTimeOffset fallbackCompletedTime)
        {
            var bundleCompletedTime = requests
                .Select(r => r.CompletedTime)
                .Where(t => t.HasValue)
                .Select(t => t!.Value)
                .DefaultIfEmpty(fallbackCompletedTime)
                .Max();

            var byRequester = requests
                .Where(r => !string.IsNullOrWhiteSpace(r.UserName))
                .GroupBy(r => r.UserName!, StringComparer.OrdinalIgnoreCase);

            foreach (var group in byRequester)
            {
                var userName = group.Key;
                var forUser = group.ToList();

                try
                {
                    var aadObjectId = ResolveAadObjectId(userName);
                    if (aadObjectId is null)
                    {
                        _logger.LogWarning(
                            "Cannot send bundle Teams notification for requests [{RequestIds}]: could not resolve AAD object ID for user '{UserName}'.",
                            string.Join(",", forUser.Select(r => r.Id)), userName);
                        continue;
                    }

                    await DispatchBundleCardAsync(forUser, bundleCompletedTime, aadObjectId);

                    _logger.LogInformation(
                        "Bundle Teams notification sent to user '{UserName}' covering {Count} request(s).",
                        userName, forUser.Count);
                }
                catch (Exception ex) when (!FatalExceptions.Is(ex))
                {
                    _logger.LogError(ex,
                        "Failed to send bundle Teams notification to user '{UserName}' for requests [{RequestIds}].",
                        userName, string.Join(",", forUser.Select(r => r.Id)));
                }
            }
        }

        private bool TryGetBundleRequestIds(DeploymentRequestApiModel request, out List<int> requestIds)
        {
            requestIds = new List<int>();
            var requestDetails = request.RequestDetails;

            if (!TryParseBundleRequestIds(requestDetails, request.Id, out requestIds))
            {
                // MakeLikeProdController updates RequestDetails with AllRequestIds in the DB
                // after creating all requests in the bundle. If the Monitor picked up the first
                // request before that update completed, the in-memory RequestDetails on the
                // processor's DTO will not have AllRequestIds yet — re-check the persisted row.
                var persistedRequest = TryGetRequest(request.Id);
                if (persistedRequest is not null
                    && !string.IsNullOrWhiteSpace(persistedRequest.RequestDetails)
                    && !string.Equals(persistedRequest.RequestDetails, requestDetails, StringComparison.Ordinal))
                {
                    request.RequestDetails = persistedRequest.RequestDetails;
                    return TryParseBundleRequestIds(persistedRequest.RequestDetails, request.Id, out requestIds);
                }

                return false;
            }

            return true;
        }

        private bool TryParseBundleRequestIds(string? requestDetails, int requestId, out List<int> requestIds)
        {
            requestIds = new List<int>();
            if (string.IsNullOrWhiteSpace(requestDetails))
                return false;

            try
            {
                var details = _requestDetailSerializer.Deserialize(requestDetails);
                var allRequestIds = details.Properties
                    .FirstOrDefault(p => string.Equals(p.Name, "AllRequestIds", StringComparison.OrdinalIgnoreCase))
                    ?.Value;

                if (string.IsNullOrWhiteSpace(allRequestIds))
                    return false;

                requestIds = allRequestIds
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(value => int.TryParse(value, out var id) ? id : 0)
                    .Where(id => id > 0)
                    .Distinct()
                    .ToList();

                return requestIds.Count > 0;
            }
            catch (Exception ex) when (!FatalExceptions.Is(ex))
            {
                _logger.LogDebug(ex, "Failed to parse bundle request IDs from request {RequestId} details.", requestId);
                return false;
            }
        }

        private DeploymentRequestApiModel? TryGetRequest(int requestId)
        {
            try
            {
                return _requestsPersistentSource.GetRequest(requestId);
            }
            catch (Exception ex) when (!FatalExceptions.Is(ex))
            {
                _logger.LogWarning(ex, "Failed to load request {RequestId} while building bundle notification.", requestId);
                return null;
            }
        }

        private static bool IsTerminalStatus(string? status)
        {
            if (!Enum.TryParse<DeploymentRequestStatus>(status, true, out var parsed))
                return false;

            return parsed is DeploymentRequestStatus.Completed
                or DeploymentRequestStatus.Failed
                or DeploymentRequestStatus.Errored
                or DeploymentRequestStatus.Cancelled
                or DeploymentRequestStatus.Abandoned;
        }

        private AsyncPolicy BuildResiliencePolicy(string context)
        {
            AsyncTimeoutPolicy timeoutPolicy = Policy
                .TimeoutAsync(TimeSpan.FromSeconds(10), TimeoutStrategy.Optimistic);

            AsyncRetryPolicy retryPolicy = Policy
                .Handle<Exception>(IsRetryable)
                .WaitAndRetryAsync(
                    3,
                    retryAttempt => RetryDelayProvider(retryAttempt),
                    onRetry: (ex, ts, attempt, _) =>
                    {
                        _logger.LogWarning(ex,
                            "Retry {Attempt} sending Teams notification for {Context}. Waiting {Delay}.",
                            attempt, context, ts);
                    });

            return Policy.WrapAsync(retryPolicy, timeoutPolicy);
        }

        // Timeouts, cancellations, caller errors, 4xx Bot Connector responses (e.g. the user
        // does not have the bot app installed) and fatal CLR exceptions are not worth retrying.
        private static bool IsRetryable(Exception ex) =>
            ex is not ArgumentException
               and not TimeoutRejectedException
               and not OperationCanceledException
            && !IsClientError(ex)
            && !FatalExceptions.Is(ex);

        private static bool IsClientError(Exception ex) =>
            ex is HttpOperationException { Response: not null } httpEx
            && (int)httpEx.Response.StatusCode is >= 400 and < 500;

        private static HashSet<string> ParseNotifyOnStatuses(string? configuredStatuses)
        {
            var statuses = (configuredStatuses ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return statuses.Count > 0
                ? statuses
                : DefaultNotifyOnStatuses.ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
    }
}
