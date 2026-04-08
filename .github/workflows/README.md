# DOrc GitHub Actions Workflows

## Release Workflow

The `release.yml` workflow builds and creates artifacts for all DOrc components, matching the functionality of the Azure DevOps pipeline.

### Trigger Events

The workflow runs on:
- **Push** to branches:
  - `main`
  - `develop`
  - `release/**`
  - `feature/**`
  - `fix/**`
  - `hotfix/**`
  - `migration/**`
  - `copilot/**`
- **Pull Requests** to:
  - `main`
  - `develop`

### Build Environment

- **Runner**: `windows-latest`
- **Requirements**:
  - .NET 8.x SDK
  - Node.js 18.x
  - MSBuild
  - WiX Toolset 6.0.1
  - .NET Framework 4.8 (pre-installed on Windows runners)

### Build Process

1. **Setup Phase**
   - Checkout code
   - Install .NET SDK
   - Install Node.js
   - Configure MSBuild
   - Install WiX Toolset

2. **Version Generation**
   - Format: `yy.MM.dd.<run_number>`
   - Example: `24.10.08.42`

3. **Web UI Build**
   - Install npm dependencies in `src/dorc-web`
   - Build web application with `npm run build`

4. **Assembly Versioning**
   - Update `AssemblyInfo.*` files with build version
   - Version PowerShell Cmdlet module

5. **Solution Build**
   - Restore NuGet packages using `pipelines/NuGet.config`
   - Restore .NET dependencies
   - Build entire solution in Release configuration
   - Generate MSI installers using WiX

6. **Testing**
   - Run all test assemblies with VSTest
   - Tests continue on error (non-blocking)

7. **Artifact Collection**
   - Install scripts (*.ps1, *.json)
   - Database files (*.dacpac, *.sql)
   - DOrc MSI installer
   - Test Acceptance MSI installer
   - PowerShell Cmdlet files (*.ps1, *.psm1, *.psd1)

8. **Artifact Publishing**
   - Upload artifacts with name: `dorc-release-<version>`
   - Retention: 90 days

### Differences from Azure DevOps Pipeline

- **Excluded**: SonarQube analysis (on-premise only)
- **Excluded**: File Transform task for test settings (GitHub Actions doesn't require this)
- **Excluded**: Network path publishing (Azure DevOps specific)
- **Added**: GitHub Actions artifact upload with proper retention

### Artifacts Structure

```
artifacts/
├── install-scripts/         # Installation scripts
│   ├── *.ps1
│   └── *.json
├── Database/                # Database deployment files
│   ├── *.dacpac
│   └── *.sql
├── Server/                  # MSI installers
│   ├── Setup.Dorc.msi
│   ├── Setup.Dorc.msi.json
│   ├── Setup.Acceptance.msi
│   └── Setup.Acceptance.msi.json
└── DOrc.Cmdlet/            # PowerShell module
    ├── *.ps1
    ├── *.psm1
    └── *.psd1
```

### Usage

Artifacts are automatically generated and uploaded on every successful build. To download artifacts:

1. Navigate to the Actions tab in GitHub
2. Select the workflow run
3. Download the `dorc-release-<version>` artifact from the artifacts section

### Local Development

To replicate the build locally on Windows:

```powershell
# Install dependencies
choco install dotnet-sdk nodejs-lts msbuild-structured-log-viewer wix

# Build web UI
cd src/dorc-web
npm install
npm run build
cd ../..

# Restore packages
nuget restore src/Dorc.sln -ConfigFile pipelines/NuGet.config
dotnet restore src/Dorc.sln --configfile pipelines/NuGet.config

# Build solution
msbuild src/Dorc.sln /p:Configuration=Release /p:Platform="Any CPU" /p:RunWixToolsOutOfProc=true /p:Version=24.10.08.1
```

### Troubleshooting

**Build fails at MSBuild step:**
- Ensure all project references are correct
- Check that .NET Framework 4.8 is available
- Verify WiX Toolset is properly installed

**Web build fails:**
- Check Node.js version compatibility
- Verify npm dependencies can be resolved
- Review `src/dorc-web/package.json` for script definitions

**MSI creation fails:**
- Verify WiX version matches project requirements (6.0.1)
- Check that all component binaries are built before WiX runs
- Ensure publish profiles are correctly configured

### Maintenance

To update the workflow:
1. Edit `.github/workflows/release.yml`
2. Test changes on a feature branch
3. Monitor the Actions tab for build results
4. Merge to main when validated
