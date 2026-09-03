using Dorc.ApiModel;
using Dorc.Core.Interfaces;
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
        private readonly DeploymentCompletionCardBuilder _cardBuilder;
        private readonly ILogger<TeamsBotNotificationSink> _logger;

        // Internal (not private) so unit tests can collapse the exponential back-off between retries.
        internal Func<int, TimeSpan> RetryDelayProvider { get; set; } = attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt));

        public TeamsBotNotificationSink(
            IOptions<TeamsBotOptions> options,
            IActiveDirectorySearcher directorySearcher,
            ITeamsConversationClient conversationClient,
            DeploymentCompletionCardBuilder cardBuilder,
            ILoggerFactory loggerFactory)
        {
            _options            = options.Value;
            _directorySearcher  = directorySearcher;
            _conversationClient = conversationClient;
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

            foreach (var orphan in requests.Where(r => string.IsNullOrWhiteSpace(r.UserName)))
            {
                _logger.LogWarning(
                    "Cannot send Teams notification for request {RequestId}: UserName is empty.",
                    orphan.Id);
            }

            var byRequester = requests
                .Where(r => !string.IsNullOrWhiteSpace(r.UserName))
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
