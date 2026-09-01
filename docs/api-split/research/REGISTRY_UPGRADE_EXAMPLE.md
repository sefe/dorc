# Registry Dependency Upgrade Example

This document demonstrates a concrete example of upgrading from Windows-only Registry APIs to cross-platform alternatives.

## Current Implementation (Windows-Only)

### File: `Dorc.Api/Controllers/RefDataServersController.cs`

**Current Code - Lines 130-148:**

```csharp
using Microsoft.Win32;  // ❌ Windows-only namespace

[HttpGet]
[SwaggerResponse(StatusCodes.Status400BadRequest, Type = typeof(string))]
[SwaggerResponse(StatusCodes.Status200OK, Type = typeof(ServerOperatingSystemApiModel))]
[Route("GetServerOperatingFromTarget")]
public IActionResult GetServerOperatingFromTarget(string serverName)
{
    var output = new ServerOperatingSystemApiModel();
    
    // ❌ Windows-only Registry API
    using (var reg = RegistryKey.OpenRemoteBaseKey(RegistryHive.LocalMachine, serverName))
    using (var key = reg.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\"))
    {
        if (key == null)
            return BadRequest("Unable to open the target machine");

        output.ProductName = key.GetValue("ProductName").ToString();
        output.CurrentVersion = key.GetValue("CurrentVersion").ToString();
    }
    
    return Ok(output);
}
```

**Issues:**
- ❌ Uses `Microsoft.Win32` namespace (Windows-only)
- ❌ `RegistryKey.OpenRemoteBaseKey()` requires Windows
- ❌ Cannot run on Linux
- ❌ Tightly coupled to Windows Registry structure

---

## Upgraded Implementation (Cross-Platform)

### Option 1: RuntimeInformation API (Recommended for Local Server)

**For detecting OS information on the server running the API:**

```csharp
using System.Runtime.InteropServices;  // ✅ Cross-platform

[HttpGet]
[SwaggerResponse(StatusCodes.Status200OK, Type = typeof(ServerOperatingSystemApiModel))]
[Route("GetServerOperating")]
public IActionResult GetServerOperating()
{
    var output = new ServerOperatingSystemApiModel
    {
        // ✅ Cross-platform - works on Windows, Linux, macOS
        ProductName = RuntimeInformation.OSDescription,
        CurrentVersion = Environment.OSVersion.Version.ToString(),
        Architecture = RuntimeInformation.OSArchitecture.ToString(),
        Framework = RuntimeInformation.FrameworkDescription
    };
    
    return Ok(output);
}
```

**Benefits:**
- ✅ Works on Windows, Linux, macOS
- ✅ Built-in to .NET 8 (no extra packages)
- ✅ No Windows dependencies
- ✅ More reliable than Registry parsing

**Example Outputs:**

*On Windows Server 2019:*
```json
{
  "ProductName": "Microsoft Windows 10.0.17763",
  "CurrentVersion": "10.0.17763.0",
  "Architecture": "X64",
  "Framework": ".NET 8.0.0"
}
```

*On Linux (Ubuntu 22.04):*
```json
{
  "ProductName": "Linux 5.15.0-1052-azure #60-Ubuntu SMP",
  "CurrentVersion": "5.15.0.1052",
  "Architecture": "X64",
  "Framework": ".NET 8.0.0"
}
```

---

### Option 2: Remote Server Management (Windows API Proxy)

**For querying remote servers (original use case):**

Since the original code queries *remote* servers, we need a different approach:

#### Step 1: Move to Windows API

Move this functionality to `Dorc.Api.Windows` since it requires Windows-specific remote registry access:

**File: `Dorc.Api.Windows/Controllers/ServerInfoController.cs`** (NEW)

```csharp
using Microsoft.Win32;
using Microsoft.AspNetCore.Mvc;
using System.Runtime.Versioning;

namespace Dorc.Api.Windows.Controllers
{
    [SupportedOSPlatform("windows")]
    [ApiController]
    [Route("[controller]")]
    public class ServerInfoController : ControllerBase
    {
        [HttpGet]
        [Route("GetOperatingSystem")]
        public IActionResult GetOperatingSystem(string serverName)
        {
            var output = new ServerOperatingSystemApiModel();
            
            try
            {
                using (var reg = RegistryKey.OpenRemoteBaseKey(RegistryHive.LocalMachine, serverName))
                using (var key = reg.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\"))
                {
                    if (key == null)
                        return BadRequest("Unable to open the target machine");

                    output.ProductName = key.GetValue("ProductName")?.ToString() ?? "Unknown";
                    output.CurrentVersion = key.GetValue("CurrentVersion")?.ToString() ?? "Unknown";
                    output.BuildNumber = key.GetValue("CurrentBuildNumber")?.ToString() ?? "Unknown";
                }
                
                return Ok(output);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Failed to query remote registry: {ex.Message}");
            }
        }
    }
}
```

#### Step 2: Create Interface in Main API

**File: `Dorc.Api/Interfaces/IWindowsApiClient.cs`** (already exists)

Add method to the interface:

```csharp
public interface IWindowsApiClient
{
    Task<ServerOperatingSystemApiModel> GetServerOperatingSystemAsync(string serverName);
    // ... other methods
}
```

#### Step 3: Update Main API Controller

**File: `Dorc.Api/Controllers/RefDataServersController.cs`** (UPDATED)

```csharp
using System.Runtime.InteropServices;  // ✅ Cross-platform

public class RefDataServersController : ControllerBase
{
    private readonly ISecurityPrivilegesChecker _securityPrivilegesChecker;
    private readonly IServersPersistentSource _serversPersistentSource;
    private readonly IEnvironmentsPersistentSource _environmentsPersistentSource;
    private readonly IWindowsApiClient _windowsApiClient;  // ✅ NEW

    public RefDataServersController(
        ISecurityPrivilegesChecker securityPrivilegesChecker,
        IServersPersistentSource serversPersistentSource, 
        IEnvironmentsPersistentSource environmentsPersistentSource,
        IWindowsApiClient windowsApiClient)  // ✅ NEW
    {
        _environmentsPersistentSource = environmentsPersistentSource;
        _serversPersistentSource = serversPersistentSource;
        _securityPrivilegesChecker = securityPrivilegesChecker;
        _windowsApiClient = windowsApiClient;  // ✅ NEW
    }

    [HttpGet]
    [SwaggerResponse(StatusCodes.Status400BadRequest, Type = typeof(string))]
    [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(ServerOperatingSystemApiModel))]
    [Route("GetServerOperatingFromTarget")]
    public async Task<IActionResult> GetServerOperatingFromTarget(string serverName)
    {
        try
        {
            // ✅ Cross-platform - delegates to Windows API when needed
            var output = await _windowsApiClient.GetServerOperatingSystemAsync(serverName);
            return Ok(output);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Failed to get server info: {ex.Message}");
        }
    }
}
```

**Remove the using statement:**
```diff
- using Microsoft.Win32;  // ❌ Remove Windows-only import
+ using System.Runtime.InteropServices;  // ✅ Cross-platform
```

---

### Option 3: SSH-Based Remote Query (Fully Cross-Platform)

**For querying remote servers without Windows dependencies:**

Install package:
```xml
<PackageReference Include="SSH.NET" Version="2024.0.0" />
```

**Implementation:**

```csharp
using Renci.SshNet;

[HttpGet]
[Route("GetServerOperatingFromTarget")]
public async Task<IActionResult> GetServerOperatingFromTarget(
    string serverName, 
    string username, 
    string password)
{
    try
    {
        var output = new ServerOperatingSystemApiModel();
        
        // ✅ Cross-platform - works with Windows (via OpenSSH) and Linux
        using (var client = new SshClient(serverName, username, password))
        {
            client.Connect();
            
            // Detect OS and run appropriate command
            var osTypeCommand = client.RunCommand("uname -s 2>/dev/null || echo Windows");
            var isLinux = !osTypeCommand.Result.Contains("Windows");
            
            if (isLinux)
            {
                // Linux command
                var osInfo = client.RunCommand("cat /etc/os-release");
                // Parse output
                output.ProductName = ParseLinuxOsRelease(osInfo.Result, "PRETTY_NAME");
                output.CurrentVersion = ParseLinuxOsRelease(osInfo.Result, "VERSION_ID");
            }
            else
            {
                // Windows command (requires OpenSSH server on Windows)
                var winVer = client.RunCommand("systeminfo | findstr /B /C:\"OS Name\" /C:\"OS Version\"");
                // Parse output
                var lines = winVer.Result.Split('\n');
                output.ProductName = lines[0].Split(':')[1].Trim();
                output.CurrentVersion = lines[1].Split(':')[1].Trim();
            }
            
            client.Disconnect();
        }
        
        return Ok(output);
    }
    catch (Exception ex)
    {
        return StatusCode(500, $"SSH connection failed: {ex.Message}");
    }
}

private string ParseLinuxOsRelease(string content, string key)
{
    var line = content.Split('\n').FirstOrDefault(l => l.StartsWith(key));
    return line?.Split('=')[1].Trim('"') ?? "Unknown";
}
```

**Benefits:**
- ✅ Fully cross-platform
- ✅ Works with both Windows and Linux targets
- ✅ No Windows Registry dependency
- ⚠️ Requires SSH server on target machines

---

## Migration Steps

### Immediate Quick Win (5 minutes)

1. **Remove local Registry usage** - Replace with `RuntimeInformation`:

```diff
- using Microsoft.Win32;
+ using System.Runtime.InteropServices;

  public IActionResult GetLocalServerInfo()
  {
-     using (var key = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion"))
-     {
-         var productName = key?.GetValue("ProductName")?.ToString();
-         var version = key?.GetValue("CurrentVersion")?.ToString();
-     }
+     var productName = RuntimeInformation.OSDescription;
+     var version = Environment.OSVersion.Version.ToString();
  }
```

**Time**: 5 minutes per file  
**Files affected**: 1 file (`RefDataServersController.cs`)

### Short-term (1-2 hours)

2. **Move remote Registry access to Windows API**:
   - Create new controller in `Dorc.Api.Windows`
   - Update `IWindowsApiClient` interface
   - Modify main API to use proxy pattern

**Time**: 1-2 hours  
**Files affected**: 3 files (1 new, 2 modified)

### Long-term (1 week)

3. **Implement SSH-based alternative**:
   - Add SSH.NET package
   - Create cross-platform remote query
   - Add configuration for SSH credentials

**Time**: 3-5 days  
**Files affected**: 5-7 files

---

## Comparison Matrix

| Approach | Cross-Platform | Remote Query | Complexity | Windows 7 Support | Linux Support |
|----------|----------------|--------------|------------|-------------------|---------------|
| **Registry API (current)** | ❌ No | ✅ Yes | Low | ✅ Yes | ❌ No |
| **RuntimeInformation** | ✅ Yes | ❌ No (local only) | Very Low | ✅ Yes | ✅ Yes |
| **Windows API Proxy** | ✅ Yes* | ✅ Yes | Medium | ✅ Yes | ❌ No** |
| **SSH.NET** | ✅ Yes | ✅ Yes | High | ✅ Yes | ✅ Yes |

*Main API is cross-platform, delegates to Windows API when on Linux  
**Target servers must be Windows

---

## Recommended Approach

**For the Dorc project**, use a hybrid approach:

1. **Local server info**: Use `RuntimeInformation` (immediate)
2. **Remote Windows servers**: Use Windows API proxy pattern (current implementation keeps working)
3. **Future enhancement**: Add SSH support for Linux targets

This approach:
- ✅ Enables Linux deployment immediately
- ✅ Maintains backward compatibility
- ✅ Provides migration path for future
- ✅ Supports Windows 7+ and Linux

---

## Code Example Summary

### Before (Windows-only)
```csharp
using Microsoft.Win32;
var reg = RegistryKey.OpenRemoteBaseKey(RegistryHive.LocalMachine, server);
```

### After (Cross-platform)
```csharp
using System.Runtime.InteropServices;
var osInfo = RuntimeInformation.OSDescription;
```

**Result**: Code runs on Windows, Linux, and macOS with no changes needed.
