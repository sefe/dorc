# DOrc (DevOps Deployment Orchestrator)

DOrc is a DevOps engine for running PowerShell deployments while managing environments and configuration. It consists of a .NET 8 API backend, TypeScript/Lit-element web UI with Vaadin components, multiple CLI tools, and PowerShell cmdlets.

Always reference these instructions first and fallback to search or bash commands only when you encounter unexpected information that does not match the info here.

## Working Effectively

### Prerequisites
- **REQUIRED**: .NET 8.0 SDK (version 8.0.119+ confirmed working)
- **REQUIRED**: Node.js >=14.0.0 and npm >=7.0.0 (Node.js v20.19.5 and npm v10.8.2 confirmed working)
- **Optional**: .NET Framework 4.8 Developer Pack (for Windows-specific projects)
- **Optional**: WiX 5 toolset (for MSI installer projects)
- **Optional**: SQL Server Data Tools (for database projects)

### Bootstrap and Build
Bootstrap the entire solution:
- Navigate to repository root: `cd /path/to/dorc`
- **Web UI dependencies**: `cd src/dorc-web && npm install` -- takes 30 seconds. NEVER CANCEL.
- **Web UI build**: `npm run build` -- takes 20 seconds. NEVER CANCEL. 
- **.NET packages restore**: `cd ../src && dotnet restore Dorc.sln` -- takes 60 seconds. NEVER CANCEL. Set timeout to 120+ seconds.
- **Core .NET projects build**: Individual projects build in 1-3 seconds each.

### Building Components

**Web UI (TypeScript/Lit-element)**:
- Location: `src/dorc-web/`
- Install: `npm install` (30 seconds)
- Build: `npm run build` (20 seconds) 
- Dev server: `npm run dev` (starts on http://localhost:8888)
- Format code: `npm run format`
- Type checking: `npm run type-checking`

**.NET 8 Projects (Core functionality)**:
- Location: `src/`
- Restore: `dotnet restore Dorc.sln` (60 seconds)
- **Working .NET 8 projects** (build individually in 1-3 seconds):
  - API: `dotnet build Dorc.Api/Dorc.Api.csproj --configuration Release`
  - Monitor: `dotnet build Dorc.Monitor/Dorc.Monitor.csproj --configuration Release`
  - Runner: `dotnet build Dorc.Runner/Dorc.Runner.csproj --configuration Release`
  - Core: `dotnet build Dorc.Core/Dorc.Core.csproj --configuration Release`
  - All CLI tools: `Tools.RequestCLI`, `Tools.PropertyValueCreationCLI`, `Tools.DeployCopyEnvBuildCLI`, `Tools.PostRestoreEndurCLI`
- **WARNING**: Full solution build `dotnet build Dorc.sln` WILL FAIL on Linux due to .NET Framework and WiX dependencies

**Problematic Projects (Linux limitations)**:
- `Dorc.Database` - SQL Server Data Tools project, cannot build on Linux
- `Setup.Dorc` and `Setup.Acceptance` - WiX 5 installer projects, Windows-only 
- `Dorc.NetFramework.*` - .NET Framework 4.8 projects, require Windows
- `Dorc.Api.Tests` - Missing test framework components (System.DirectoryServices.Fakes)

### Running Tests
- **Core tests**: `dotnet test Dorc.Core.Tests/Dorc.Core.Tests.csproj --configuration Release` -- takes 8 seconds. NEVER CANCEL.
- **Integration tests**: `Dorc.Monitor.IntegrationTests` (may require additional configuration)
- **Acceptance tests**: `Tests.Acceptance` using SpecFlow (may require test environment setup)

### Running the Application

**Development Mode**:
1. **Start Web UI**: `cd src/dorc-web && npm run dev` (available at http://localhost:8888)
2. **Start API**: Requires configuration setup (see Configuration Requirements below)

**API Configuration Requirements**:
The API (`Dorc.Api`) requires several configurations to run:
- Database connection string (`DOrcConnectionString`) 
- OpenSearch settings (`ConnectionUri`, `UserName`, `Password`)
- Authentication settings (Azure AD, OAuth2, or Windows Auth)
- **Without proper configuration, API will fail to start with configuration errors**

## Validation

**Always run these validation steps after making changes**:
1. **Web UI**: `cd src/dorc-web && npm run build` (20 seconds)
2. **Core .NET**: `dotnet build Dorc.Api/Dorc.Api.csproj --configuration Release` (2 seconds)
3. **Tests**: `dotnet test Dorc.Core.Tests/Dorc.Core.Tests.csproj --configuration Release` (8 seconds)
4. **Format check**: `cd src/dorc-web && npm run format`

**Manual Testing Scenarios**:
- **Web UI**: Start `npm run dev` and verify it loads at http://localhost:8888 (confirmed working)
- **API**: Cannot fully test without database and OpenSearch configuration - will fail with "OpenSearchSettings.ConnectionUri not set" error
- **CLI Tools**: All .NET 8 CLI tools build successfully but require proper arguments to run
- **Type checking**: `npm run type-checking` works but shows numerous linting warnings (expected)

## Project Structure

### Key Projects
- **`Dorc.Api`** - Main REST API (.NET 8)
- **`Dorc.Monitor`** - Deployment monitoring service (.NET 8)  
- **`Dorc.Runner`** - Deployment execution engine (.NET 8)
- **`Dorc.Core`** - Core business logic (.NET 8)
- **`dorc-web`** - Web UI (TypeScript, Lit-element, Vaadin)
- **`Tools.*`** - Various CLI tools for deployment tasks
- **`Tools.DOrc.Cmdlet`** - PowerShell cmdlets module

### Important Files  
- **`src/Dorc.sln`** - Main solution file
- **`src/dorc-web/package.json`** - Web UI dependencies and scripts
- **`pipelines/dorc-build.yml`** - Azure DevOps build pipeline
- **`src/dorc-web/src/apis/`** - Generated API clients (TypeScript)

## Common Tasks

### Generate API Clients
- **DOrc API**: `cd src/dorc-web && npm run dorc-api-gen`
- **Azure DevOps API**: Uses OpenAPI generator with build.json spec

### Code Quality
- **Lint Web UI**: ESLint runs automatically during build
- **Format Web UI**: `npm run format` 
- **Type Checking**: `npm run type-checking` and `npm run lit:type-checking`

### Development Workflow
1. Make changes to .NET projects or web UI
2. Build affected components (web: 20s, .NET: 2s per project)
3. Run tests (8s for core tests)
4. Test in development mode (web UI at localhost:8888)
5. **Always** run format and validation before committing

### Timing Expectations
- **npm install**: 30 seconds - NEVER CANCEL
- **npm run build**: 20 seconds - NEVER CANCEL  
- **dotnet restore**: 60 seconds - NEVER CANCEL, set timeout 120+ seconds
- **dotnet build** (individual project): 1-3 seconds
- **dotnet test**: 8 seconds - NEVER CANCEL
- **npm run dev**: Starts in 1-2 seconds
- **npm run format**: 5 seconds - NEVER CANCEL
- **npm run type-checking**: 13 seconds - NEVER CANCEL (may show linting issues)

### Platform Limitations
- **Full builds only work on Windows** due to .NET Framework, WiX, and SQL Server dependencies
- **Linux/macOS**: Can build and run core .NET 8 projects and web UI only
- **Database projects**: Require Visual Studio or SQL Server Data Tools (Windows only)
- **MSI installers**: Require WiX toolset (Windows only)

### Authentication & Configuration
- Supports multiple authentication schemes: OAuth2, Azure AD, Windows Authentication
- Requires external dependencies: SQL Server database, OpenSearch cluster
- **Development setup requires proper configuration files** - API will not start without valid settings
- Configuration templates available in `src/install-scripts/`

## Repository Outputs

### Repository root listing
```
.git
.gitattributes  
.gitignore
CONTRIBUTING.md
LICENSE.md
README.md
pipelines/
src/
```

### Source directory listing  
```
src/
  Dorc.Api/                    # Main REST API (.NET 8)
  Dorc.Api.Tests/             # API unit tests  
  Dorc.Monitor/               # Deployment monitoring (.NET 8)
  Dorc.Runner/                # Deployment execution (.NET 8)
  Dorc.Core/                  # Core business logic (.NET 8)
  dorc-web/                   # Web UI (TypeScript/Lit)
  Tools.RequestCLI/           # CLI deployment tool
  Tools.DOrc.Cmdlet/          # PowerShell cmdlets
  Dorc.Database/              # SQL Server database project
  Setup.Dorc/                 # MSI installer (Windows)
  [other projects...]
```