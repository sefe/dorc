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
            catch (Microsoft.Graph.Models.ODataErrors.ODataError odataEx)
            {
                // Handle Graph API specific errors with detailed diagnostics
                var errorCode = odataEx.Error?.Code;
                var errorMessage = odataEx.Error?.Message ?? string.Empty;
                var innerError = odataEx.Error?.InnerError;
                
                // Log diagnostic information for troubleshooting
                _logger.Error($"Graph API ODataError for request {notification.RequestId}:");
                _logger.Error($"  Error Code: {errorCode}");
                _logger.Error($"  Error Message: {errorMessage}");
                _logger.Error($"  User: {notification.UserIdentifier}");
                
                if (innerError != null)
                {
                    if (innerError.AdditionalData?.ContainsKey("code") == true)
                    {
                        _logger.Error($"  Inner Error Code: {innerError.AdditionalData["code"]}");
                    }
                    if (innerError.AdditionalData?.ContainsKey("request-id") == true)
                    {
                        _logger.Error($"  Request ID: {innerError.AdditionalData["request-id"]}");
                    }
                    if (innerError.AdditionalData?.ContainsKey("date") == true)
                    {
                        _logger.Error($"  Date: {innerError.AdditionalData["date"]}");
                    }
                }
                
                // Provide specific guidance based on error type
                if (errorCode == "InteractionRequired" || errorMessage.Contains("Continuous access evaluation"))
                {
                    _logger.Error($"TEAMS NOTIFICATION BLOCKED BY CONDITIONAL ACCESS POLICY:");
                    _logger.Error($"  This service principal is being blocked by Continuous Access Evaluation (CAE) policies.");
                    _logger.Error($"  Common causes:");
                    _logger.Error($"    - Location-based Conditional Access policies requiring authentication from specific IP addresses");
                    _logger.Error($"    - The service is authenticating from an IP not in the Trusted Named Locations");
                    _logger.Error($"    - Device compliance or risk-based policies affecting the service principal");
                    _logger.Error($"  Recommended solutions:");
                    _logger.Error($"    1. Add this service's IP address to Entra Conditional Access Trusted Named Locations");
                    _logger.Error($"    2. Create a Conditional Access policy exclusion for this service principal");
                    _logger.Error($"    3. Ensure consistent egress IP addresses for Microsoft Graph API calls");
                    _logger.Error($"  Troubleshooting steps:");
                    _logger.Error($"    1. Check Entra Sign-in Logs > Service Principal Sign-ins for this application");
                    _logger.Error($"    2. Review Conditional Access > Policies to identify which policy is blocking access");
                    _logger.Error($"    3. Verify the service's current IP address is in a Trusted Named Location");
                    _logger.Error($"  See: https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-continuous-access-evaluation");
                }
                else if (errorCode == "Authorization_RequestDenied")
                {
                    _logger.Error($"TEAMS NOTIFICATION AUTHORIZATION DENIED:");
                    _logger.Error($"  The service principal does not have sufficient permissions.");
                    _logger.Error($"  Required Microsoft Graph API permissions:");
                    _logger.Error($"    - Chat.Create (Application permission)");
                    _logger.Error($"    - Chat.ReadWrite.All (Application permission)");
                    _logger.Error($"    - User.Read.All (Application permission)");
                    _logger.Error($"  Verify:");
                    _logger.Error($"    1. These permissions are added to the app registration");
                    _logger.Error($"    2. Admin consent has been granted for these permissions");
                    _logger.Error($"    3. The service principal has not been disabled or expired");
                }
                else if (errorCode == "InvalidAuthenticationToken")
                {
                    _logger.Error($"TEAMS NOTIFICATION AUTHENTICATION TOKEN ERROR:");
                    _logger.Error($"  The authentication token is invalid or expired.");
                    _logger.Error($"  Verify:");
                    _logger.Error($"    1. TenantId, ClientId, and ClientSecret are correctly configured");
                    _logger.Error($"    2. The client secret has not expired");
                    _logger.Error($"    3. The service principal is enabled in Azure AD");
                }
                else
                {
                    _logger.Error($"TEAMS NOTIFICATION ERROR:");
                    _logger.Error($"  An unexpected Graph API error occurred.");
                    _logger.Error($"  Review the error details above and check Microsoft Graph API status.");
                }
                
                _logger.Error($"Full exception:", odataEx);
                // Exception logged locally; composite service will handle via SendNotificationSafelyAsync
            }
            catch (Exception ex)
            {
                _logger.Error($"Error sending Teams notification for request {notification.RequestId} to user {notification.UserIdentifier}", ex);
                // Exception logged locally; composite service will handle via SendNotificationSafelyAsync
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
                // Use $expand to get members in a single request and pagination
                var chats = await _graphClient.Chats.GetAsync(requestConfiguration =>
                {
                    requestConfiguration.QueryParameters.Filter = $"chatType eq 'oneOnOne'";
                    requestConfiguration.QueryParameters.Expand = new[] { "members" };
                    requestConfiguration.QueryParameters.Top = 50; // Limit initial fetch
                });

                string? chatId = null;
                
                if (chats?.Value != null)
                {
                    // Use explicit filtering with .Where()
                    var matchingChat = chats.Value
                        .Where(chat => chat.Id != null && chat.Members != null)
                        .FirstOrDefault(chat => chat.Members!.Any(m => 
                            m is AadUserConversationMember aadMember && aadMember.UserId == userId));
                    
                    chatId = matchingChat?.Id;
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
