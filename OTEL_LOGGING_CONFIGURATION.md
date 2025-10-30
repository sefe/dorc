# OpenTelemetry Logging Configuration Guide

## Overview
All DOrc components now use native .NET logging with OpenTelemetry support. Logs are written to:
1. **Local disk files** - with automatic rotation
2. **OpenTelemetry Collector** - via OTLP protocol (http://localhost:4317 by default)

## Configuration Settings

All hosts support the following configuration in their `appsettings.json`:

```json
{
  "OpenTelemetry": {
    "OtlpEndpoint": "http://localhost:4317",
    "FileLogging": {
      "Enabled": true,
      "LogPath": "c:\\Log\\DOrc\\...",
      "FileSizeLimitMB": 10,
      "MaxRollingFiles": 100
    }
  }
}
```

### Configuration Parameters

- **OtlpEndpoint**: URL of the OpenTelemetry Collector OTLP receiver (e.g., `http://localhost:4317`)
  - Leave empty or omit to disable OTLP export
  - Default: `http://localhost:4317`

- **FileLogging.Enabled**: Enable/disable file logging
  - Default: `true`

- **FileLogging.LogPath**: Directory path for log files
  - Logs are written as `{servicename}.log` in this directory
  - Directory is automatically created if it doesn't exist

- **FileLogging.FileSizeLimitMB**: Maximum size of each log file in MB
  - Default: `10` MB
  - When limit is reached, file is rotated

- **FileLogging.MaxRollingFiles**: Maximum number of rotated log files to keep
  - Default: `100` for services, `10` for CLI tools
  - Oldest files are deleted when limit is exceeded

## Component-Specific Default Log Paths

| Component | Default Log Path |
|-----------|-----------------|
| **Dorc.Api** | `c:\Log\DOrc\Deploy\Web\Api` |
| **Dorc.Monitor** | `c:\Log\DOrc\Deploy\Services` |
| **Dorc.Runner** | `c:\Log\DOrc\Deploy\Services\Requests` |
| **Tools.RequestCLI** | `c:\Log\DOrc\Tools\RequestCLI` |
| **Tools.PostRestoreEndurCLI** | `c:\Log\DOrc\Tools\PostRestoreEndurCLI` |
| **Tools.DeployCopyEnvBuildCLI** | `c:\Log\DOrc\Tools\DeployCopyEnvBuildCLI` |
| **Tools.PropertyValueCreationCLI** | `c:\Log\DOrc\Tools\PropertyValueCreationCLI` |

## MSI Installer Requirements

The MSI installer must perform the following tasks during installation:

### 1. Create Log Directories
Create all required log directories with appropriate permissions:
```
c:\Log\DOrc\Deploy\Web\Api
c:\Log\DOrc\Deploy\Services
c:\Log\DOrc\Deploy\Services\Requests
c:\Log\DOrc\Tools\RequestCLI
c:\Log\DOrc\Tools\PostRestoreEndurCLI
c:\Log\DOrc\Tools\DeployCopyEnvBuildCLI
c:\Log\DOrc\Tools\PropertyValueCreationCLI
```

### 2. Configure OpenTelemetry Endpoints

During installation, the MSI should:

1. **Prompt for OpenTelemetry Collector URL** (or use default `http://localhost:4317`)
2. **Update all appsettings.json files** with the configured OTLP endpoint:
   - `src\Dorc.Api\appsettings.json`
   - `src\Dorc.Monitor\appsettings.json`
   - `src\Dorc.Runner\appsettings.json`
   - `src\Tools.RequestCLI\appsettings.json`
   - `src\Tools.PostRestoreEndurCLI\appsettings.json`
   - `src\Tools.DeployCopyEnvBuildCLI\appsettings.json`
   - `src\Tools.PropertyValueCreationCLI\appsettings.json`

### 3. Set File Permissions
Ensure service accounts have write permissions to log directories:
- IIS AppPool identity for Dorc.Api
- Windows Service account for Dorc.Monitor
- Runner process account for Dorc.Runner

### 4. Example WiX Configuration

```xml
<!-- Create log directories -->
<Directory Id="LOGDIR" Name="Log">
  <Directory Id="DORCLOGDIR" Name="DOrc">
    <Directory Id="DEPLOYLOGDIR" Name="Deploy">
      <Directory Id="WEBLOGDIR" Name="Web">
        <Directory Id="APILOGDIR" Name="Api">
          <Component Id="ApiLogDir" Guid="...">
            <CreateFolder>
              <Permission User="[APPPOOLIDENTITY]" GenericAll="yes"/>
            </CreateFolder>
          </Component>
        </Directory>
      </Directory>
      <Directory Id="SERVICESLOGDIR" Name="Services">
        <Component Id="ServicesLogDir" Guid="...">
          <CreateFolder>
            <Permission User="[SERVICEACCOUNT]" GenericAll="yes"/>
          </CreateFolder>
        </Component>
        <Directory Id="REQUESTSLOGDIR" Name="Requests">
          <Component Id="RequestsLogDir" Guid="...">
            <CreateFolder>
              <Permission User="[RUNNERACCOUNT]" GenericAll="yes"/>
            </CreateFolder>
          </Component>
        </Directory>
      </Directory>
    </Directory>
    <Directory Id="TOOLSLOGDIR" Name="Tools">
      <!-- Similar for each tool -->
    </Directory>
  </Directory>
</Directory>

<!-- Configure OTLP endpoint via custom action -->
<Property Id="OTLPENDPOINT" Value="http://localhost:4317" />
<CustomAction Id="UpdateOtlpConfig" 
              BinaryKey="ConfigUpdate" 
              DllEntry="UpdateAppsettings" 
              Execute="deferred" 
              Impersonate="no" />
```

### 5. Configuration Update Script Example

The installer should include a custom action to update all `appsettings.json` files with the configured OTLP endpoint:

```csharp
[CustomAction]
public static ActionResult UpdateAppsettings(Session session)
{
    string otlpEndpoint = session["OTLPENDPOINT"];
    string installDir = session["INSTALLDIR"];
    
    var appsettingsPaths = new[]
    {
        Path.Combine(installDir, "Dorc.Api", "appsettings.json"),
        Path.Combine(installDir, "Dorc.Monitor", "appsettings.json"),
        // ... add all other paths
    };
    
    foreach (var path in appsettingsPaths)
    {
        if (File.Exists(path))
        {
            var json = JObject.Parse(File.ReadAllText(path));
            json["OpenTelemetry"]["OtlpEndpoint"] = otlpEndpoint;
            File.WriteAllText(path, json.ToString());
        }
    }
    
    return ActionResult.Success;
}
```

## OpenTelemetry Collector Setup

### Installing the Collector

1. Download OpenTelemetry Collector from: https://github.com/open-telemetry/opentelemetry-collector/releases
2. Install as Windows Service

### Sample Collector Configuration

Create `otel-collector-config.yaml`:

```yaml
receivers:
  otlp:
    protocols:
      grpc:
        endpoint: 0.0.0.0:4317
      http:
        endpoint: 0.0.0.0:4318

processors:
  batch:
    timeout: 10s
    send_batch_size: 1024

exporters:
  logging:
    verbosity: detailed
  
  # Example: Export to Jaeger
  jaeger:
    endpoint: jaeger:14250
    tls:
      insecure: true
  
  # Example: Export to Prometheus
  prometheus:
    endpoint: "0.0.0.0:8889"
  
  # Example: Export to file
  file:
    path: /var/log/otel/traces.json

service:
  pipelines:
    traces:
      receivers: [otlp]
      processors: [batch]
      exporters: [logging, jaeger]
    
    metrics:
      receivers: [otlp]
      processors: [batch]
      exporters: [logging, prometheus]
    
    logs:
      receivers: [otlp]
      processors: [batch]
      exporters: [logging, file]
```

### Running the Collector as Windows Service

```powershell
# Install service
sc.exe create OtelCollector binPath= "C:\otel\otelcol.exe --config=C:\otel\config.yaml"
sc.exe description OtelCollector "OpenTelemetry Collector Service"

# Start service
sc.exe start OtelCollector

# Configure to start automatically
sc.exe config OtelCollector start= auto
```

## Environment-Specific Configuration

For different environments (Dev, QA, Prod), update the `OtlpEndpoint` in appsettings.json:

- **Development**: `http://localhost:4317`
- **QA**: `http://qa-otel-collector:4317`
- **Production**: `http://prod-otel-collector:4317`

## Troubleshooting

### Logs not appearing in files
1. Check log directory exists and has write permissions
2. Verify `FileLogging.Enabled` is set to `true`
3. Check disk space

### Logs not reaching OpenTelemetry Collector
1. Verify OTLP Collector is running: `sc query OtelCollector`
2. Check `OtlpEndpoint` URL is correct
3. Verify network connectivity to collector
4. Check collector logs for errors
5. Leave `OtlpEndpoint` empty to disable OTLP export if not needed

### High disk usage
1. Reduce `FileSizeLimitMB` to smaller value
2. Reduce `MaxRollingFiles` to keep fewer files
3. Set log level to Warning or Error instead of Information

## Migration Notes

This implementation:
- ✅ Uses **100% native .NET libraries** (Microsoft.Extensions.Logging, OpenTelemetry)
- ✅ **No third-party dependencies** (no log4net, no Serilog)
- ✅ Saves all logs to **local disk** with automatic rotation
- ✅ Sends all logs to **OpenTelemetry Collector** via OTLP protocol
- ✅ Works with **.NET 8 and .NET Framework 4.8** projects
- ✅ **Configurable** via appsettings.json
- ✅ **Production-ready** with proper error handling

## Support

For issues or questions, refer to:
- OpenTelemetry .NET documentation: https://opentelemetry.io/docs/instrumentation/net/
- DOrc deployment documentation
- Internal DevOps team
