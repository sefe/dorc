# OAuth 2.0 Token Endpoint 400 Bad Request - Diagnostic Guide

## Error Details

```
[ERROR] Failed to refresh OAuth 2.0 credentials
System.Net.Http.HttpRequestException: Response status code does not indicate success: 400 (Bad Request).
  at RabbitMQ.Client.OAuth2.OAuth2Client.RequestTokenAsync(CancellationToken cancellationToken)
  at RabbitMQ.Client.OAuth2.OAuth2ClientCredentialsProvider.GetCredentialsAsync(CancellationToken cancellationToken)
  at RabbitMQ.Client.OAuth2.CredentialsRefresher.RefreshLoopAsync()
```

## Root Cause Analysis

A **400 Bad Request** error from the OAuth token endpoint means the server rejected the request. This is **NOT a network error** - the OAuth server received and rejected the request.

### Most Likely Causes (Ranked by Probability)

#### 1. ❌ **Invalid or Missing OAuth Credentials** (80% likelihood)
   
   **What's happening:**
   - `RabbitMqOAuthClientId` is empty, null, or incorrect
   - `RabbitMqOAuthClientSecret` is empty, null, or incorrect
   - OAuth server doesn't recognize the client
   
   **Configuration Path:**
   ```
   AppSettings:HighAvailability:RabbitMQ:OAuth:ClientId
   AppSettings:HighAvailability:RabbitMQ:OAuth:ClientSecret
   ```
   
   **Fix:**
   ```json
   {
     "AppSettings": {
       "HighAvailability": {
         "RabbitMQ": {
           "OAuth": {
             "ClientId": "your-actual-client-id",
             "ClientSecret": "your-actual-client-secret",
             "TokenEndpoint": "https://oauth-server/token"
           }
         }
       }
     }
   }
   ```

#### 2. ❌ **Invalid OAuth Scope** (10% likelihood)
   
   **What's happening:**
   - Scope parameter is malformed
   - Scope is not valid for this client
   - Scope has invalid characters or format
   
   **Configuration Path:**
   ```
   AppSettings:HighAvailability:RabbitMQ:OAuth:Scope
   ```
   
   **Common Issues:**
   - Scope contains spaces without URL encoding
   - Scope format doesn't match provider requirements
   - Scope wasn't registered for this client_id
   
   **Verification:**
   - Check OAuth provider documentation for correct scope format
   - Verify scope is registered for this client_id in OAuth provider

#### 3. ❌ **Wrong Token Endpoint URL** (5% likelihood)
   
   **What's happening:**
   - URL points to wrong OAuth server
   - URL has typo (e.g., `http://` instead of `https://`)
   - URL has wrong path for token endpoint
   
   **Configuration Path:**
   ```
   AppSettings:HighAvailability:RabbitMQ:OAuth:TokenEndpoint
   ```
   
   **Common Issues:**
   - Using `http://` instead of `https://`
   - Wrong hostname
   - Missing `/token` or similar endpoint path
   
   **Example Issues:**
   ```
   ❌ https://oauth.example.com/  (missing endpoint path)
   ✅ https://oauth.example.com/token
   
   ❌ http://oauth.example.com/token  (wrong protocol)
   ✅ https://oauth.example.com/token
   ```

#### 4. ❌ **OAuth Server Configuration Issue** (3% likelihood)
   
   **What's happening:**
   - Client not registered in OAuth provider
   - Client is disabled/revoked
   - Client doesn't have permission for requested scope
   - OAuth provider requires additional configuration
   
   **Verification:**
   - Check OAuth provider's admin console
   - Verify client_id is registered and active
   - Verify client has permissions for the requested scope
   - Check if OAuth provider has requirements (e.g., redirect URIs, origins)

#### 5. ⚠️ **TLS/SSL Certificate Issue** (2% likelihood)
   
   **What's happening:**
   - OAuth endpoint has self-signed certificate
   - Certificate chain validation failing
   - TLS version mismatch
   
   **Current Configuration:**
   ```
   AppSettings:HighAvailability:RabbitMQ:Ssl:Enabled = true/false
   AppSettings:HighAvailability:RabbitMQ:Ssl:Version = TLS12_TLS13
   ```
   
   **Note:** These settings apply to RabbitMQ connection, NOT the OAuth token endpoint
   
   **Workaround:** If OAuth endpoint uses self-signed cert:
   - Add custom certificate validation (code change needed)
   - Or use proper signed certificate on OAuth endpoint

---

## Diagnostic Steps (In Order)

### Step 1: Verify Configuration Is Loaded

**Add logging to see actual values:**

In `RabbitMqDistributedLockService.ConfigureOAuth2Async()`, add:

```csharp
logger.LogInformation("OAuth Configuration:");
logger.LogInformation("  ClientId: {ClientId}", 
    string.IsNullOrEmpty(configuration.RabbitMqOAuthClientId) ? "[EMPTY]" : "[CONFIGURED]");
logger.LogInformation("  ClientSecret: {Secret}", 
    string.IsNullOrEmpty(configuration.RabbitMqOAuthClientSecret) ? "[EMPTY]" : "[CONFIGURED]");
logger.LogInformation("  TokenEndpoint: {Endpoint}", configuration.RabbitMqOAuthTokenEndpoint);
logger.LogInformation("  Scope: {Scope}", 
    string.IsNullOrEmpty(configuration.RabbitMqOAuthScope) ? "[EMPTY]" : configuration.RabbitMqOAuthScope);
```

**Expected Output:**
```
[INFO] OAuth Configuration:
[INFO]   ClientId: [CONFIGURED]
[INFO]   ClientSecret: [CONFIGURED]
[INFO]   TokenEndpoint: https://oauth-server/token
[INFO]   Scope: rabbitmq.all
```

**If You See [EMPTY]:** Configuration is not being loaded correctly

---

### Step 2: Test OAuth Endpoint Manually

**Using PowerShell:**

```powershell
$clientId = "your-client-id"
$clientSecret = "your-client-secret"
$tokenEndpoint = "https://oauth-server/token"
$scope = "rabbitmq.all"  # or empty if not needed

$body = @{
    grant_type    = "client_credentials"
    client_id     = $clientId
    client_secret = $clientSecret
    scope         = $scope
}

$response = Invoke-WebRequest `
    -Method Post `
    -Uri $tokenEndpoint `
    -Body $body `
    -ContentType "application/x-www-form-urlencoded"

$response.StatusCode
$response.Content | ConvertFrom-Json
```

**Expected Response:**
```
StatusCode: 200
{
  "access_token": "eyJ0eXAiOiJKV1QiLCJhbGc...",
  "expires_in": 3600,
  "token_type": "Bearer"
}
```

**If You Get 400:** OAuth endpoint is rejecting the request
- Check error details in response body (may have more info)
- Verify ClientId and ClientSecret are correct
- Verify Scope is correct/valid
- Check OAuth provider logs

**If You Get 401:** Credentials are incorrect
- Verify ClientId and ClientSecret match OAuth provider records
- Check if client is enabled/active in OAuth provider

---

### Step 3: Verify Configuration File Format

Check `appsettings.json` structure:

```json
{
  "AppSettings": {
    "HighAvailability": {
      "Enabled": true,
      "RabbitMQ": {
        "HostName": "backboneut",
        "Port": 5671,
        "VirtualHost": "/",
        "OAuth": {
          "ClientId": "dorc-client",
          "ClientSecret": "super-secret-value",
          "TokenEndpoint": "https://oauth.example.com/token",
          "Scope": "rabbitmq.all"
        },
        "Ssl": {
          "Enabled": true,
          "ServerName": "backboneut",
          "Version": "TLS12_TLS13"
        }
      }
    }
  }
}
```

**Common Mistakes:**
- ❌ `OAuth.ClientId` instead of correct path
- ❌ Missing `AppSettings` parent
- ❌ Wrong `HighAvailability` nesting level
- ❌ Typos in property names

---

### Step 4: Check OAuth Provider Requirements

Contact your OAuth provider and verify:

1. **Client Registration:**
   - Client ID is registered
   - Client is active (not disabled/revoked)
   - Client secret is correct

2. **Scope Requirements:**
   - Scope format matches provider requirements
   - Scope is registered for this client
   - Client has permission for requested scope

3. **Token Endpoint:**
   - Correct endpoint URL
   - Endpoint is accessible from DOrc server
   - No firewall/network restrictions

4. **Additional Requirements:**
   - Does OAuth provider require specific headers?
   - Does it require specific request format?
   - Any rate limiting or throttling?

---

## Solution Checklist

- [ ] Verify `ClientId` is not empty and correct
- [ ] Verify `ClientSecret` is not empty and correct
- [ ] Verify `TokenEndpoint` is valid HTTPS URL
- [ ] Verify `Scope` is valid or empty
- [ ] Test token endpoint manually with PowerShell
- [ ] Check OAuth provider admin console
- [ ] Verify client is registered and active
- [ ] Verify scope is registered for client
- [ ] Check firewall/network connectivity to OAuth endpoint
- [ ] Verify TLS certificate on OAuth endpoint (if self-signed)
- [ ] Check OAuth provider logs for 400 errors
- [ ] Check RabbitMQ logs for connection attempts

---

## Configuration Paths for Reference

```
AppSettings:HighAvailability:RabbitMQ:OAuth:ClientId
AppSettings:HighAvailability:RabbitMQ:OAuth:ClientSecret
AppSettings:HighAvailability:RabbitMQ:OAuth:TokenEndpoint
AppSettings:HighAvailability:RabbitMQ:OAuth:Scope
```

## Implementation Reference

The OAuth flow is implemented in:
- `RabbitMqDistributedLockService.ConfigureOAuth2Async()`
- Uses official `RabbitMQ.Client.OAuth2` package
- Creates `OAuth2ClientBuilder` with credentials
- Builds `OAuth2Client` that makes HTTP POST to token endpoint
- `OAuth2ClientCredentialsProvider` manages credentials
- `CredentialsRefresher` handles automatic token refresh

---

## Common Questions

**Q: What does 400 Bad Request mean?**
A: The OAuth server received your request but rejected it because something was wrong with the request parameters.

**Q: Why doesn't it show the error details?**
A: The HttpResponseMessage throws an exception without including the OAuth provider's error response. Add logging to capture the response body.

**Q: Can it be a network issue?**
A: Unlikely. If network was down, you'd get a different error (connection timeout, DNS failure, etc.). 400 means the server responded.

**Q: What if OAuth server is down?**
A: You'd get a different error (timeout, connection refused, etc.), not 400.

**Q: Does scope being empty cause 400?**
A: Unlikely. Scope is optional. OAuth provider should accept empty scope.

**Q: Should I disable HighAvailability as a workaround?**
A: Yes, temporarily. Set `HighAvailability:Enabled` to `false` to bypass OAuth. But fix the underlying issue.

---

## More Help

If the checklist doesn't resolve the issue:

1. Enable debug logging on OAuth2 component
2. Capture the actual HTTP request/response with Fiddler or similar tool
3. Check OAuth provider documentation for specific requirements
4. Contact OAuth provider support with:
   - Client ID
   - Error timestamp
   - Full error response body
   - Token endpoint URL
