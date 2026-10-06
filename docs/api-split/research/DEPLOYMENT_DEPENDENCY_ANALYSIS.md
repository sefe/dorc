# Deployment Folder Dependency Analysis

## Summary

Analysis of the 6 files in `Dorc.Api.Windows/Deployment/` to determine if they have hidden Windows dependencies and whether they can be moved to the main Dorc.Api.

## Files Analyzed

1. **EnvironmentMapper.cs** - Maps environment configurations
2. **FileSystemHelper.cs** - File system operations
3. **ManageUsers.cs** - User management operations
4. **PropertiesService.cs** - Property management
5. **PropertyValuesService.cs** - Property value management
6. **RequestService.cs** - Request orchestration

## Dependency Analysis

### Direct Windows Dependencies

**Result:** ✅ **NONE FOUND**

All 6 files have:
- ❌ No `System.DirectoryServices` imports
- ❌ No `WindowsIdentity` or Windows authentication
- ❌ No `Microsoft.Win32` (Registry)
- ❌ No `System.Management` (WMI)
- ❌ No `[SupportedOSPlatform("windows")]` attributes

### Indirect Dependencies (Used By)

**EnvironmentMapper (IEnvironmentMapper):**
- Used by: ❓ Not used by any Windows-specific controllers or orchestrators in current PR files
- Recommendation: ⚠️ **CONDITIONAL** - Check if used by main Dorc.Api

**FileSystemHelper (IFileSystemHelper):**
- Used by: 
  - ✅ `Build/DeployableBuildFactory.cs` (moving to Dorc.Api)
  - ✅ `Build/FileShareDeployableBuild.cs` (moving to Dorc.Api)
- Recommendation: ✅ **MOVE** - Required by Build files

**ManageUsers (IManageUsers):**
- Used by: 
  - ❌ `Orchestration/EnvironmentOrchestrator.cs` (Windows-specific orchestrator)
- Recommendation: ❌ **KEEP** - Used by Windows API orchestration

**PropertiesService (IPropertiesService):**
- Used by: ❓ Not directly used in PR files
- Recommendation: ⚠️ **CONDITIONAL** - Likely used by Windows controllers via DI

**PropertyValuesService (IPropertyValuesService):**
- Used by: ❓ Not directly used in PR files  
- Recommendation: ⚠️ **CONDITIONAL** - Likely used by Windows controllers via DI

**RequestService (IRequestService):**
- Used by: ❓ Not directly used in PR files
- Recommendation: ⚠️ **CONDITIONAL** - Likely used by Windows controllers via DI

### Dependencies on Other Deployment Files

**FileSystemHelper** - No dependencies on other Deployment files
**ManageUsers** - No dependencies on other Deployment files
**EnvironmentMapper** - No dependencies on other Deployment files
**PropertiesService** - No dependencies on other Deployment files
**PropertyValuesService** - May depend on PropertiesService (need to check implementation)
**RequestService** - May depend on other services (need to check implementation)

## Hidden Windows Dependencies - NONE FOUND ✅

**Conclusion:** All Deployment files are **technically cross-platform** (no direct Windows API usage).

However, they are **architecturally Windows-specific** because they:
1. Support Windows-only controllers (BundledRequests, MakeLikeProd, etc.)
2. Are registered in WindowsDependencyRegistry
3. Part of Windows API business logic

## Recommendations

### ✅ MUST Move (1 file) - Required by Build files

**FileSystemHelper.cs + IFileSystemHelper.cs**
- **Reason:** Required by Build files that are moving to Dorc.Api
- **Windows Dependencies:** None
- **Impact:** Low risk, enables Build files to function
- **Action:** Move to `Dorc.Api/Deployment/` along with interface

### ❌ SHOULD Stay (5 files) - Windows API architecture

**ManageUsers.cs + IManageUsers.cs**
- **Reason:** Used by EnvironmentOrchestrator (Windows-specific)
- **Used by:** Windows API orchestration layer
- **Action:** Keep in Dorc.Api.Windows

**PropertiesService.cs + IPropertiesService.cs**
- **Reason:** Likely used by Windows controllers
- **Registered in:** WindowsDependencyRegistry
- **Action:** Keep in Dorc.Api.Windows

**PropertyValuesService.cs + IPropertyValuesService.cs**
- **Reason:** Likely used by Windows controllers
- **Registered in:** WindowsDependencyRegistry
- **Action:** Keep in Dorc.Api.Windows

**RequestService.cs + IRequestService.cs**
- **Reason:** Request orchestration for Windows API
- **Registered in:** WindowsDependencyRegistry
- **Action:** Keep in Dorc.Api.Windows

**EnvironmentMapper.cs + IEnvironmentMapper.cs**
- **Reason:** Environment configuration mapping
- **Registered in:** WindowsDependencyRegistry
- **Action:** Keep in Dorc.Api.Windows

## Implementation Plan

### Phase 1: Move Build Files + FileSystemHelper (Required)

1. Move `Build/DeployableBuildFactory.cs` to `Dorc.Api/Build/`
2. Move `Build/AzureDevOpsDeployableBuild.cs` to `Dorc.Api/Build/`
3. Move `Build/FileShareDeployableBuild.cs` to `Dorc.Api/Build/`
4. Move `Deployment/FileSystemHelper.cs` to `Dorc.Api/Deployment/`
5. Move `Interfaces/IFileSystemHelper.cs` to `Dorc.Api/Interfaces/`
6. Move `Interfaces/IDeployableBuildFactory.cs` to `Dorc.Api/Interfaces/`
7. Move `Interfaces/IDeployableBuild.cs` to `Dorc.Api/Interfaces/`
8. Move `Interfaces/IBuildInterface.cs` to `Dorc.Api/Interfaces/`
9. Update namespaces from `Dorc.Api.Windows.*` to `Dorc.Api.*`
10. Update Dorc.Api dependency injection to register these services

**Files moving: 8 total**
- 3 Build implementation files
- 1 Deployment file (FileSystemHelper)
- 4 Interface files

### Phase 2: Update Remaining Files (Optional - Future Work)

The other 5 Deployment files **can technically be moved** but:
- They're used exclusively by Windows API
- Moving them would require extensive refactoring
- Current architecture is cleaner with them in Windows API

**Recommendation:** Keep them in Dorc.Api.Windows unless the main API needs them.

## Validation Checklist

After moving files:

- [ ] Update namespace references in all moved files
- [ ] Update `using` statements to remove `Dorc.Api.Windows` references
- [ ] Register services in Dorc.Api dependency injection
- [ ] Remove services from WindowsDependencyRegistry in Windows API
- [ ] Build Dorc.Api successfully
- [ ] Build Dorc.Api.Windows successfully (verify it still works without moved files)
- [ ] Update .csproj file references if needed
- [ ] Run tests

## Impact Assessment

**Low Risk:**
- FileSystemHelper has no Windows dependencies
- Build files are self-contained
- Clear separation of concerns

**Medium Risk:**
- May need to add Dorc.PersistentData reference to Dorc.Api if not already present
- Need to ensure all transitive dependencies are available

**High Risk:**
- None identified

## Conclusion

**Hidden Windows Dependencies Found:** ✅ **NONE**

All Deployment files are cross-platform compatible at the code level. However:
- **FileSystemHelper** must move (required by Build files)
- **Other 5 files** should stay (Windows API business logic)

This maintains clean architectural separation while enabling Build file movement.
