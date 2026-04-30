using System.Net;
using System.Text;
using System.Text.Json;
using Dorc.Api.Interfaces;
using Dorc.PersistentData.Sources.Interfaces;

namespace Dorc.Api.Services
{
    public class EmailNotificationService : IEmailNotificationService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<EmailNotificationService> _logger;
        private readonly IConfigValuesPersistentSource _configValuesPersistentSource;
        private readonly IAzureAdTokenService _tokenService;

        private const string GraphScope = "https://graph.microsoft.com/.default";

        public EmailNotificationService(
            HttpClient httpClient,
            ILogger<EmailNotificationService> logger,
            IConfigValuesPersistentSource configValuesPersistentSource,
            IAzureAdTokenService tokenService)
        {
            _httpClient = httpClient;
            _logger = logger;
            _configValuesPersistentSource = configValuesPersistentSource;
            _tokenService = tokenService;
        }

        private string GetFromAddress() => _configValuesPersistentSource.GetConfigValue("Email_FromAddress", string.Empty);
        private string GetFromName() => _configValuesPersistentSource.GetConfigValue("Email_FromName", "DOrc Deployment System");
        private static string SanitizeForLog(string? value) =>
            (value ?? string.Empty)
                .Replace("\r", string.Empty)
                .Replace("\n", string.Empty);

        public async Task SendCrOverrideNotificationAsync(
            string username,
            string environment,
            string project,
            string buildNumber,
            string notificationEmail)
        {
            if (string.IsNullOrEmpty(notificationEmail))
            {
                _logger.LogInformation("No notification email configured for project '{Project}'. Skipping CR override notification.", SanitizeForLog(project));
                return;
            }

            var fromAddress = GetFromAddress();
            if (string.IsNullOrEmpty(fromAddress))
            {
                _logger.LogWarning("Email_FromAddress not configured. Cannot send CR override notification.");
                return;
            }

            try
            {
                var token = await _tokenService.GetTokenAsync(GraphScope);
                if (string.IsNullOrEmpty(token))
                {
                    _logger.LogWarning("Cannot send CR override notification: failed to acquire Graph API token.");
                    return;
                }

                var subject = $"[DOrc Alert] Production Deployment CR Override - {environment}";
                var body = CreateCrOverrideEmailBody(username, environment, project, buildNumber);

                // Build recipients list
                var recipientList = notificationEmail
                    .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(r => r.Trim())
                    .Where(r => !string.IsNullOrEmpty(r))
                    .ToList();

                if (recipientList.Count == 0)
                {
                    _logger.LogWarning("No valid recipients for CR override notification.");
                    return;
                }

                var toRecipients = recipientList.Select(r => new
                {
                    emailAddress = new { address = r }
                }).ToArray();

                var graphPayload = new
                {
                    message = new
                    {
                        subject,
                        body = new
                        {
                            contentType = "HTML",
                            content = body
                        },
                        from = new
                        {
                            emailAddress = new
                            {
                                address = fromAddress,
                                name = GetFromName()
                            }
                        },
                        toRecipients
                    },
                    saveToSentItems = false
                };

                var json = JsonSerializer.Serialize(graphPayload);
                var request = new HttpRequestMessage(HttpMethod.Post,
                    $"https://graph.microsoft.com/v1.0/users/{fromAddress}/sendMail");
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    var responseBody = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Graph API sendMail failed. Status: {Status}, Response: {Response}",
                        response.StatusCode, responseBody.Length > 500 ? responseBody[..500] : responseBody);
                    return;
                }

                _logger.LogInformation(
                    "CR override notification sent via Graph API to {RecipientCount} recipients",
                    recipientList.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to send CR override notification via Graph API");
                // Don't throw - email failure shouldn't block deployment
            }
        }

        private static string CreateCrOverrideEmailBody(
            string username,
            string environment,
            string project,
            string buildNumber)
        {
            return $@"
<!DOCTYPE html>
<html>
<head>
    <style>
        body {{ font-family: Arial, sans-serif; }}
        .alert-box {{ 
            background-color: #fff3cd; 
            border: 1px solid #ffc107; 
            border-radius: 4px; 
            padding: 15px; 
            margin: 10px 0; 
        }}
        .details {{ 
            background-color: #f8f9fa; 
            border: 1px solid #dee2e6; 
            border-radius: 4px; 
            padding: 15px; 
            margin: 10px 0; 
        }}
        .label {{ font-weight: bold; color: #495057; }}
        .value {{ color: #212529; }}
    </style>
</head>
<body>
    <h2>⚠️ Production Deployment CR Override Alert</h2>
    
    <div class='alert-box'>
        <p>A production deployment was initiated <strong>without a valid Change Request</strong>.</p>
        <p>The user has acknowledged this by checking the 'Override CR' checkbox.</p>
    </div>
    
    <div class='details'>
        <h3>Deployment Details</h3>
        <p><span class='label'>User:</span> <span class='value'>{WebUtility.HtmlEncode(username)}</span></p>
        <p><span class='label'>Environment:</span> <span class='value'>{WebUtility.HtmlEncode(environment)}</span></p>
        <p><span class='label'>Project:</span> <span class='value'>{WebUtility.HtmlEncode(project)}</span></p>
        <p><span class='label'>Build Number:</span> <span class='value'>{WebUtility.HtmlEncode(buildNumber)}</span></p>
        <p><span class='label'>Timestamp:</span> <span class='value'>{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</span></p>
    </div>
    
    <p>Please review this deployment and follow up with the user if necessary.</p>
    
    <hr>
    <p style='color: #6c757d; font-size: 12px;'>
        This is an automated notification from the DOrc Deployment Orchestrator.
    </p>
</body>
</html>";
        }
    }
}
