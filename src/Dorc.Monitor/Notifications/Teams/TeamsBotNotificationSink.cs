using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
            var policy = BuildResiliencePolicy(request.Id);

            var conversationId = await policy.ExecuteAsync(() =>
                _conversationClient.CreateConversationAsync(aadObjectId));

            _logger.LogDebug("Created conversation {ConversationId} for user {User} (request {RequestId})",
                conversationId, request.UserName, request.Id);

            var cardJson = _cardBuilder.Build(request, finalStatus, startedTime, completedTime);

            await policy.ExecuteAsync(() =>
                _conversationClient.SendCardAsync(conversationId, cardJson));

            _logger.LogDebug("Adaptive card message sent to conversation {ConversationId}", conversationId);
        }

        private AsyncPolicy BuildResiliencePolicy(int requestId)
        {
            AsyncTimeoutPolicy timeoutPolicy = Policy
                .TimeoutAsync(TimeSpan.FromSeconds(10), TimeoutStrategy.Optimistic);

            AsyncRetryPolicy retryPolicy = Policy
                .Handle<Exception>(ex => ex is not ArgumentException and not TimeoutRejectedException && !FatalExceptions.Is(ex))
                .WaitAndRetryAsync(
                    3,
                    retryAttempt => RetryDelayProvider(retryAttempt),
                    onRetry: (ex, ts, attempt, _) =>
                    {
                        _logger.LogWarning(ex,
                            "Retry {Attempt} sending Teams notification for request {RequestId}. Waiting {Delay}.",
                            attempt, requestId, ts);
                    });

            return Policy.WrapAsync(retryPolicy, timeoutPolicy);
        }

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
