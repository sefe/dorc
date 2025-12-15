# High Availability Troubleshooting Guide

## Known Issues and Workarounds

### Email Notifications with Incomplete/Empty Component Lists

**Symptom:** 
When HA is enabled with multiple monitors, environment refresh completion emails may show empty component lists or incomplete request information.

**Root Cause:**
The PowerShell email scripts (typically in DOrc components) query the database using:
```sql
WHERE dr.Id >= $DOInitialRequestID
```

This assumes all requests for an environment refresh have sequential IDs starting from `$DOInitialRequestID`. However, with HA enabled, distributed locking allows different monitor instances to process interleaved requests, breaking this assumption.

**Example:**
- Monitor A processes request IDs: 1001, 1003, 1005
- Monitor B processes request IDs: 1002, 1004, 1006
- If Monitor A sets `$DOInitialRequestID = 1001`, the email query `WHERE dr.Id >= 1001` includes ALL requests, even those processed by Monitor B
- If Monitor B sets `$DOInitialRequestID = 1002`, the email query misses request 1001

**Solution 1: Use Timestamp-Based Queries (Recommended)**

Modify the PowerShell email script to query by timestamp instead of ID:

```powershell
if ([String]::IsNullOrEmpty($DOInitialRequestID))
{
    # Query all requests for today
    $command.CommandText = "SELECT dr.Id AS 'Request ID', c.Name AS 'Component', 
        dr.RequestDetails.value('(/DeploymentRequestDetail[1]/BuildDetail[1]/BuildNumber)[1]', 'varchar(64)') AS 'Build Number',
        res.Status 
        FROM deploy.DeploymentRequest dr 
        INNER JOIN deploy.DeploymentResult res ON res.DeploymentRequestId=dr.Id 
        INNER JOIN deploy.Component c ON c.Id=res.ComponentId 
        WHERE dr.RequestDetails.value('(/DeploymentRequestDetail[1]/EnvironmentName)[1]', 'varchar(64)') = @EnvironmentName
        AND CAST(dr.[RequestedTime] AS date) = CAST(GETDATE() AS DATE) 
        ORDER BY dr.Id, c.Name"
}
else
{
    # Query by timestamp of the initial request, not by ID range
    $command.CommandText = "SELECT dr.Id AS 'Request ID', c.Name AS 'Component', 
        dr.RequestDetails.value('(/DeploymentRequestDetail[1]/BuildDetail[1]/BuildNumber)[1]', 'varchar(64)') AS 'Build Number',
        res.Status 
        FROM deploy.DeploymentRequest dr 
        INNER JOIN deploy.DeploymentResult res ON res.DeploymentRequestId=dr.Id 
        INNER JOIN deploy.Component c ON c.Id=res.ComponentId 
        WHERE dr.RequestedTime >= (
            SELECT MIN(RequestedTime) 
            FROM deploy.DeploymentRequest 
            WHERE Id = " + $DOInitialRequestID + "
        )
        AND dr.RequestDetails.value('(/DeploymentRequestDetail[1]/EnvironmentName)[1]', 'varchar(64)') = @EnvironmentName
        ORDER BY dr.Id, c.Name"
}
```

**Solution 2: Use Request ID List**

If the DOrc component can be modified to pass all request IDs as a comma-separated list:

```powershell
# Assuming $DOInitialRequestID now contains "1001,1003,1005"
$command.CommandText = "SELECT dr.Id AS 'Request ID', c.Name AS 'Component', 
    dr.RequestDetails.value('(/DeploymentRequestDetail[1]/BuildDetail[1]/BuildNumber)[1]', 'varchar(64)') AS 'Build Number',
    res.Status 
    FROM deploy.DeploymentRequest dr 
    INNER JOIN deploy.DeploymentResult res ON res.DeploymentRequestId=dr.Id 
    INNER JOIN deploy.Component c ON c.Id=res.ComponentId 
    WHERE dr.Id IN (" + $DOInitialRequestID + ")
    AND dr.RequestDetails.value('(/DeploymentRequestDetail[1]/EnvironmentName)[1]', 'varchar(64)') = @EnvironmentName
    ORDER BY dr.Id, c.Name"
```

**Solution 3: Disable HA for Specific Environments**

If email accuracy is critical and cannot be fixed in the PowerShell scripts, consider disabling HA for those specific environments by setting:
```json
{
  "AppSettings": {
    "HighAvailability": {
      "Enabled": false
    }
  }
}
```

### Split Deployments Across Multiple Monitors

**Symptom:**
Deployment requests for the same environment appear in separate deployment groups on different monitors.

**Status:** FIXED in version with race condition prevention (commit 352fe8c)

The distributed lock now checks queue message count before publishing, ensuring only one monitor acquires the lock per environment.

### OAuth Token Expiry During Long Deployments

**Symptom:**
`ACCESS_REFUSED` errors after deployments run for more than 1 hour.

**Status:** FIXED in version with automatic connection refresh (commit e4f07e5)

The implementation now detects expired OAuth tokens and automatically refreshes the connection with retry logic.

## Diagnostic Commands

### Check RabbitMQ Queue Status
```bash
# List all queues
rabbitmqctl list_queues name messages consumers

# Check specific lock queue
rabbitmqctl list_queues name messages consumers | grep "lock.env:"
```

### Check Monitor Logs for HA Activity
```bash
# Look for distributed lock acquisition
grep "Acquired distributed lock" /path/to/monitor/logs

# Look for lock release
grep "Released distributed lock" /path/to/monitor/logs

# Look for OAuth token refresh
grep "OAuth token may have expired" /path/to/monitor/logs
```

### Verify HA Configuration
```powershell
# Check if HA is enabled
$config = Get-Content "appsettings.json" | ConvertFrom-Json
$config.AppSettings.HighAvailability.Enabled

# Check RabbitMQ connection details
$config.AppSettings.HighAvailability.RabbitMQ
```

## Performance Considerations

### Lock Acquisition Timeout
Default timeout is 5 seconds. If monitors frequently fail to acquire locks, consider:
- Checking RabbitMQ server performance
- Verifying network latency between monitors and RabbitMQ
- Reviewing OAuth token acquisition time

### Queue Cleanup
Lock queues are automatically deleted after each deployment completes. If queues accumulate:
- Check monitor logs for disposal errors
- Verify RabbitMQ permissions allow queue deletion
- Manually clean up with: `rabbitmqctl delete_queue "lock.env:EnvironmentName"`

## Getting Help

For issues not covered in this guide:
1. Check monitor logs for detailed error messages
2. Verify RabbitMQ server health and connectivity
3. Ensure OAuth credentials are valid and not expired
4. Review the HA implementation documentation in `docs/HIGH_AVAILABILITY.md`
