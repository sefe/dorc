using Azure.Core;
using Azure.Identity;
using Dorc.Core;
using Dorc.Core.Interfaces;
using log4net;
using Microsoft.Graph;
using Microsoft.Graph.Models;

using Dorc.Core.Notifications;

namespace Dorc.Monitor.Services
{
    /// <summary>
    /// Microsoft Teams implementation of user notification service using Graph API
    /// Sends direct messages to users via Teams
    /// </summary>
    public class TeamsNotificationService : IUserNotificationService
    {
        private readonly ILog _logger;
        private readonly GraphServiceClient? _graphClient;

        public string ProviderName => "Microsoft Teams";

        public bool IsConfigured { get; }

        public TeamsNotificationService(ILog logger, IMonitorConfiguration configuration)
        {
            _logger = logger;
            
            var tenantId = configuration.TeamsTenantId;
            var clientId = configuration.TeamsClientId;
            var clientSecret = configuration.TeamsClientSecret;

            if (!string.IsNullOrWhiteSpace(tenantId) && 
                !string.IsNullOrWhiteSpace(clientId) && 
                !string.IsNullOrWhiteSpace(clientSecret))
            {
                try
                {
                    var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
                    _graphClient = new GraphServiceClient(credential);
                    IsConfigured = true;
                    _logger.Info("Teams notification service configured successfully with Graph API");
                }
                catch (Exception ex)
                {
                    _logger.Error("Failed to initialize Teams Graph API client", ex);
                    IsConfigured = false;
                }
            }
            else
            {
                _logger.Debug("Teams notification not configured - missing tenant ID, client ID, or client secret");
                IsConfigured = false;
            }
        }

        public async Task SendJobCompletionNotificationAsync(JobCompletionNotification notification)
        {
            // Only notify if userName is a valid email address
            if (!EmailValidator.IsValidEmail(notification.UserIdentifier))
            {
                _logger.Debug($"Skipping Teams notification for request {notification.RequestId} - UserIdentifier '{notification.UserIdentifier}' is not a valid email address");
                return;
            }

            // Check if Teams notification is configured
            if (!IsConfigured || _graphClient == null)
            {
                _logger.Debug("Teams notification not configured - skipping notification");
                return;
            }

            try
            {
                // Get the user's ID from their email address
                var user = await _graphClient.Users[notification.UserIdentifier].GetAsync();
                
                if (user?.Id == null)
                {
                    _logger.Warn($"Could not find user with email {notification.UserIdentifier} in Teams");
                    return;
                }

                // Create a chat message
                var chatMessage = CreateChatMessage(notification);
                
                // Send message directly to the user via one-on-one chat
                await SendDirectMessageAsync(user.Id, chatMessage);

                _logger.Info($"Successfully sent Teams notification for request {notification.RequestId} to user {notification.UserIdentifier}");
            }
            catch (Exception ex)
            {
                _logger.Error($"Error sending Teams notification for request {notification.RequestId} to user {notification.UserIdentifier}", ex);
                throw; // Re-throw to allow composite service to handle
            }
        }

        private async Task SendDirectMessageAsync(string userId, string message)
        {
            if (_graphClient == null)
                return;

            try
            {
                // Create a new one-on-one chat or get existing chat with the user
                var chatMessage = new ChatMessage
                {
                    Body = new ItemBody
                    {
                        ContentType = BodyType.Html,
                        Content = message
                    }
                };

                // First, try to find or create a chat with the user
                var chats = await _graphClient.Chats.GetAsync(requestConfiguration =>
                {
                    requestConfiguration.QueryParameters.Filter = $"chatType eq 'oneOnOne'";
                });

                string? chatId = null;
                
                if (chats?.Value != null)
                {
                    foreach (var chat in chats.Value)
                    {
                        if (chat.Id == null) continue;
                        
                        var members = await _graphClient.Chats[chat.Id].Members.GetAsync();
                        if (members?.Value != null && members.Value.Any(m => 
                            m is AadUserConversationMember aadMember && aadMember.UserId == userId))
                        {
                            chatId = chat.Id;
                            break;
                        }
                    }
                }

                // If no existing chat found, create a new one
                if (string.IsNullOrEmpty(chatId))
                {
                    var newChat = new Chat
                    {
                        ChatType = ChatType.OneOnOne,
                        Members = new List<ConversationMember>
                        {
                            new AadUserConversationMember
                            {
                                Roles = new List<string> { "owner" },
                                AdditionalData = new Dictionary<string, object>
                                {
                                    { "user@odata.bind", $"https://graph.microsoft.com/v1.0/users('{userId}')" }
                                }
                            }
                        }
                    };

                    var createdChat = await _graphClient.Chats.PostAsync(newChat);
                    chatId = createdChat?.Id;
                }

                if (!string.IsNullOrEmpty(chatId))
                {
                    await _graphClient.Chats[chatId].Messages.PostAsync(chatMessage);
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Error creating or sending message to user {userId}", ex);
                throw;
            }
        }

        private string CreateChatMessage(JobCompletionNotification notification)
        {
            var emoji = notification.Status switch
            {
                "Completed" => "✅",
                "Failed" => "❌",
                "Errored" => "⚠️",
                _ => "ℹ️"
            };

            return $@"
<h2>{emoji} Deployment {notification.Status}</h2>
<p><strong>Request ID:</strong> {notification.RequestId}</p>
<p><strong>Status:</strong> {notification.Status}</p>
<p><strong>Environment:</strong> {notification.Environment ?? "N/A"}</p>
<p><strong>Project:</strong> {notification.Project ?? "N/A"}</p>
<p><strong>Build:</strong> {notification.BuildNumber ?? "N/A"}</p>";
        }
    }
}
