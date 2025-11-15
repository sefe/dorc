# Troubleshooting MS Teams Notifications

This guide helps diagnose and resolve common issues with Teams notifications in DOrc.

## CAE Support in DOrc

**DOrc's Teams notification service supports Continuous Access Evaluation (CAE):**

- Uses Microsoft.Graph SDK v5.96.0+ with native CAE support
- Uses Azure.Identity v1.14.0+ which handles CAE token refresh
- Implements proper error handling for CAE challenges

**Important Limitation for Service Principals:**

DOrc uses **client credentials flow** (service-to-service authentication) which means:
- CAE challenges **cannot be automatically resolved** because there's no interactive user to re-authenticate
- When Conditional Access policies block the service, it's a **permanent failure** requiring infrastructure changes
- The service correctly logs CAE errors with actionable guidance rather than attempting futile retries

**Reference:** [Microsoft CAE Documentation](https://learn.microsoft.com/en-us/entra/identity-platform/app-resilience-continuous-access-evaluation)

## Common Errors

### 1. Continuous Access Evaluation (CAE) Errors

**Error Message:**
```
Microsoft.Graph.Models.ODataErrors.ODataError: Continuous access evaluation resulted in challenge with result: InteractionRequired and code: LocationConditionEvaluationSatisfied
```

**What This Means:**
The service principal is being blocked by Azure AD Conditional Access policies, typically location-based policies. This happens when the service authenticates from an IP address that doesn't match the allowed locations in your Conditional Access configuration.

**Why Can't This Be Automatically Fixed?**
Unlike interactive applications where a user can re-authenticate, DOrc uses service-to-service authentication (client credentials flow). When a CAE challenge occurs, there's no way for the service to respond to the challenge automatically - it requires administrative action to update Conditional Access policies or network configuration.

#### Diagnosis Steps

1. **Check Sign-in Logs:**
   - Navigate to **Microsoft Entra admin center** > **Identity** > **Monitoring & Health** > **Sign-in Logs**
   - Filter for **Service Principal Sign-ins**
   - Look for your DOrc application's service principal
   - Check the **Conditional Access** tab to see which policies are being applied
   - Note the IP address from which the service is authenticating

2. **Review Conditional Access Policies:**
   - Go to **Microsoft Entra admin center** > **Protection** > **Conditional Access** > **Policies**
   - Identify policies that target your application or all cloud apps
   - Look for:
     - Location-based conditions
     - Named Locations that might not include your service's IP
     - Session controls with CAE enabled

3. **Verify Named Locations:**
   - Go to **Conditional Access** > **Named locations**
   - Ensure your datacenter/application IP addresses are in a **Trusted Named Location**
   - Check if the service's egress IP matches the configured locations

#### Solutions

**Solution 1: Add Service IP to Trusted Named Locations (Recommended)**

1. In Entra admin center, go to **Protection** > **Conditional Access** > **Named locations**
2. Create or edit a named location to include your service's IP address:
   - Click **New location**
   - Name: `DOrc Service IPs`
   - Mark as **Trusted location**
   - Add your service's public IP address or IP range
   - Save
3. Update relevant Conditional Access policies to trust this location

**Solution 2: Exclude Service Principal from Location Policies**

1. Go to **Conditional Access** > **Policies**
2. Find the policy blocking your service (check sign-in logs for policy name)
3. Edit the policy:
   - Under **Assignments** > **Users**
   - In **Exclude**, select **Service principals**
   - Add your DOrc application's service principal
   - Save
4. This exempts the service from location-based restrictions

**Solution 3: Ensure Consistent Egress IP**

If your service's IP address varies:
- **Azure-hosted**: Use Azure NAT Gateway or Application Gateway with static public IP
- **On-premises**: Configure firewall/proxy to use consistent egress IP for Microsoft Graph traffic
- **Hybrid**: Ensure all paths to `login.microsoftonline.com` and `graph.microsoft.com` use the same IP

**Solution 4: Disable CAE for Service Principal (Not Recommended)**

Only use this if other solutions aren't feasible:
1. Create a new Conditional Access policy
2. Target your DOrc service principal
3. In **Session** controls, select **Customize continuous access evaluation**
4. Choose **Disable CAE**
5. Save

⚠️ **Security Note:** This reduces security posture. Prefer IP whitelisting instead.

### 2. Authorization Errors

**Error Message:**
```
Authorization_RequestDenied: Insufficient privileges to complete the operation
```

**Solutions:**

1. **Verify Required Permissions:**
   - Go to Azure AD > **App registrations** > Your DOrc app
   - Click **API permissions**
   - Ensure these are present:
     - `Chat.Create` (Application)
     - `Chat.ReadWrite.All` (Application)
     - `User.Read.All` (Application)

2. **Grant Admin Consent:**
   - In **API permissions**, click **Grant admin consent for [Organization]**
   - Confirm the consent dialog
   - Wait 5-10 minutes for propagation

3. **Check Service Principal Status:**
   - Verify the service principal hasn't been disabled
   - Check the client secret hasn't expired

### 3. Authentication Token Errors

**Error Message:**
```
InvalidAuthenticationToken: Access token is empty
```

**Solutions:**

1. **Verify Configuration:**
   ```json
   {
     "Teams": {
       "TenantId": "your-tenant-id",
       "ClientId": "your-application-id",
       "ClientSecret": "your-client-secret"
     }
   }
   ```

2. **Check Client Secret Expiration:**
   - Go to Azure AD > **App registrations** > Your app > **Certificates & secrets**
   - Check secret expiration date
   - Rotate secret if needed and update configuration

3. **Verify Service Principal:**
   - Go to Azure AD > **Enterprise applications**
   - Search for your application
   - Ensure it's enabled

### 4. User Not Found Errors

**Error Message in Logs:**
```
Could not find user with email X in Teams
```

**Solutions:**

1. **Verify Email Format:**
   - Ensure the DOrc username is a valid email address
   - Only valid email addresses receive notifications

2. **Check User Exists:**
   - Verify the user exists in Azure AD
   - Ensure the user has a Teams license
   - Check the email matches the user's UserPrincipalName

## Monitoring and Diagnostics

### Enable Detailed Logging

The service automatically logs detailed error information. Check your DOrc Monitor logs for:

```
Graph API ODataError for request XXXXX:
  Error Code: InteractionRequired
  Error Message: Continuous access evaluation...
  User: user@domain.com
  Inner Error Code: ...
  Request ID: ...
```

### Useful Log Queries

If using centralized logging, search for:
- `"Teams notification blocked by conditional access"`
- `"Graph API ODataError"`
- `"TEAMS NOTIFICATION"`

## Best Practices

1. **Use Certificate Authentication:**
   - More resilient to CAE issues than client secrets
   - Configure workload identity federation if available

2. **Monitor IP Addresses:**
   - Document your service's egress IP addresses
   - Set up alerts for IP changes
   - Keep Named Locations updated

3. **Regular Permission Audits:**
   - Review app permissions quarterly
   - Ensure admin consent is current
   - Rotate secrets before expiration

4. **Test After Changes:**
   - Test notifications after Conditional Access policy changes
   - Verify after IP address changes
   - Confirm after Azure AD updates

## Getting Help

If issues persist:

1. **Collect Diagnostic Information:**
   - Complete error message from logs
   - Request ID from error details
   - Timestamp of failure
   - Service's current IP address
   - Screenshots of Conditional Access policies

2. **Check Microsoft Status:**
   - [Microsoft 365 Service Health](https://admin.microsoft.com/Adminportal/Home#/servicehealth)
   - [Azure Status](https://status.azure.com/)

3. **Review Documentation:**
   - [Continuous Access Evaluation](https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-continuous-access-evaluation)
   - [Troubleshooting CAE](https://learn.microsoft.com/en-us/entra/identity/conditional-access/howto-continuous-access-evaluation-troubleshoot)
   - [Graph API Error Handling](https://learn.microsoft.com/en-us/graph/errors)

## Related Documentation

- [Teams Notifications Setup Guide](teams-notifications-setup.md)
- [Conditional Access Best Practices](https://learn.microsoft.com/en-us/entra/identity/conditional-access/plan-conditional-access)
- [Service Principal Authentication](https://learn.microsoft.com/en-us/entra/identity-platform/scenario-daemon-app-configuration)
