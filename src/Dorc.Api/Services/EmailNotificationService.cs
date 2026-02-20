using System.Net;
using System.Net.Mail;
using Dorc.Api.Interfaces;
using Dorc.PersistentData.Sources.Interfaces;

namespace Dorc.Api.Services
{
    /// <summary>
    /// Email notification service that uses DOrc configuration.
    /// 
    /// Required DOrc Config Values (in Config table):
    /// - Email_SmtpHost: SMTP server hostname
    /// - Email_SmtpPort: SMTP server port (default: 25)
    /// - Email_FromAddress: Sender email address
    /// - Email_FromName: Sender display name (default: "DOrc Deployment System")
    /// - App support: Email recipient(s) for CR override notifications
    /// </summary>
    public class EmailNotificationService : IEmailNotificationService
    {
        private readonly ILogger<EmailNotificationService> _logger;
        private readonly IConfigValuesPersistentSource _configValuesPersistentSource;

        // DOrc Config Value Keys
        private const string ConfigKey_SmtpHost = "Email_SmtpHost";
        private const string ConfigKey_SmtpPort = "Email_SmtpPort";
        private const string ConfigKey_FromAddress = "Email_FromAddress";
        private const string ConfigKey_FromName = "Email_FromName";
        private const string ConfigKey_AppSupport = "App support";

        public EmailNotificationService(
            ILogger<EmailNotificationService> logger,
            IConfigValuesPersistentSource configValuesPersistentSource)
        {
            _logger = logger;
            _configValuesPersistentSource = configValuesPersistentSource;
        }

        private string GetSmtpHost() => _configValuesPersistentSource.GetConfigValue(ConfigKey_SmtpHost, string.Empty);
        private int GetSmtpPort() => int.TryParse(_configValuesPersistentSource.GetConfigValue(ConfigKey_SmtpPort, "25"), out var port) ? port : 25;
        private string GetFromAddress() => _configValuesPersistentSource.GetConfigValue(ConfigKey_FromAddress, string.Empty);
        private string GetFromName() => _configValuesPersistentSource.GetConfigValue(ConfigKey_FromName, "DOrc Deployment System");
        private string GetAppSupportEmail() => _configValuesPersistentSource.GetConfigValue(ConfigKey_AppSupport, string.Empty);

        public async Task SendCrOverrideNotificationAsync(
            string username,
            string environment,
            string project,
            string buildNumber)
        {
            var smtpHost = GetSmtpHost();
            if (string.IsNullOrEmpty(smtpHost))
            {
                _logger.LogWarning("Email_SmtpHost not configured. Cannot send CR override notification.");
                return;
            }

            var recipients = GetAppSupportEmail();
            if (string.IsNullOrEmpty(recipients))
            {
                _logger.LogWarning("'App support' config value not configured. Cannot send CR override notification.");
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
                var subject = $"[DOrc Alert] Production Deployment CR Override - {environment}";
                var body = CreateCrOverrideEmailBody(username, environment, project, buildNumber);

                using var smtpClient = new SmtpClient(smtpHost, GetSmtpPort());

                var mailMessage = new MailMessage
                {
                    From = new MailAddress(fromAddress, GetFromName()),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = true
                };

                // Add all recipients (App support may contain multiple emails)
                var recipientList = recipients
                    .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(r => r.Trim())
                    .Where(r => !string.IsNullOrEmpty(r));

                foreach (var recipient in recipientList)
                {
                    mailMessage.To.Add(recipient);
                }

                if (mailMessage.To.Count == 0)
                {
                    _logger.LogWarning("No valid recipients for CR override notification.");
                    return;
                }

                await smtpClient.SendMailAsync(mailMessage);

                _logger.LogInformation(
                    "CR override notification sent to {RecipientCount} recipients for deployment to {Environment} by {Username}",
                    mailMessage.To.Count, environment, username);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to send CR override notification for deployment to {Environment} by {Username}",
                    environment, username);
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
