using Dorc.ApiModel;
using Dorc.Core;
using Dorc.Core.Configuration;
using Microsoft.Bot.Connector;
using Microsoft.Bot.Connector.Authentication;
using Microsoft.Bot.Schema;
using Microsoft.Bot.Schema.Teams;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Polly;
using Polly.Retry;
using Polly.Timeout;

namespace Dorc.Monitor.Notifications.Teams
{
    internal sealed class TeamsBotNotificationSink : IDeploymentNotificationSink
    {
        private readonly TeamsBotOptions _options;
        private readonly IConfigurationSettings _configurationSettings;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<TeamsBotNotificationSink> _logger;
        private readonly DeploymentCompletionCardBuilder _cardBuilder;

        public TeamsBotNotificationSink(
            IOptions<TeamsBotOptions> options,
            IConfigurationSettings configurationSettings,
            DeploymentCompletionCardBuilder cardBuilder,
            ILoggerFactory loggerFactory)
        {
            _options               = options.Value;
            _configurationSettings = configurationSettings;
            _cardBuilder           = cardBuilder;
            _loggerFactory         = loggerFactory;
            _logger                = loggerFactory.CreateLogger<TeamsBotNotificationSink>();
        }

        public async Task NotifyRequestCompletedAsync(
            DeploymentRequestApiModel request,
            string finalStatus,
            DateTimeOffset startedTime,
            DateTimeOffset completedTime)
        {
            if (!_options.Enabled)
                return;

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
            catch (Exception ex)
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
                var entraLogger = _loggerFactory.CreateLogger<AzureEntraSearcher>();
                var searcher    = new AzureEntraSearcher(_configurationSettings, entraLogger);

                var results = searcher.Search(userName);
                var match = results.FirstOrDefault(u =>
                    !u.IsGroup &&
                    (string.Equals(u.Username,    userName, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(u.Email,       userName, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(u.DisplayName, userName, StringComparison.OrdinalIgnoreCase)));

                return match?.Pid;
            }
            catch (Exception ex)
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
            MicrosoftAppCredentials.TrustServiceUrl(_options.ServiceUrl);

            var credentials = new MicrosoftAppCredentials(
                _options.BotAppId,
                _options.BotAppPassword,
                _options.TenantId);

            using var connectorClient = new ConnectorClient(new Uri(_options.ServiceUrl), credentials);

            var conversationParameters = new ConversationParameters
            {
                IsGroup = false,
                Bot     = new ChannelAccount { Id = _options.BotAppId },
                Members = new[]
                {
                    new ChannelAccount
                    {
                        Id          = aadObjectId,
                        AadObjectId = aadObjectId
                    }
                },
                TenantId    = _options.TenantId,
                ChannelData = new TeamsChannelData
                {
                    Tenant = new TenantInfo { Id = _options.TenantId }
                }
            };

            AsyncTimeoutPolicy timeoutPolicy = Policy
                .TimeoutAsync(TimeSpan.FromSeconds(10), TimeoutStrategy.Optimistic);

            AsyncRetryPolicy retryPolicy = Policy
                .Handle<Exception>(ex => ex is not ArgumentException and not TimeoutRejectedException)
                .WaitAndRetryAsync(
                    3,
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    onRetry: (ex, ts, attempt, _) =>
                    {
                        _logger.LogWarning(ex,
                            "Retry {Attempt} sending Teams notification for request {RequestId}. Waiting {Delay}.",
                            attempt, request.Id, ts);
                    });

            var policy = Policy.WrapAsync(retryPolicy, timeoutPolicy);

            var conversationResource = await policy.ExecuteAsync(async () =>
                await connectorClient.Conversations.CreateConversationAsync(conversationParameters));

            _logger.LogDebug("Created conversation {ConversationId} for user {User} (request {RequestId})",
                conversationResource?.Id, request.UserName, request.Id);

            var cardJson = _cardBuilder.Build(request, finalStatus, startedTime, completedTime);
            _logger.LogDebug("Adaptive Card JSON:\n{CardJson}", cardJson);

            var message = new Activity
            {
                Type         = ActivityTypes.Message,
                From         = new ChannelAccount { Id = _options.BotAppId },
                Conversation = new ConversationAccount { Id = conversationResource.Id },
                Attachments  = new List<Attachment>
                {
                    new Attachment
                    {
                        ContentType = "application/vnd.microsoft.card.adaptive",
                        Content     = JObject.Parse(cardJson)
                    }
                }
            };

            await policy.ExecuteAsync(async () =>
                await connectorClient.Conversations.SendToConversationAsync(conversationResource.Id, message));

            _logger.LogDebug("Adaptive card message sent to conversation {ConversationId}", conversationResource.Id);
        }
    }
}