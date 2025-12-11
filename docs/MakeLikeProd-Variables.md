# Make Like Prod (MLP) Variables

This document describes the variables available for use in PowerShell scripts during Make Like Prod (MLP) deployments.

## Overview

When performing a Make Like Prod operation, DOrc creates multiple deployment requests based on the bundled requests configured for the MLP operation. During this process, several variables are automatically set and made available for use in PowerShell deployment scripts through the variable resolver.

## Available Variables

### $AllRequestIds$

**Type:** String (comma-separated list)  
**Description:** Contains all request IDs created during the entire MLP deployment operation.  
**Format:** Comma-separated list of integers (e.g., "12345,12346,12347,12348")  
**Availability:** Set after all bundled requests have been processed

This variable is useful when you need to reference all deployment requests created during the MLP operation, such as:
- Generating email notifications that include all request IDs
- Creating database queries that filter by all MLP request IDs
- Building audit trails that link all related deployments

**Example Usage in PowerShell:**
```powershell
# Use in SQL WHERE clause
$requestIds = "$AllRequestIds$"
$query = "SELECT * FROM DeploymentRequests WHERE RequestId IN ($requestIds)"

# Use in email notification
$emailBody = "The following requests were created during MLP: $AllRequestIds$"
```

**Example Usage in SQL Scripts:**
```sql
-- Query deployment status for all MLP requests
SELECT dr.Id, dr.Status, dr.ProjectName, dr.EnvironmentName
FROM DeploymentRequests dr
WHERE dr.Id IN ($AllRequestIds$)
ORDER BY dr.Id;
```

### $StartingRequestId$

**Type:** String  
**Description:** Contains the first request ID created during the MLP deployment operation.  
**Format:** Integer as string (e.g., "12345")  
**Availability:** Set as soon as the first bundled request is processed

This variable provides backward compatibility and is useful when you only need to reference the initial request in the MLP sequence.

**Example Usage:**
```powershell
$firstRequestId = "$StartingRequestId$"
Write-Host "MLP deployment started with request ID: $firstRequestId"
```

### Other Standard Variables

The following standard DOrc variables are also available during MLP operations:

- **$CreatedByUserEmail$** - Email address of the user who initiated the MLP operation
- **$DataBackup$** - The selected data backup option (e.g., "Live Snap" or "Staging Snap: ...")
- **$TargetEnvironmentName$** - The name of the target environment for the MLP operation

Plus any custom bundle properties defined in the MLP request.

## Implementation Details

### How Variables Are Set

1. **Before processing bundled requests:**
   - `$CreatedByUserEmail$` is set from the authenticated user
   - `$DataBackup$` is set from the MLP request
   - `$TargetEnvironmentName$` is set from the MLP request
   - Custom bundle properties are set from the MLP request

2. **During bundled request processing:**
   - Each bundled request (JobRequest or CopyEnvBuild) creates one or more deployment requests
   - Request IDs are collected as they are created
   - `$StartingRequestId$` is set when the first request ID is available

3. **After all bundled requests are processed:**
   - `$AllRequestIds$` is set with a comma-separated list of all collected request IDs

### Variable Resolution

Variables are resolved by the `BundledRequestVariableResolver` which is injected into the `MakeLikeProdController`. The variable resolver:

1. Stores variables in a thread-safe dictionary
2. Supports variable substitution using the `$VariableName$` syntax
3. Evaluates expressions and nested variable references
4. Integrates with the property evaluation system

## Best Practices

1. **Always check for empty values:** While variables should always be set during normal MLP operations, include error handling in your scripts:
   ```powershell
   if ([string]::IsNullOrEmpty("$AllRequestIds$")) {
       Write-Error "No request IDs available"
       exit 1
   }
   ```

2. **Use appropriate variables for your use case:**
   - Use `$AllRequestIds$` when you need to reference all requests in the MLP operation
   - Use `$StartingRequestId$` when you only need the first request ID

3. **Format for SQL:** The `$AllRequestIds$` variable is pre-formatted for SQL IN clauses:
   ```sql
   WHERE dr.Id IN ($AllRequestIds$)
   ```

4. **Email notifications:** Include all request IDs for complete traceability:
   ```powershell
   $emailBody = @"
   Make Like Prod completed for environment: $TargetEnvironmentName$
   
   Request IDs: $AllRequestIds$
   Started by: $CreatedByUserEmail$
   Data source: $DataBackup$
   "@
   ```

## Related Documentation

- [DOrc Variable Resolution System](../src/Dorc.Core/VariableResolution/)
- [Make Like Prod Controller](../src/Dorc.Api/Controllers/MakeLikeProdController.cs)
- [Bundled Request Variable Resolver](../src/Dorc.Core/VariableResolution/BundledRequestVariableResolver.cs)
