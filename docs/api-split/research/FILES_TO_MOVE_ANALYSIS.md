# Analysis: Files That Can Be Moved from Dorc.Api.Windows to Dorc.Api

This document analyzes the current files in `Dorc.Api.Windows` to determine which ones can be moved back to the main `Dorc.Api` project.

## Summary

**Files that MUST stay in Dorc.Api.Windows**: 14 files (Windows-specific APIs)  
**Files that CAN be moved to Dorc.Api**: 35 files (cross-platform compatible)

---

## Files That MUST Stay in Dorc.Api.Windows (Windows-Only)

These files contain Windows-specific APIs and must remain in the Windows API:

### Controllers (6 files) - All marked with [SupportedOSPlatform("windows")]
1. ✅ **Controllers/DirectorySearchController.cs** - Uses System.DirectoryServices for Active Directory
2. ✅ **Controllers/AccountController.cs** - AD account validation
3. ✅ **Controllers/AccessControlController.cs** - AD group management
4. ✅ **Controllers/ResetAppPasswordController.cs** - Windows impersonation + Registry
5. ✅ **Controllers/BundledRequestsController.cs** - Marked as Windows-only
6. ✅ **Controllers/MakeLikeProdController.cs** - Environment cloning with Windows dependencies

### Identity (4 files) - Active Directory and Windows Authentication
7. ✅ **Identity/DirectorySearchProvider.cs** - Creates ActiveDirectorySearcher (System.DirectoryServices)
8. ✅ **Identity/ClaimsTransformer.cs** - WindowsIdentity transformation
9. ✅ **Identity/CachedUserGroupReader.cs** - Uses IActiveDirectorySearcher
10. ✅ **Identity/UserGroupProvider.cs** - Uses DirectorySearchProvider

### Security (2 files) - Windows Authentication
11. ✅ **Security/WinAuthClaimsPrincipalReader.cs** - Windows claims
12. ✅ **Security/WinAuthLoggingMiddleware.cs** - Windows auth middleware

### Configuration (1 file)
13. ✅ **Configuration/WindowsDependencyRegistry.cs** - Registers Windows-specific services

### Program (1 file)
14. ✅ **Program.cs** - Windows API entry point with Windows auth configuration

**Total: 14 files must stay**

---

## Files That CAN Be Moved to Dorc.Api (Cross-Platform)

These files have NO Windows-specific dependencies and could be moved to the main API:

### Build (3 files) - ✅ Cross-platform build handling
1. **Build/DeployableBuildFactory.cs** - Creates build instances, no Windows APIs
2. **Build/AzureDevOpsDeployableBuild.cs** - Azure DevOps integration, cross-platform
3. **Build/FileShareDeployableBuild.cs** - File share builds, works on Linux

**Recommendation**: ✅ **MOVE to Dorc.Api/Build/**
- These handle build artifact retrieval from Azure DevOps and file shares
- No Windows-specific APIs used
- Can work on Linux with proper file share access

### Deployment (6 files) - ⚠️ Mixed (some should move, some stay)
4. **Deployment/PropertiesService.cs** - Property management, cross-platform
5. **Deployment/PropertyValuesService.cs** - Property values, cross-platform
6. **Deployment/RequestService.cs** - Request orchestration, cross-platform
7. **Deployment/EnvironmentMapper.cs** - Environment mapping, cross-platform
8. **Deployment/FileSystemHelper.cs** - File system operations, cross-platform
9. **Deployment/ManageUsers.cs** - User management logic, cross-platform

**Recommendation**: ⚠️ **REVIEW DEPENDENCIES FIRST**
- These appear cross-platform but may depend on Windows-only controllers
- If they're only used by Windows controllers, keep them in Dorc.Api.Windows
- If they're general-purpose, move to Dorc.Api/Deployment/

### Orchestration (3 files) - ⚠️ Depends on usage
10. **Orchestration/EnvironmentOrchestrator.cs** - Environment operations
11. **Orchestration/ProjectComponentOrchestrator.cs** - Project/component operations
12. **Orchestration/ServiceStatusOrchestrator.cs** - Windows service status

**Recommendation**: ❌ **KEEP in Dorc.Api.Windows**
- Created specifically to replace the generic APIServices
- Used by Windows-specific controllers
- Orchestrator pattern is specific to this API's architecture

### Model (5 files) - ✅ DTOs are cross-platform
13. **Model/BuildDetails.cs** - Build information DTO
14. **Model/DeployRequest.cs** - Deployment request model
15. **Model/MakeLikeProdRequest.cs** - Environment cloning request
16. **Model/MakeLikeProdResponse.cs** - Environment cloning response
17. **Model/AccountGranularity.cs** - Enum for account types

**Recommendation**: ⚠️ **KEEP unless also used in Dorc.Api**
- Models should be in the API that uses them
- If both APIs use them, move to shared ApiModel project
- Current usage is Windows-specific controllers only

### Interfaces (13 files) - ✅ Interfaces are cross-platform
18. **Interfaces/IBuildInterface.cs**
19. **Interfaces/IDeployableBuild.cs**
20. **Interfaces/IPropertiesService.cs**
21. **Interfaces/IPropertyValuesService.cs**
22. **Interfaces/IRequestService.cs**
23. **Interfaces/IEnvironmentMapper.cs**
24. **Interfaces/IFileSystemHelper.cs**
25. **Interfaces/IManageUsers.cs**
26. **Interfaces/IUserGroupReader.cs**
27. **Interfaces/IUserGroupProvider.cs**
28. **Interfaces/IDirectorySearchProvider.cs**
29. **Interfaces/IAuditingManager.cs**
30. **Interfaces/IRequestService.cs** (duplicate?)

**Recommendation**: ❌ **KEEP in Dorc.Api.Windows**
- Interfaces should be in the same project as their implementations
- These define contracts for Windows-specific functionality

### Exceptions (2 files) - ✅ Cross-platform
31. **Exceptions/NonEnoughRightsException.cs** - Custom exception
32. **Exceptions/WrongBuildTypeException.cs** - Custom exception

**Recommendation**: ⚠️ **KEEP unless shared**
- Exceptions should be where they're thrown
- If only Windows API throws them, keep them there

### Infrastructure (1 file) - ✅ Cross-platform
33. **Infrastructure/ExceptionJsonConverter.cs** - JSON converter

**Recommendation**: ✅ **MOVE to Dorc.Api/Infrastructure/** if used in both APIs
- JSON serialization is cross-platform
- Could be shared infrastructure

### Configuration Files (2 files)
34. **appsettings.json** - Windows API configuration
35. **loggerSettings.json** - Logging configuration

**Recommendation**: ✅ **KEEP** - Each API needs its own configuration

### Documentation (1 file)
36. **README.md** - Windows API documentation

**Recommendation**: ✅ **KEEP** - Documents Windows-specific API

---

## Recommended Actions

### ✅ Immediate Moves (Low Risk) - 3 files

Move these to `Dorc.Api` as they're clearly cross-platform and useful for both APIs:

1. **Build/DeployableBuildFactory.cs** → `Dorc.Api/Build/`
2. **Build/AzureDevOpsDeployableBuild.cs** → `Dorc.Api/Build/`
3. **Build/FileShareDeployableBuild.cs** → `Dorc.Api/Build/`

**Rationale:**
- No Windows-specific APIs
- Build artifact retrieval works on Linux
- Useful for the main API deployment functionality

### ⚠️ Conditional Moves (Need Analysis) - 7 files

Move these to `Dorc.Api` ONLY if they're also needed by cross-platform controllers:

4. **Deployment/PropertiesService.cs** → `Dorc.Api/Deployment/` (if used by main API)
5. **Deployment/PropertyValuesService.cs** → `Dorc.Api/Deployment/` (if used by main API)
6. **Deployment/RequestService.cs** → `Dorc.Api/Deployment/` (if used by main API)
7. **Deployment/EnvironmentMapper.cs** → `Dorc.Api/Deployment/` (if used by main API)
8. **Deployment/FileSystemHelper.cs** → `Dorc.Api/Deployment/` (if used by main API)
9. **Deployment/ManageUsers.cs** → `Dorc.Api/Deployment/` (if used by main API)
10. **Infrastructure/ExceptionJsonConverter.cs** → `Dorc.Api/Infrastructure/` (if shared)

**Analysis Required:**
- Check if the main Dorc.Api uses these services
- Review dependencies on Windows-only components
- Verify they don't indirectly depend on Active Directory or Windows auth

### ❌ Do NOT Move (Keep in Windows API) - 25 files

Keep these in `Dorc.Api.Windows`:

- All 6 Controllers (Windows platform-specific)
- All 4 Identity files (Active Directory dependencies)
- All 2 Security files (Windows authentication)
- All 3 Orchestration files (Windows API architecture)
- All 5 Model files (Windows controller DTOs)
- All 13 Interface files (Windows implementation contracts)
- All 2 Exception files (Windows-specific errors)
- Configuration files (Program.cs, WindowsDependencyRegistry.cs)
- Settings files (appsettings.json, loggerSettings.json)
- Documentation (README.md)

---

## Migration Impact Analysis

### If Build Files Are Moved (3 files):

**Benefits:**
- ✅ Main API can retrieve builds directly (reduces Windows API dependency)
- ✅ Linux deployment can fetch Azure DevOps builds
- ✅ Consistent build handling across both APIs

**Risks:**
- ⚠️ Potential duplicate code if Windows API still needs them
- ⚠️ Need to update dependency injection in both APIs

**Effort:** 1-2 hours

### If Deployment Files Are Moved (7 files):

**Benefits:**
- ✅ Reduced duplication if both APIs need deployment logic
- ✅ Centralized business logic

**Risks:**
- ❌ May break Windows API if dependencies aren't properly managed
- ❌ Deployment logic might have hidden Windows dependencies
- ❌ Could complicate the separation of concerns

**Effort:** 4-8 hours + extensive testing

---

## Current State Assessment

**The current split is actually CORRECT for the stated goal:**

> "second API should be the rest of the code/ bare minimum code thats needed for running on windows"

The Windows API contains:
- ✅ Windows-specific controllers (Active Directory, Windows Auth)
- ✅ Supporting services for those controllers
- ✅ Minimal cross-platform code needed to support Windows operations

**Moving files back would:**
- ❌ Create dependencies from main API to Windows-only features
- ❌ Complicate the architecture
- ❌ Risk introducing Windows dependencies into the cross-platform API

---

## Recommendation

### Final Answer: **Move only Build files (3 files)**

**Safest approach:**
1. Move the 3 Build files to Dorc.Api
2. Keep everything else in Dorc.Api.Windows as-is
3. If both APIs need the same deployment logic, create a **shared library** instead

**Better long-term solution:**
- Create `Dorc.Core.Deployment` shared project for deployment logic
- Both APIs reference it
- Keeps separation clean while reducing duplication

**Current state is actually well-designed** - the split clearly separates:
- Cross-platform API (Dorc.Api)
- Windows-only features (Dorc.Api.Windows)
- Communication via HTTP (IWindowsApiClient)

Most files should **stay where they are** to maintain this clean separation.
