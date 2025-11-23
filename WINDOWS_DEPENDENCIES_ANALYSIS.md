# Windows Dependencies Analysis

This document provides a comprehensive analysis of all Windows-specific dependencies in the Dorc codebase, their purpose, potential alternatives, and recommendations for cross-platform migration.

## Summary

The Dorc codebase has **4 major categories** of Windows dependencies affecting **30+ files** across multiple projects.

---

## 1. Active Directory (AD) / LDAP Dependencies

### NuGet Packages
- **System.DirectoryServices** (v9.0.10)
  - Used in: `Dorc.Core`, `Dorc.PersistentData`, `Dorc.Api.Tests`
  - **Status**: Windows-only, no Linux support

- **System.DirectoryServices.AccountManagement** (v9.0.10)
  - Used in: `Dorc.Core`, `Dorc.PersistentData`
  - **Status**: Windows-only, no Linux support

### Primary Usage
1. **User/Group Search** (`ActiveDirectorySearcher.cs`)
   - Search users and groups by name
   - Get user details by SID
   - LDAP queries for user attributes (displayName, mail, SAMAccountName, objectSid)

2. **Authentication & Authorization**
   - Validate group membership
   - Check if user belongs to specific AD groups
   - Recursive group membership checks

3. **User Management**
   - Get all SIDs for a user (including nested groups)
   - Check account status (enabled/disabled)

### Files Using AD APIs (23 files)
```
Dorc.Api/Controllers/DirectorySearchController.cs
Dorc.Api/Controllers/AccessControlController.cs
Dorc.Api/Controllers/ResetAppPasswordController.cs
Dorc.Api/Services/ApiRegistry.cs
Dorc.Api/Services/CachedUserGroupReader.cs
Dorc.Api/Services/ClaimsTransformer.cs
Dorc.Api/Services/DirectorySearcherFactory.cs
Dorc.Core/ActiveDirectorySearcher.cs
Dorc.Core/CompositeDirectorySearcher.cs
Dorc.PersistentData/Sources/UsersPermissionsPersistentSource.cs
[+ 13 more files in tests and Windows API]
```

### **Alternatives & Recommendations**

#### ✅ **Option 1: Azure Entra ID / Microsoft Graph (RECOMMENDED)**
- **Status**: Already partially implemented in `AzureEntraSearcher.cs`
- **Packages**: `Microsoft.Graph` (v5.95.0), `Azure.Identity` (v1.17.0)
- **Cross-platform**: ✅ Yes (works on Linux)
- **Capabilities**:
  - User/group search via Microsoft Graph API
  - Group membership validation
  - User attribute retrieval
- **Migration Path**:
  1. Extend `AzureEntraSearcher` to replace all `ActiveDirectorySearcher` functionality
  2. Update `IActiveDirectorySearcher` interface implementations
  3. Switch DI registration to use Azure Entra by default
  4. Keep AD as fallback for on-premises deployments

**Example Implementation:**
```csharp
// Already exists - expand functionality
public class AzureEntraSearcher : IActiveDirectorySearcher
{
    private readonly GraphServiceClient _graphClient;
    
    public async Task<List<UserElementApiModel>> Search(string objectName)
    {
        var users = await _graphClient.Users
            .GetAsync(req => req.QueryParameters.Search = $"\"displayName:{objectName}\"");
        // Map to UserElementApiModel
    }
}
```

#### ✅ **Option 2: Novell.Directory.Ldap**
- **Package**: `Novell.Directory.Ldap.NETStandard` (v3.6.0+)
- **Cross-platform**: ✅ Yes (works on Linux)
- **Capabilities**:
  - Direct LDAP queries (works with AD and other LDAP servers)
  - User/group search
  - Attribute retrieval
- **Pros**: Works with on-premises AD without Azure Entra
- **Cons**: Requires direct LDAP access, more complex setup

**Example Implementation:**
```csharp
using Novell.Directory.Ldap;

public class LdapDirectorySearcher : IActiveDirectorySearcher
{
    public List<UserElementApiModel> Search(string objectName)
    {
        using var conn = new LdapConnection();
        conn.Connect("ldap.company.com", 389);
        conn.Bind("cn=admin,dc=company,dc=com", "password");
        
        var search = conn.Search(
            "dc=company,dc=com",
            LdapConnection.ScopeSub,
            $"(&(objectClass=user)(cn=*{objectName}*))",
            new[] { "displayName", "mail", "sAMAccountName" },
            false);
        // Process results
    }
}
```

#### ⚠️ **Option 3: Keep Windows-only (Current Implementation)**
- Windows API runs on Windows server with AD access
- Linux API forwards AD requests via HTTP to Windows API
- **Status**: ✅ Already implemented in `Dorc.Api.Windows`

---

## 2. Windows Authentication & Identity

### Windows-Specific APIs
- **WindowsIdentity** (15 files)
- **SecurityIdentifier** / **NTAccount**
- **Windows Impersonation**

### Primary Usage
1. **Windows Authentication**
   - `WinAuthClaimsPrincipalReader.cs` - Read Windows user claims
   - `ClaimsTransformer.cs` - Transform Windows identity to app claims
   - Negotiate/Kerberos authentication

2. **Password Reset with Impersonation** (`ResetAppPasswordController.cs`)
   - Uses `LogonUser` P/Invoke
   - `WindowsIdentity.RunImpersonated()` for SQL password reset
   - Requires Windows domain credentials

3. **Security Context**
   - Convert between SID and NTAccount formats
   - Get user's group SIDs for authorization

### **Alternatives & Recommendations**

#### ✅ **Option 1: OAuth2 / JWT Bearer (RECOMMENDED)**
- **Status**: ✅ Already implemented in `Dorc.Api`
- **Packages**: `Microsoft.AspNetCore.Authentication.JwtBearer` (v8.0.13)
- **Cross-platform**: ✅ Yes
- **Migration Path**: Already supports OAuth2 as alternative to Windows Auth
  - Configuration: Set `AuthenticationScheme` to `"OAuth"` or `"Both"`

#### ✅ **Option 2: Azure AD / Entra ID Authentication**
- Uses OAuth2/OIDC tokens
- Works cross-platform
- Integrates with Microsoft Graph for user info

#### ⚠️ **Option 3: External Authentication Service**
- For password reset: Use Azure Key Vault or HashiCorp Vault
- For impersonation: Replace with service account with proper RBAC

**Password Reset Alternative:**
```csharp
// Instead of Windows impersonation, use service principal
public class SqlPasswordResetService
{
    public async Task<ApiBoolResult> ResetPassword(string username, string serverName)
    {
        // Use service account with SQL permissions
        // Or use Azure Key Vault for credential management
        var credentials = await _keyVaultClient.GetSecretAsync("sql-admin-credentials");
        // Reset password without impersonation
    }
}
```

---

## 3. WMI (Windows Management Instrumentation)

### NuGet Packages
- **System.Management** (v9.0.10)
  - Used in: `Dorc.Runner`, `Dorc.TerraformRunner`
  - **Status**: Windows-only

### Primary Usage
1. **Server Operations** (`WmiUtil.cs`)
   - Remote server reboot
   - Check Windows services status
   - Query OS version

2. **Service Status Checks** (`ServiceStatus.cs`)
   - Check if Windows services are running
   - Get service state

### Files Using WMI (4 files)
```
Dorc.Api/Services/WmiUtil.cs
Dorc.Core/ServiceStatus.cs
Dorc.Runner/Program.cs
Dorc.TerraformRunner/Program.cs
```

### **Alternatives & Recommendations**

#### ✅ **Option 1: SSH / PowerShell Remoting (RECOMMENDED)**
- **Package**: `SSH.NET` or `System.Management.Automation` (cross-platform version)
- **Cross-platform**: ✅ Yes
- **Capabilities**:
  - Remote command execution via SSH
  - PowerShell Core remoting (works on Linux)

**Example Implementation:**
```csharp
using Renci.SshNet;

public class SshServerManager
{
    public async Task RebootServer(string serverName)
    {
        using var client = new SshClient(serverName, "username", "password");
        client.Connect();
        var result = client.RunCommand("sudo reboot");
        client.Disconnect();
    }
}
```

#### ✅ **Option 2: REST API / Agent-Based**
- Deploy lightweight agent on target servers
- Expose REST API for operations
- Works across all platforms

#### ⚠️ **Option 3: Platform-Specific Implementation**
- Windows: Use WMI
- Linux: Use systemd/systemctl commands
- Runtime detection with conditional execution

---

## 4. Windows Registry

### Windows-Specific APIs
- **Microsoft.Win32.Registry** / **RegistryKey**

### Primary Usage
1. **OS Version Detection** (`RefDataServersController.cs`)
   - Read `Software\Microsoft\Windows NT\CurrentVersion`
   - Get Windows build number and version

### Files Using Registry (21 files - mostly tests and references)
```
Dorc.Api/Controllers/RefDataServersController.cs
[+ 20 files in tests]
```

### **Alternatives & Recommendations**

#### ✅ **Option 1: Runtime Information API (RECOMMENDED)**
- **Package**: Built-in `System.Runtime.InteropServices.RuntimeInformation`
- **Cross-platform**: ✅ Yes

**Example Implementation:**
```csharp
using System.Runtime.InteropServices;

public string GetOSVersion()
{
    return RuntimeInformation.OSDescription;
    // Returns: "Microsoft Windows 10.0.19044" or "Linux 5.15.0-1"
}

public string GetOSArchitecture()
{
    return RuntimeInformation.OSArchitecture.ToString();
    // Returns: "X64", "Arm64", etc.
}
```

#### ✅ **Option 2: Environment Variables**
```csharp
var osVersion = Environment.OSVersion.VersionString;
var platform = Environment.OSVersion.Platform;
```

---

## 5. PowerShell Dependencies

### NuGet Packages
- **System.Management.Automation** (v7.4.13 in `Dorc.PowerShell`)
- **Microsoft.PowerShell.SDK** (v7.5.4 in `Dorc.Core`)

### Status
- ✅ **PowerShell 7+ is cross-platform**
- `Microsoft.PowerShell.SDK` works on Linux/macOS
- Only `.NetFramework.PowerShell` projects are Windows-only

### Recommendation
- ✅ **Already cross-platform compatible** via PowerShell 7+
- Keep using `Microsoft.PowerShell.SDK` (v7.5.4)
- Deprecate `Dorc.NetFramework.PowerShell` and `Dorc.NetFramework.Runner`

---

## Migration Priority & Roadmap

### Phase 1: Quick Wins (Low Effort, High Impact)
1. ✅ **Replace Registry API** with `RuntimeInformation` - 1 file
2. ✅ **Standardize on OAuth2** instead of Windows Auth - Already supported
3. ✅ **Document Azure Entra** as AD alternative - Partially implemented

### Phase 2: Medium Effort
4. **Expand AzureEntraSearcher** to full AD replacement
   - Implement all `IActiveDirectorySearcher` methods
   - Add configuration option to choose AD provider
   - Files affected: ~10

5. **Replace WMI with SSH/REST**
   - Implement cross-platform server management
   - Files affected: ~4

### Phase 3: Long-term (Complex Migration)
6. **Refactor password reset** to use service accounts
   - Remove Windows impersonation dependency
   - Use Azure Key Vault or similar
   - Files affected: 1-2

7. **Implement LDAP alternative** for on-premises without Azure
   - Use `Novell.Directory.Ldap`
   - Provide configuration-based switching
   - Files affected: ~10

---

## Cross-Platform Compatibility Matrix

| Component | Current Status | Linux Compatible? | Recommended Alternative |
|-----------|----------------|-------------------|------------------------|
| **Active Directory** | System.DirectoryServices | ❌ No | ✅ Microsoft Graph (Azure Entra) or Novell.Directory.Ldap |
| **Windows Auth** | Negotiate/NTLM | ❌ No | ✅ OAuth2/JWT (already implemented) |
| **WMI** | System.Management | ❌ No | ✅ SSH.NET or REST API |
| **Registry** | Microsoft.Win32 | ❌ No | ✅ RuntimeInformation (built-in) |
| **PowerShell** | PowerShell 7+ | ✅ Yes | ✅ Keep current (already compatible) |
| **Impersonation** | WindowsIdentity | ❌ No | ⚠️ Service accounts with RBAC |

---

## Dependency Removal Impact

### Projects Affected by Full Windows Dependency Removal:
1. **Dorc.Core** - Contains `ActiveDirectorySearcher` (can be replaced)
2. **Dorc.PersistentData** - Uses AD for user lookups (can be abstracted)
3. **Dorc.Api** - Windows-specific controllers (✅ already moved to Dorc.Api.Windows)
4. **Dorc.Runner** - WMI for service management (needs SSH alternative)

### Projects That Can Be Deprecated:
1. **Dorc.NetFramework.PowerShell** - Replaced by PowerShell 7+
2. **Dorc.NetFramework.Runner** - Can be migrated to .NET 8

---

## Recommended Action Items

### Immediate (Next Sprint)
1. ✅ Document current Windows dependencies (this document)
2. Replace `Registry` usage with `RuntimeInformation` in `RefDataServersController`
3. Add configuration option to choose AD provider (ActiveDirectory vs AzureEntra vs LDAP)

### Short-term (1-2 Sprints)
4. Complete `AzureEntraSearcher` implementation for all AD operations
5. Create abstraction layer for server management (WMI vs SSH)
6. Add feature flags for gradual migration

### Long-term (3-6 months)
7. Implement `Novell.Directory.Ldap` as fallback for on-premises
8. Deprecate .NET Framework projects
9. Migrate all authentication to OAuth2/JWT
10. Replace Windows impersonation with service principal pattern

---

## Configuration Example for Flexible AD Provider

```json
{
  "AppSettings": {
    "DirectoryProvider": "AzureEntra",  // Options: "ActiveDirectory", "AzureEntra", "Ldap"
    "AzureEntra": {
      "TenantId": "...",
      "ClientId": "...",
      "ClientSecret": "..."
    },
    "Ldap": {
      "Server": "ldap.company.com",
      "Port": 389,
      "BaseDn": "dc=company,dc=com",
      "Username": "cn=admin,dc=company,dc=com"
    }
  }
}
```

---

## Conclusion

The Dorc codebase has **significant but manageable** Windows dependencies. Key findings:

1. **23 files** use Active Directory APIs - Can be replaced with Microsoft Graph or LDAP
2. **15 files** use Windows Identity - OAuth2 already available as alternative  
3. **4 files** use WMI - Can be replaced with SSH or REST APIs
4. **PowerShell is already cross-platform** via PowerShell 7+

**Current Split Architecture** (Dorc.Api + Dorc.Api.Windows) is a good interim solution that:
- ✅ Enables immediate Linux deployment of core functionality
- ✅ Keeps Windows-specific features working
- ✅ Provides path for gradual migration

**Recommended Next Steps:**
1. Replace Registry API usage (1 hour)
2. Expand Azure Entra implementation (2-3 days)
3. Create configuration-based provider switching (1-2 days)
4. Implement SSH-based server management (3-5 days)

Total effort for full cross-platform migration: **2-4 weeks** of focused development.
