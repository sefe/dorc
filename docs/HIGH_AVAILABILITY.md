# High Availability Configuration for DOrc Monitor

## Overview

The DOrc Monitor now supports High Availability (HA) deployment, allowing multiple monitor instances to run simultaneously across different machines. This ensures continuous deployment processing even if one monitor instance crashes or stops.

## How It Works

### Distributed Locking with RabbitMQ

The HA implementation uses RabbitMQ as a distributed coordination service. Key features:

1. **Environment-Level Locking**: Each deployment environment (e.g., "Production", "Staging") can only be processed by one monitor instance at a time
2. **Sequential Execution**: Deployments within an environment execute sequentially, never in parallel
3. **Automatic Failover**: If a monitor crashes, its locks are automatically released, allowing other monitors to take over
4. **Cross-Environment Parallelism**: Different environments can be processed simultaneously by different monitor instances

### Lock Mechanism

- Uses RabbitMQ **exclusive queues** for lock management
- Each environment gets a unique queue: `dorc.lock.env:{environmentName}`
- Only one monitor can declare an exclusive queue at a time
- Queues auto-delete when the monitor disconnects (automatic cleanup on crash)
- Lock leases prevent indefinite locking if a monitor hangs

## Prerequisites

- RabbitMQ server (version 3.8 or later recommended)
- Network connectivity between all monitor instances and RabbitMQ

## Configuration

### 1. Install RabbitMQ

**Using Docker with OAuth Plugin:**
```bash
docker run -d --name rabbitmq \
  -p 5672:5672 \
  -p 15672:15672 \
  -e RABBITMQ_PLUGINS=rabbitmq_auth_backend_oauth2 \
  rabbitmq:3-management

# Enable OAuth plugin
docker exec rabbitmq rabbitmq-plugins enable rabbitmq_auth_backend_oauth2
```

**Using Package Manager (Ubuntu/Debian):**
```bash
sudo apt-get update
sudo apt-get install rabbitmq-server
sudo systemctl enable rabbitmq-server
sudo systemctl start rabbitmq-server

# Enable OAuth plugin
sudo rabbitmq-plugins enable rabbitmq_auth_backend_oauth2
```

**Configure RabbitMQ OAuth:**

Create `/etc/rabbitmq/rabbitmq.conf`:
```ini
# OAuth 2.0 configuration
auth_backends.1 = oauth
auth_oauth2.resource_server_id = rabbitmq
auth_oauth2.additional_scopes_key = scope
auth_oauth2.issuer = https://your-oauth-server.com
auth_oauth2.jwks_url = https://your-oauth-server.com/.well-known/jwks.json
```

### 2. Configure Monitor appsettings.json

Update the `appsettings.json` file for each monitor instance:

```json
{
  "AppSettings": {
    "IsProduction": "True",
    "ServiceName": "DeploymentActionServiceProd",
    // ... other settings ...
    "HighAvailability": {
      "Enabled": "true",
      "RabbitMQ": {
        "HostName": "rabbitmq.yourdomain.com",
        "Port": "5672",
        "VirtualHost": "/",
        "OAuth": {
          "UserName": "svc-dorc-monitor@domain.com",
          "Password": "service-account-password",
          "TokenEndpoint": "https://auth.yourdomain.com/oauth/token",
          "ClientId": "dorc-monitor",
          "Scope": "rabbitmq:configure:* rabbitmq:read:* rabbitmq:write:*"
        }
      }
    }
  }
}
```

### Configuration Options

| Setting | Required | Default | Description |
|---------|----------|---------|-------------|
| `Enabled` | Yes | `false` | Enable/disable high availability mode |
| `HostName` | When HA enabled | `localhost` | RabbitMQ server hostname or IP |
| `Port` | No | `5672` | RabbitMQ server port |
| `VirtualHost` | No | `/` | RabbitMQ virtual host |
| `OAuth:UserName` | When HA enabled | - | Service account username for OAuth authentication |
| `OAuth:Password` | When HA enabled | - | Service account password for OAuth authentication |
| `OAuth:TokenEndpoint` | When HA enabled | - | OAuth 2.0 token endpoint URL |
| `OAuth:ClientId` | When HA enabled | - | OAuth 2.0 client ID for Resource Owner Password Credentials flow |
| `OAuth:Scope` | No | - | OAuth 2.0 scope (optional, depends on OAuth server configuration) |

## Deployment Scenarios

### Single Monitor (Traditional)

```
┌─────────────┐
│   Monitor   │ ──► Processes all environments
└─────────────┘
```

Configuration:
```json
"HighAvailability": {
  "Enabled": "false"
}
```

### Multiple Monitors with HA

```
┌─────────────┐      ┌──────────────┐
│  Monitor 1  │      │   Monitor 2  │
│  (Active)   │ ◄──► │  (Standby)   │
└─────────────┘      └──────────────┘
       │                     │
       └──────── RabbitMQ ───┘
```

Configuration on all monitors:
```json
"HighAvailability": {
  "Enabled": "true",
  "RabbitMQ": {
    "HostName": "rabbitmq-server",
    "Port": "5672",
    "VirtualHost": "/",
    "OAuth": {
      "UserName": "svc-dorc-monitor@domain.com",
      "Password": "service-account-password",
      "TokenEndpoint": "https://auth.domain.com/oauth/token",
      "ClientId": "dorc-monitor",
      "Scope": "rabbitmq:*"
    }
  }
}
```

**Behavior:**
- Both monitors poll for pending requests
- First monitor to acquire environment lock processes the deployment
- Second monitor skips and tries next iteration
- If Monitor 1 crashes, Monitor 2 takes over immediately

### Load Distribution

With 3 environments and 2 monitors:

```
Monitor 1: Processing Production  ──► [Env Lock: env:Production]
Monitor 2: Processing Staging     ──► [Env Lock: env:Staging]
           (Development pending)
```

Both monitors can work simultaneously on different environments.

## Monitoring and Troubleshooting

### Verify RabbitMQ Connection

1. Access RabbitMQ Management UI: `http://rabbitmq-server:15672`
2. Login with configured credentials
3. Check the **Connections** tab - you should see active connections from each monitor
4. Check the **Queues** tab - you should see `dorc.lock.env:*` queues when deployments are running

### Log Messages

When HA is enabled, monitor logs include:

```
[INFO] Established RabbitMQ connection to rabbitmq-server:5672
[INFO] Acquired distributed lock for environment 'Production' to process request 12345
[DEBUG] Released distributed lock for environment 'Production'
[DEBUG] Could not acquire distributed lock for environment 'Staging' - likely being processed by another monitor instance
```

### Common Issues

**Issue: Monitor can't connect to RabbitMQ**
- Check network connectivity: `telnet rabbitmq-server 5672`
- Verify credentials
- Check RabbitMQ is running: `systemctl status rabbitmq-server`

**Issue: Requests not being processed**
- Check RabbitMQ queues aren't stuck
- Restart all monitors to clear locks
- Verify database connectivity

**Issue: Multiple monitors processing same environment**
- This should never happen with HA enabled
- Check RabbitMQ is functioning correctly
- Verify all monitors have same configuration

## Performance Considerations

- **Lock acquisition overhead**: ~10-50ms per environment check
- **Lock lease time**: Default 300 seconds (5 minutes) - sufficient for most deployments
- **Heartbeat interval**: 60 seconds - monitors send heartbeats to RabbitMQ
- **Recovery time**: If a monitor crashes, another can acquire the lock within 1-2 seconds

## Backward Compatibility

Setting `HighAvailability.Enabled` to `false` or omitting the HA configuration entirely will use the original single-monitor behavior with no changes required to existing deployments.

## Security Recommendations

1. **OAuth 2.0 ROPC Flow**: Uses Resource Owner Password Credentials flow with service account for RabbitMQ authentication
2. **Service Account Security**: Store service account password in secure configuration (e.g., Azure Key Vault, environment variables)
3. **TLS/SSL**: Enable TLS/SSL for both RabbitMQ connections and OAuth token endpoint
4. **Least Privilege**: Configure OAuth scopes to grant only the minimum required permissions
5. **Network Security**: Run RabbitMQ in a private network, not exposed to the internet
6. **Token Caching**: Tokens are cached and automatically refreshed to minimize token endpoint calls
7. **Audit Logging**: Enable RabbitMQ audit logging to track connection and queue operations
8. **Service Account Management**: Use dedicated service account with limited permissions for DOrc Monitor

## Maintenance

### Planned Monitor Downtime

To gracefully stop a monitor:
1. Stop the monitor service
2. Locks will auto-release within seconds
3. Other monitors will take over

No manual cleanup required.

### RabbitMQ Maintenance

To perform RabbitMQ maintenance:
1. Stop all monitors
2. Perform RabbitMQ upgrade/maintenance
3. Restart monitors
4. Verify connections in RabbitMQ Management UI

## Testing HA Setup

### Test Failover

1. Deploy 2 monitors with HA enabled
2. Submit a deployment request
3. Monitor 1 acquires lock and processes
4. Kill Monitor 1 process
5. Verify Monitor 2 takes over within seconds

### Test Load Distribution

1. Deploy 2 monitors with HA enabled
2. Submit deployments to 3 different environments simultaneously
3. Verify each monitor processes different environments
4. Check RabbitMQ queues to confirm environment locks

## Advanced Configuration

### Custom Virtual Host

To isolate DOrc locks from other RabbitMQ usage:

```bash
# Create virtual host on RabbitMQ server
rabbitmqctl add_vhost dorc
rabbitmqctl set_permissions -p dorc dorc ".*" ".*" ".*"
```

Then configure monitors:
```json
"VirtualHost": "dorc"
```

### High Availability RabbitMQ

For production environments, consider clustering RabbitMQ:
- Deploy 3+ RabbitMQ nodes in cluster
- Configure monitors to connect to cluster endpoint
- Enable mirrored queues for lock queues

See [RabbitMQ Clustering Guide](https://www.rabbitmq.com/clustering.html)
