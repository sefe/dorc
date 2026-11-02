using Microsoft.Bot.Connector;
using Microsoft.Bot.Connector.Authentication;
using Microsoft.Bot.Schema;
using Microsoft.Bot.Schema.Teams;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace Dorc.Monitor.Notifications.Teams
{
    internal sealed class TeamsConversationClient : ITeamsConversationClient, IDisposable
    {
        private readonly TeamsBotOptions _options;
        private readonly Lazy<ConnectorClient> _connectorClient;

        public TeamsConversationClient(IOptions<TeamsBotOptions> options)
        {
            _options = options.Value;
            _connectorClient = new Lazy<ConnectorClient>(
                () => new ConnectorClient(
                    new Uri(_options.ServiceUrl),
                    new MicrosoftAppCredentials(_options.BotAppId, _options.BotAppPassword, _options.TenantId)),
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public async Task<string> CreateConversationAsync(string aadObjectId, CancellationToken cancellationToken)
        {
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

            var conversationResource = await _connectorClient.Value.Conversations
                .CreateConversationAsync(conversationParameters, cancellationToken);

            if (string.IsNullOrWhiteSpace(conversationResource?.Id))
            {
                throw new InvalidOperationException(
                    $"Bot Connector returned no conversation id for AAD object id '{aadObjectId}'.");
            }

            return conversationResource.Id;
        }

        public async Task SendCardAsync(string conversationId, string cardJson, CancellationToken cancellationToken)
        {
            var message = new Activity
            {
                Type         = ActivityTypes.Message,
                From         = new ChannelAccount { Id = _options.BotAppId },
                Conversation = new ConversationAccount { Id = conversationId },
                Attachments  = new List<Attachment>
                {
                    new Attachment
                    {
                        ContentType = "application/vnd.microsoft.card.adaptive",
                        Content     = JObject.Parse(cardJson)
                    }
                }
            };

            await _connectorClient.Value.Conversations
                .SendToConversationAsync(conversationId, message, cancellationToken);
        }

        public void Dispose()
        {
            if (_connectorClient.IsValueCreated)
            {
                _connectorClient.Value.Dispose();
            }
        }
    }
}
