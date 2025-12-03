# API Split Implementation Summary

## Overview
Split Dorc.Api into two separate APIs to enable Linux compatibility:
- **Dorc.Api**: Cross-platform API (runs on Linux and Windows)
- **Dorc.Api.Windows**: Windows-specific API (requires Windows OS)

## Changes Made

### 1. Created Dorc.Api.Windows Project
**Location**: `src/Dorc.Api.Windows/`

**Structure**:
```
Dorc.Api.Windows/
├── Controllers/          # 6 Windows-specific controllers
│   ├── DirectorySearchController.cs
│   ├── ResetAppPasswordController.cs
│   ├── AccessControlController.cs
│   ├── AccountController.cs
│   ├── BundledRequestsController.cs
│   └── MakeLikeProdController.cs
├── Security/             # Windows authentication components
│   ├── WinAuthClaimsPrincipalReader.cs
│   └── WinAuthLoggingMiddleware.cs
├── Services/             # Windows-specific services
│   ├── ApiRegistry.cs
│   ├── DirectorySearcherFactory.cs
│   ├── ClaimsTransformer.cs
│   ├── CachedUserGroupReader.cs
│   ├── UserGroupReaderFactory.cs
│   └── [other supporting services]
├── Interfaces/           # Service interfaces
├── Model/                # API models
├── Program.cs            # Windows-only API bootstrapping
├── appsettings.json      # Configuration
└── README.md             # Documentation
```

**Project File**: `Dorc.Api.Windows.csproj`
- Target: `net8.0-windows`
- References: Dorc.Core, Dorc.OpenSearchData
- NuGet packages for Windows Authentication, Entity Framework, Swagger

### 2. Modified Dorc.Api for Cross-Platform Support

**Dorc.Api/Dorc.Api.csproj**:
- Added conditional compilation symbol `WINDOWS` when building on Windows
- Added `<Compile Remove>` directives to exclude Windows-specific files on Linux

**Dorc.Api/Program.cs**:
- Wrapped Windows-specific code in `#if WINDOWS` blocks:
  - `ConfigureWinAuth()` method
  - `ConfigureBoth()` method  
  - `WinAuthLoggingMiddleware` usage
  - `ApiRegistry` registration
- Changed default authentication to OAuth on non-Windows platforms

**Dorc.Api/appsettings.json**:
- Added `WindowsApiUrl` configuration: `"https://localhost:5002"`

### 3. Created WindowsApiClient for Remote Calls

**Dorc.Api/Interfaces/IWindowsApiClient.cs**:
- Interface for calling Windows API endpoints from Linux

**Dorc.Api/Services/WindowsApiClient.cs**:
- HTTP client implementation
- Maps to all 6 Windows-specific controllers
- Registered in DI only on non-Windows platforms (`#if !WINDOWS`)

### 4. Updated Configuration System

**Dorc.Core/Configuration/IConfigurationSettings.cs**:
- Added `GetWindowsApiUrl()` method

**Dorc.Core/Configuration/ConfigurationSettings.cs**:
- Implemented `GetWindowsApiUrl()` returning `AppSettings:WindowsApiUrl`
- Default value: `https://localhost:5002`

### 5. Solution Updates

**src/Dorc.sln**:
- Added Dorc.Api.Windows project to solution

## Architecture

### On Linux
```
[Dorc.Api on Linux:5000]
         |
         | HTTP calls via WindowsApiClient
         |
         v
[Dorc.Api.Windows on Windows Server:5002]
         |
         v
    Active Directory
    Windows Services
```

### On Windows
Two deployment options:

**Option 1**: Run both APIs (recommended)
```
[Dorc.Api on Windows:5000]  +  [Dorc.Api.Windows on Windows:5002]
         |                              |
         +------------------------------+
                      |
                      v
                Active Directory
                Windows Services
```

**Option 2**: Run main API with Windows features (legacy)
```
[Dorc.Api on Windows:5000]
    (includes Windows controllers via conditional compilation)
         |
         v
    Active Directory
    Windows Services
```

## Configuration Requirements

### Main API (Dorc.Api) on Linux
```json
{
  "AppSettings": {
    "WindowsApiUrl": "https://windows-server:5002",
    "AuthenticationScheme": "OAuth"
  }
}
```

### Windows API (Dorc.Api.Windows)
```json
{
  "AppSettings": {
    "DomainName": "your-domain.com",
    "DomainNameIntra": "INTRA",
    "ADUserCacheTimeMinutes": "30"
  },
  "ConnectionStrings": {
    "DOrcConnectionString": "Server=...;Database=DOrc;..."
  }
}
```

## Windows-Specific Controllers

1. **DirectorySearchController**: Search AD users/groups
2. **AccountController**: Check user/group existence in AD
3. **ResetAppPasswordController**: Reset app passwords via Windows APIs
4. **AccessControlController**: Manage AD-based access control
5. **BundledRequestsController**: Handle bundled deployment requests
6. **MakeLikeProdController**: Environment cloning operations

## Testing

### Dorc.Api (Main API)
- ✅ Builds successfully on Linux
- ✅ Excludes Windows-specific controllers on Linux
- ✅ Includes WindowsApiClient on Linux for remote calls
- ✅ Conditional compilation directives work correctly

### Dorc.Api.Windows
- ⚠️  Can only be built and tested on Windows OS
- Requires Windows platform for:
  - System.DirectoryServices
  - Windows Authentication
  - Active Directory access

## Deployment

### Development
```bash
# Main API (Linux or Windows)
cd src/Dorc.Api
dotnet run --urls "https://localhost:5000"

# Windows API (Windows only)
cd src/Dorc.Api.Windows
dotnet run --urls "https://localhost:5002"
```

### Production

**Linux Server** (Main API):
- Deploy Dorc.Api
- Configure WindowsApiUrl to point to Windows server

**Windows Server** (Windows API):
- Deploy Dorc.Api.Windows
- Ensure access to Active Directory
- Configure firewall for API port

## Benefits

1. **Linux Compatibility**: Main API can run on Linux
2. **Minimal Changes**: Surgical modifications to existing code
3. **Backward Compatible**: Can still run everything on Windows
4. **Clear Separation**: Windows-specific code isolated
5. **Type-Safe Client**: WindowsApiClient provides typed interface

## Standards Compliance

Follows .NET coding standards:
- No "Manager", "Helper", "Service" in new type names (interfaces use existing names)
- Descriptive namespaces: `Dorc.Api.Windows`
- .NET 8.0 LTS for new Windows API
- Proper separation of concerns
