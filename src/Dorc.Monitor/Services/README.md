# Notification Services

This directory contains notification service implementations for sending job completion notifications to users.

## Architecture

The notification system is designed to be messaging-platform agnostic, allowing multiple notification providers to be plugged in without changing core application code.

### Key Interfaces

- **`IUserNotificationService`** - Generic interface for any notification provider
- **`IJobNotificationService`** - Interface used by the application (wraps composite service)

### Components

1. **`CompositeNotificationService`** - Routes notifications to all registered providers
2. **`TeamsNotificationService`** - Microsoft Teams implementation using Graph API

## Adding a New Notification Provider

To add support for a new messaging system (e.g., Slack, Email, SMS):

### 1. Create Provider Implementation

Create a new class implementing `IUserNotificationService`:

```csharp
using Dorc.Core.Interfaces;
using Dorc.Core.Notifications;

namespace Dorc.Monitor.Services
{
    public class SlackNotificationService : IUserNotificationService
    {
        public string ProviderName => "Slack";
        
        public bool IsConfigured { get; }
        
        public SlackNotificationService(ILog logger, IMonitorConfiguration configuration)
        {
            // Initialize your provider
            // Check configuration and set IsConfigured accordingly
        }
        
        public async Task SendJobCompletionNotificationAsync(JobCompletionNotification notification)
        {
            // Implement your notification logic here
            // notification.UserIdentifier could be email, username, user ID, etc.
        }
    }
}
```

### 2. Add Configuration

Update `IMonitorConfiguration` and `MonitorConfiguration` to include your provider's settings:

```csharp
// In IMonitorConfiguration.cs
string? SlackWebhookUrl { get; }
string? SlackBotToken { get; }

// In MonitorConfiguration.cs
public string? SlackWebhookUrl => 
    configurationRoot.GetSection(appSettings)["Slack:WebhookUrl"];
```

### 3. Register in DI Container

Register your service in `Program.cs`:

```csharp
// Register your notification service
builder.Services.AddSingleton<IUserNotificationService, SlackNotificationService>();
```

The `CompositeNotificationService` will automatically pick up all registered `IUserNotificationService` implementations.

### 4. Add Configuration to appsettings.json

```json
"AppSettings": {
  "Slack": {
    "WebhookUrl": "",
    "BotToken": ""
  }
}
```

## Current Implementations

### Microsoft Teams (TeamsNotificationService)

**Configuration Required:**
- `Teams:TenantId` - Azure AD tenant ID
- `Teams:ClientId` - Azure AD application ID
- `Teams:ClientSecret` - Azure AD application secret

**Permissions Required:**
- `Chat.Create` - Create one-on-one chats with users
- `Chat.ReadWrite.All` - Read existing chats and send messages (required to find existing chats and post messages)
- `User.Read.All` - Look up users by email

**Behavior:**
- Only sends to users with valid email addresses
- Creates or finds existing one-on-one chat with user
- Sends HTML-formatted message with deployment details
- Logs all successes and failures

## User Identifier Validation

Each notification provider should validate that the `UserIdentifier` in the notification is appropriate for their system:

- **Teams**: Requires valid email address (uses `EmailValidator.IsValidEmail()`)
- **Slack**: Might accept Slack user ID or email
- **Email**: Requires valid email address
- **SMS**: Requires valid phone number

If the identifier is invalid for your provider, log a debug message and return without throwing an exception.

## Error Handling

- Providers should catch and log their own errors
- The `CompositeNotificationService` ensures one provider's failure doesn't affect others
- Re-throw exceptions only if you want the composite service to log them
- Use appropriate log levels:
  - `Debug`: Configuration not set, invalid user identifier
  - `Info`: Successful notification sent
  - `Warn`: User not found, recoverable errors
  - `Error`: Configuration errors, API failures

## Testing

When implementing a new provider, consider:

1. **Unit tests** for validation logic
2. **Integration tests** with mock APIs
3. **Configuration validation** (what happens if settings are missing?)
4. **User lookup failures** (what if user doesn't exist?)
5. **API rate limits** (how to handle throttling?)
6. **Async behavior** (notifications shouldn't block deployments)
