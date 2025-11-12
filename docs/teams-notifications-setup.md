# MS Teams Direct User Notifications Setup Guide

This guide explains how to configure DOrc to send job completion notifications directly to users via Microsoft Teams.

## Overview

When a deployment job completes, DOrc can send a direct message to the user who initiated the deployment. The notification includes:
- Request ID
- Deployment status (Completed ✅, Failed ❌, or Errored ⚠️)
- Environment name
- Project name
- Build number

**Important**: Notifications are only sent if the user's username is a valid email address.

## User Control

**Automatic Opt-in**: Users with valid email addresses automatically receive notifications.

**How to Opt-out**: Currently, notifications are automatically sent to all users whose username is a valid email address. If the Teams service is not configured (TenantId, ClientId, or ClientSecret are empty), no notifications will be sent to anyone.

**Note**: Future enhancements may include per-user notification preferences to allow individual users to opt-in or opt-out of Teams notifications.

## Prerequisites

1. Azure AD tenant with Microsoft Teams enabled
2. Admin access to create Azure AD app registrations
3. Access to the DOrc Monitor service configuration

## Azure AD App Registration

### Step 1: Create App Registration

1. Go to [Azure Portal](https://portal.azure.com)
2. Navigate to **Azure Active Directory** > **App registrations**
3. Click **New registration**
4. Configure:
   - **Name**: `DOrc Teams Notifications`
   - **Supported account types**: `Accounts in this organizational directory only`
   - **Redirect URI**: Leave empty
5. Click **Register**

### Step 2: Note Application IDs

After registration, note these values (you'll need them for configuration):
- **Application (client) ID**
- **Directory (tenant) ID**

### Step 3: Create Client Secret

1. In your app registration, go to **Certificates & secrets**
2. Click **New client secret**
3. Configure:
   - **Description**: `DOrc Notifications`
   - **Expires**: Choose appropriate duration (e.g., 24 months)
4. Click **Add**
5. **Important**: Copy the **Value** immediately - you won't be able to see it again

### Step 4: Configure API Permissions

1. Go to **API permissions**
2. Click **Add a permission**
3. Select **Microsoft Graph** > **Application permissions**
4. Add these permissions:
   - `Chat.Create` - Create one-on-one chats with users
   - `Chat.ReadWrite.All` - Read existing chats and send messages (required to find existing chats and post messages)
   - `User.Read.All` - Look up users by email address
5. Click **Add permissions**
6. Click **Grant admin consent for [Your Organization]**
7. Confirm the consent

**Note**: `Chat.ReadWrite.All` is required (not just `Chat.Create`) because the service needs to:
- Read existing one-on-one chats to avoid creating duplicates
- Send messages to those chats

## DOrc Configuration

Update the Monitor service's `appsettings.json`:

```json
{
  "AppSettings": {
    "Teams": {
      "TenantId": "your-tenant-id-here",
      "ClientId": "your-client-id-here",
      "ClientSecret": "your-client-secret-here"
    }
  }
}
```

Replace the placeholder values with the ones you noted during app registration.

## Verification

After configuration:

1. Restart the DOrc Monitor service
2. Check the logs for: `"Teams notification service configured successfully with Graph API"`
3. Trigger a test deployment with a user account that has a valid email address
4. The user should receive a direct Teams message when the deployment completes

## Troubleshooting

### No notifications received

**Check 1: Is the service configured?**
- Look for log message: `"Teams notification service configured successfully with Graph API"`
- If you see `"Teams notification not configured"`, verify your configuration values

**Check 2: Is the username a valid email?**
- Look for log message: `"Skipping Teams notification for request X - UserName 'Y' is not a valid email address"`
- The notification system only sends to valid email addresses

**Check 3: Can the user be found in Teams?**
- Look for log message: `"Could not find user with email X in Teams"`
- Verify the user exists in Azure AD and has Teams enabled

**Check 4: API permissions granted?**
- Verify admin consent was granted for all required permissions
- Check Azure AD audit logs for permission-related errors

### Common Errors

**"Failed to initialize Teams Graph API client"**
- Verify Tenant ID, Client ID, and Client Secret are correct
- Check that the secret hasn't expired

**"Unauthorized" errors**
- Ensure admin consent was granted for the API permissions
- Verify the service principal has the correct permissions

**"User not found"**
- The email address doesn't match any user in your Azure AD tenant
- User might not have Teams enabled

## Security Considerations

1. **Store secrets securely**: Consider using Azure Key Vault or another secrets management system instead of plain text in configuration files
2. **Least privilege**: The app only requests permissions it needs for direct messaging
3. **Audit logs**: Monitor Azure AD sign-in logs for the service principal's activity
4. **Secret rotation**: Set up a process to rotate the client secret before it expires

## Adding Additional Notification Channels

The notification system is designed to support multiple messaging platforms. To add Slack, Email, or other channels, see `/src/Dorc.Monitor/Services/README.md` for developer documentation.

## Architecture Notes

- **Platform agnostic**: The system uses a generic `IUserNotificationService` interface
- **Composite pattern**: Multiple notification providers can run simultaneously
- **Fault tolerant**: One provider's failure doesn't affect others
- **Async**: Notifications don't block deployment processing
- **Validated**: Only sends to users with valid email addresses
