# Dorc.Api.Windows

Windows-specific API for DOrc that handles platform-dependent operations requiring Windows OS features.

## Purpose

This API contains Windows-specific controllers that were split from the main Dorc.Api to enable Linux compatibility. It must run on Windows OS as it relies on:
- Active Directory integration (System.DirectoryServices)
- Windows Authentication
- Windows-specific security features

## Controllers

The following controllers are hosted in this API:

1. **DirectorySearchController** - Search users and groups in Active Directory
2. **AccountController** - Check user/group existence in AD
3. **ResetAppPasswordController** - Reset application passwords using Windows APIs
4. **AccessControlController** - Manage access control using AD groups
5. **BundledRequestsController** - Handle bundled deployment requests
6. **MakeLikeProdController** - Environment cloning operations

## Configuration

### appsettings.json

Key configuration settings:

```json
{
  "AppSettings": {
    "DomainName": "your-domain.com",
    "DomainNameIntra": "INTRA",
    "AllowedCORSLocations": "http://localhost:8888,https://localhost:7159",
    "ADUserCacheTimeMinutes": "30"
  },
  "ConnectionStrings": {
    "DOrcConnectionString": "Server=...;Database=DOrc;..."
  },
  "OpenSearchSettings": {
    "ConnectionUri": "https://opensearch:9200",
    "UserName": "admin",
    "Password": "..."
  }
}
```

### Main API Configuration

The main Dorc.Api must be configured to call this Windows API when running on Linux. Add to Dorc.Api appsettings.json:

```json
{
  "AppSettings": {
    "WindowsApiUrl": "https://windows-server:5002"
  }
}
```

## Running

### Development

```bash
cd src/Dorc.Api.Windows
dotnet run --urls "https://localhost:5002"
```

### Production

The Windows API should run on a Windows server on a separate port from the main API (e.g., port 5002).

Ensure:
- The server has access to Active Directory
- Windows Authentication is enabled
- The main Dorc.Api can reach this API endpoint

## Architecture

When the main Dorc.Api runs on Linux, it forwards Windows-specific requests to this API via HTTP:

```
[Linux Server]                    [Windows Server]
Dorc.Api (port 5000)  -------->  Dorc.Api.Windows (port 5002)
   |                                    |
   | HTTP Client                        | Windows APIs
   | (WindowsApiClient)                 | Active Directory
   |                                    | DirectoryServices
```

When the main Dorc.Api runs on Windows, it can either:
1. Run both APIs on the same server (recommended for simplicity)
2. Use the Windows-specific features directly (legacy mode)

## Dependencies

- .NET 8.0 (Windows)
- System.DirectoryServices
- Active Directory access
- SQL Server access
- OpenSearch cluster
