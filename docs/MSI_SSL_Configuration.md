# MSI Installer - SSL/TLS Configuration for RabbitMQ

## Overview

The DOrc MSI installer has been updated to support configurable SSL/TLS settings for RabbitMQ OAuth and AMQPS connections during installation.

## New Installer Properties

The following properties can be passed to the MSI installer via `msiexec` command line:

| Property | Default | Description |
|----------|---------|-------------|
| `HA.ENABLED` | `false` | Enable/disable High Availability (RabbitMQ) |
| `HA.RABBITMQ.HOSTNAME` | `localhost` | RabbitMQ server hostname or IP address |
| `HA.RABBITMQ.PORT` | `5671` | RabbitMQ port (5671 for AMQPS, 5672 for AMQP) |
| `HA.RABBITMQ.VIRTUALHOST` | `/` | RabbitMQ virtual host |
| `HA.RABBITMQ.OAUTH.CLIENTID` | Empty | OAuth 2.0 client ID |
| `HA.RABBITMQ.OAUTH.CLIENTSECRET` | Empty | OAuth 2.0 client secret |
| `HA.RABBITMQ.OAUTH.TOKENENDPOINT` | Empty | OAuth 2.0 token endpoint URL |
| `HA.RABBITMQ.OAUTH.SCOPE` | Empty | OAuth 2.0 scope (optional) |
| `HA.RABBITMQ.SSL.ENABLED` | `true` | Enable SSL/TLS for RabbitMQ connection |
| `HA.RABBITMQ.SSL.SERVERNAME` | Empty | Server certificate name for custom validation |
| `HA.RABBITMQ.SSL.VERSION` | `TLS12_TLS13` | TLS version: `TLS12`, `TLS13`, or `TLS12_TLS13` |

## Usage Examples

### Basic Installation with SSL/TLS Enabled
```batch
msiexec /i Setup.Dorc.msi ^
  HA.ENABLED="true" ^
  HA.RABBITMQ.HOSTNAME="rabbitmq.internal" ^
  HA.RABBITMQ.PORT="5671" ^
  HA.RABBITMQ.SSL.SERVERNAME="rabbitmq.internal" ^
  HA.RABBITMQ.OAUTH.CLIENTID="dorc-monitor" ^
  HA.RABBITMQ.OAUTH.CLIENTSECRET="your-secret" ^
  HA.RABBITMQ.OAUTH.TOKENENDPOINT="https://auth.internal/token" ^
  /qb /L*v Setup.log
```

### Production Installation with TLS 1.3 Only
```batch
msiexec /i Setup.Dorc.msi ^
  HA.ENABLED="true" ^
  HA.RABBITMQ.HOSTNAME="rabbitmq-prod.company.com" ^
  HA.RABBITMQ.PORT="5671" ^
  HA.RABBITMQ.SSL.SERVERNAME="rabbitmq-prod.company.com" ^
  HA.RABBITMQ.SSL.VERSION="TLS13" ^
  HA.RABBITMQ.OAUTH.CLIENTID="dorc-prod" ^
  HA.RABBITMQ.OAUTH.CLIENTSECRET="prod-secret" ^
  HA.RABBITMQ.OAUTH.TOKENENDPOINT="https://auth.company.com/token" ^
  /qb /L*v Setup.log
```

### Development Installation (No SSL)
```batch
msiexec /i Setup.Dorc.msi ^
  HA.ENABLED="false" ^
  HA.RABBITMQ.SSL.ENABLED="false" ^
  HA.RABBITMQ.PORT="5672" ^
  /qb /L*v Setup.log
```

## Programmatic Installation

The template batch file `Install.Orchestrator.bat` is provided and can be modified:

```batch
@echo off
call msiexec /i "Setup.Dorc.msi" ^
  HA.RABBITMQ.SSL.ENABLED="true" ^
  HA.RABBITMQ.SSL.SERVERNAME="your-rabbitmq-server" ^
  HA.RABBITMQ.SSL.VERSION="TLS12_TLS13" ^
  /qb /L*v Setup.log
echo Returncode: %ERRORLEVEL%
```

## Configuration Files Updated

### 1. NonProdActionService.wxs
- Added JSON configuration entries for SSL settings
- Properties: `HARabbitMQSslEnabledNonProd`, `HARabbitMQSslServerNameNonProd`, `HARabbitMQSslVersionNonProd`

### 2. ProdActionService.wxs
- Added JSON configuration entries for SSL settings
- Properties: `HARabbitMQSslEnabledProd`, `HARabbitMQSslServerNameProd`, `HARabbitMQSslVersionProd`

### 3. Install.Orchestrator.bat
- Added default SSL/TLS parameters:
  - `HA.RABBITMQ.SSL.ENABLED="true"` (enabled by default)
  - `HA.RABBITMQ.SSL.VERSION="TLS12_TLS13"` (both TLS 1.2 and 1.3)
  - `HA.RABBITMQ.SSL.SERVERNAME=""` (empty for custom certificate validation)

## Installation Steps

### Step 1: Prepare Installation Command
Edit `Install.Orchestrator.bat` or create your own batch file with appropriate values.

### Step 2: Set RabbitMQ Credentials
```batch
set HA.RABBITMQ.OAUTH.CLIENTID=your-client-id
set HA.RABBITMQ.OAUTH.CLIENTSECRET=your-client-secret
set HA.RABBITMQ.OAUTH.TOKENENDPOINT=https://your-auth-server/token
```

### Step 3: Set SSL Server Name (if using self-signed certificates)
```batch
set HA.RABBITMQ.SSL.SERVERNAME=rabbitmq.yourdomain.com
```

### Step 4: Run Installation
```batch
msiexec /i Setup.Dorc.msi /qb /L*v Setup.log
```

## Configuration Verification

After installation, verify the configuration in:
```
C:\Program Files\DOrc\Deploy\Services\ActionServiceNonProd\appsettings.json
C:\Program Files\DOrc\Deploy\Services\ActionServiceProd\appsettings.json
```

Expected content:
```json
{
  "AppSettings": {
    "HighAvailability": {
      "RabbitMQ": {
        "Ssl": {
          "Enabled": "true",
          "ServerName": "your-server-name",
          "Version": "TLS12_TLS13"
        }
      }
    }
  }
}
```

## Security Notes

✅ **SSL/TLS Enabled by Default** - Ensures encrypted connections from the start
✅ **TLS 1.2 + 1.3 Support** - Supports modern TLS versions, no weak protocols
✅ **Certificate Validation** - Custom server name validation prevents MITM attacks
✅ **OAuth 2.0 Tokens** - All tokens transmitted over HTTPS with configured TLS version

## Troubleshooting

### Certificate Validation Errors
If you see certificate validation errors:
1. Ensure `HA.RABBITMQ.SSL.SERVERNAME` matches the certificate CN/SAN
2. Check that RabbitMQ certificate is issued by a trusted CA
3. For self-signed certificates, ensure the server name is configured

### TLS Connection Failures
If RabbitMQ connection fails:
1. Verify port is correct (5671 for AMQPS, 5672 for AMQP)
2. Check TLS version compatibility with RabbitMQ server
3. Review logs in `C:\Program Files\DOrc\Deploy\Services\ActionServiceNonProd\logs\`

### OAuth Token Endpoint Issues
If token acquisition fails:
1. Verify `HA.RABBITMQ.OAUTH.TOKENENDPOINT` is accessible
2. Check OAuth credentials (ClientId, ClientSecret)
3. Ensure OAuth server certificate is valid

## Rollback

To disable SSL/TLS and revert to HTTP:
```batch
msiexec /i Setup.Dorc.msi ^
  HA.RABBITMQ.SSL.ENABLED="false" ^
  HA.RABBITMQ.PORT="5672" ^
  /qb /L*v Setup.log
```

Then restart the DOrc Monitor service:
```batch
net stop DeploymentActionServiceNonProd
net start DeploymentActionServiceNonProd
```
