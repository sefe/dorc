using Dorc.Core;
using Dorc.Core.Interfaces;
using log4net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Dorc.Monitor.Services
{
    /// <summary>
    /// Service for sending job completion notifications to Microsoft Teams
    /// </summary>
    public class TeamsNotificationService : ITeamsNotificationService
    {
        private readonly ILog _logger;
        private readonly HttpClient _httpClient;
        private readonly string? _teamsWebhookUrl;

        public TeamsNotificationService(ILog logger, IMonitorConfiguration configuration)
        {
            _logger = logger;
            _httpClient = new HttpClient();
            _teamsWebhookUrl = configuration.TeamsWebhookUrl;
        }

        public async Task NotifyJobCompletionAsync(
            string userName,
            int requestId,
            string status,
            string environment,
            string project,
            string buildNumber)
        {
            // Only notify if userName is a valid email address
            if (!EmailValidator.IsValidEmail(userName))
            {
                _logger.Debug($"Skipping Teams notification for request {requestId} - UserName '{userName}' is not a valid email address");
                return;
            }

            // Check if Teams webhook URL is configured
            if (string.IsNullOrWhiteSpace(_teamsWebhookUrl))
            {
                _logger.Debug("Teams webhook URL not configured - skipping notification");
                return;
            }

            try
            {
                var message = CreateAdaptiveCardMessage(userName, requestId, status, environment, project, buildNumber);
                var response = await _httpClient.PostAsJsonAsync(_teamsWebhookUrl, message);

                if (response.IsSuccessStatusCode)
                {
                    _logger.Info($"Successfully sent Teams notification for request {requestId} to user {userName}");
                }
                else
                {
                    var responseBody = await response.Content.ReadAsStringAsync();
                    _logger.Warn($"Failed to send Teams notification for request {requestId}. Status: {response.StatusCode}, Response: {responseBody}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Error sending Teams notification for request {requestId} to user {userName}", ex);
            }
        }

        private object CreateAdaptiveCardMessage(
            string userName,
            int requestId,
            string status,
            string environment,
            string project,
            string buildNumber)
        {
            var color = status switch
            {
                "Completed" => "good",
                "Failed" => "attention",
                "Errored" => "attention",
                _ => "default"
            };

            var emoji = status switch
            {
                "Completed" => "✅",
                "Failed" => "❌",
                "Errored" => "⚠️",
                _ => "ℹ️"
            };

            return new
            {
                type = "message",
                attachments = new[]
                {
                    new
                    {
                        contentType = "application/vnd.microsoft.card.adaptive",
                        content = new
                        {
                            type = "AdaptiveCard",
                            version = "1.4",
                            body = new object[]
                            {
                                new
                                {
                                    type = "TextBlock",
                                    text = $"{emoji} Deployment {status}",
                                    size = "Large",
                                    weight = "Bolder"
                                },
                                new
                                {
                                    type = "FactSet",
                                    facts = new[]
                                    {
                                        new { title = "Request ID", value = requestId.ToString() },
                                        new { title = "Status", value = status },
                                        new { title = "Environment", value = environment ?? "N/A" },
                                        new { title = "Project", value = project ?? "N/A" },
                                        new { title = "Build", value = buildNumber ?? "N/A" },
                                        new { title = "Requested By", value = userName }
                                    }
                                }
                            }
                        }
                    }
                }
            };
        }
    }
}
