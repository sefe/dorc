# Server and Database Connectivity Monitoring

## Overview

DOrc now includes a system to periodically monitor the connectivity status of all registered servers and databases. This helps identify infrastructure issues proactively and provides visibility into which resources are currently reachable.

## Features

### Background Monitoring Service
- Automatically checks connectivity to all servers and databases every hour
- Runs as a hosted service within the Dorc.Monitor application
- Updates status information in the database for each check
- Logs warnings when resources become unreachable

### Server Connectivity Check
- Uses ICMP ping to verify server reachability
- 5-second timeout per check
- Verifies basic network connectivity to the server

### Database Connectivity Check  
- Attempts to establish a SQL Server connection
- 5-second timeout per connection attempt
- Uses integrated security (Windows Authentication)
- Verifies both network connectivity and SQL Server availability

### Web UI Indicators

The Status column in both the Servers and Databases list views displays:

- ✅ **Green check + "Online"**: Resource is currently reachable
  - Tooltip shows: "Last checked: [timestamp]"

- ❌ **Red X + "Offline"**: Resource was unreachable in the last check
  - Tooltip shows: "Last checked: [timestamp]"

- ⚠️ **Orange warning + "Unreachable (7+ days)"**: Resource has been unreachable for 7 or more days
  - Tooltip shows: "Not reachable since: [timestamp]"
  - This indicates the resource may need attention or may have been decommissioned

- ⚪ **Gray "Not checked"**: Connectivity has not been checked yet
  - Appears for newly added resources before the first check cycle

## Configuration

### Enabling Connectivity Checks

The connectivity check service is **disabled by default** to avoid performance issues when multiple Monitor instances are running. To enable it, configure the `appsettings.json` file:

**File**: `src/Dorc.Monitor/appsettings.json`
```json
{
  "AppSettings": {
    "EnableConnectivityCheck": "True",
    "ConnectivityCheckIntervalMinutes": "60"
  }
}
```

**Important**: Only enable this on **one** Monitor instance (typically the Production Monitor) to avoid:
- Multiple instances checking the same resources simultaneously
- Database contention when updating connectivity status
- Unnecessary network traffic

### Check Frequency

The connectivity check interval can be configured in `appsettings.json`:

**Default**: 60 minutes (1 hour)

To modify, change the `ConnectivityCheckIntervalMinutes` setting:
```json
"ConnectivityCheckIntervalMinutes": "30"  // Check every 30 minutes
```

**Initial Delay**: The service waits 2 minutes after starting before performing the first check to allow the Monitor service to fully initialize.

### Unreachable Threshold
The threshold for showing the orange warning is 7 days. This can be modified in the UI renderers:

**Files**: 
- `src/dorc-web/src/pages/page-servers-list.ts`
- `src/dorc-web/src/pages/page-databases-list.ts`

```typescript
const oneWeekAgo = new Date(Date.now() - 7 * 24 * 60 * 60 * 1000);
```

## Database Schema Changes

Two new columns have been added to track connectivity status:

### SERVER Table
- `LastChecked` (DATETIME2, nullable): Timestamp of the last connectivity check
- `IsReachable` (BIT, nullable): Boolean indicating if the server was reachable

### DATABASE Table
- `LastChecked` (DATETIME2, nullable): Timestamp of the last connectivity check
- `IsReachable` (BIT, nullable): Boolean indicating if the database was reachable

### Migration
A migration script is included to add these columns to existing databases:
`src/Dorc.Database/Scripts/Post-Deployment/AddConnectivityStatusColumns.sql`

The script safely adds the columns only if they don't already exist.

## Logging

The connectivity service logs:
- Info messages when starting/stopping and configuration status
- Info messages when starting each check cycle
- Info messages after completing each check cycle with counts
- Warning messages for individual unreachable resources
- Error messages for unexpected failures during checks

**Log Location**: Check the Dorc.Monitor application logs

**Example Log Messages**:
```
INFO  - Connectivity Check Service is starting. Check interval: 60 minutes.
INFO  - Waiting 2 minutes before first connectivity check...
INFO  - Starting connectivity check cycle...
INFO  - Completed connectivity check for 42 servers.
WARN  - Server PROD-WEB-01 (ID: 123) is not reachable.
INFO  - Completed connectivity check for 18 databases.
WARN  - Database AppDB on PROD-SQL-01 (ID: 456) is not reachable.
INFO  - Connectivity check cycle completed.
```

**Disabled Service**:
```
INFO  - Connectivity Check Service is disabled in configuration.
```

## Troubleshooting

### Service Not Starting
- Check if `EnableConnectivityCheck` is set to `"True"` in `appsettings.json`
- Review logs for the message: "Connectivity Check Service is disabled in configuration."
- Verify the Monitor service has the necessary configuration settings
- Check that the service waits 2 minutes before the first check (initial delay is intentional)

### Servers Always Showing as Offline
- Verify the Dorc.Monitor service has network access to ping the servers
- Check if ICMP (ping) is blocked by firewalls
- Ensure server names are correct and resolve via DNS

### Databases Always Showing as Offline
- Verify the Dorc.Monitor service account has SQL Server login permissions
- Check if SQL Server is running and accepting connections
- Ensure Windows Authentication is configured correctly
- Verify firewall rules allow SQL Server connections (default port 1433)

### Checks Not Running
- Verify the Dorc.Monitor service is running
- Check the Monitor service logs for errors
- Ensure `EnableConnectivityCheck` is set to `"True"` in appsettings.json
- Verify only ONE Monitor instance has connectivity checking enabled
- Look for the initial 2-minute delay message in logs

### Status Not Updating in UI
- Refresh the browser page
- Verify the Dorc.Api is serving the updated status fields
- Check browser console for API errors
- Wait for the next check cycle (default: 60 minutes after service start)

### Multiple Monitor Instances Conflict
- Only enable connectivity checking on ONE Monitor instance (typically Production)
- Set `EnableConnectivityCheck` to `"False"` on all other instances
- This prevents database contention and duplicate checks

## Security Considerations

- Server checks use ICMP ping only - no credentials required
- Database checks use integrated security (Windows Authentication)
- No passwords or credentials are stored or logged
- Connection failures are handled gracefully without exposing sensitive details
- The service runs with the same permissions as the Dorc.Monitor application

## Future Enhancements

Potential improvements for future versions:
- Configurable check intervals per resource
- Email/webhook notifications for resources that become unreachable
- Historical connectivity reporting and trends
- Support for alternative authentication methods for databases
- Customizable timeout values per resource type
- Dashboard view showing overall infrastructure health
