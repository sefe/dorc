# DOrc GitHub Actions Workflows

## Release Workflow

The `release.yml` workflow builds and creates artifacts for all DOrc components, matching the functionality of the Azure DevOps pipeline.

### Trigger Events

The workflow runs on:
- **Push** to branches:
  - `main`
  - `develop`
  - `release/**`
- **Pull Requests** to:
  - `main`
  - `develop`

Topic branches (`feature/**`, `fix/**`, `hotfix/**`, `migration/**`, `copilot/**`,
`claude/**`) are covered by the pull request trigger — open a PR to get a build.
They are deliberately not listed under **Push**: while they were, a push to a
branch with an open PR started two full builds of the same commit.

Runs are grouped per PR (or per branch for direct pushes) and an in-flight run is
cancelled when a newer commit arrives. Runs on `main` and `release/**` are exempt
from cancellation because they publish the artifacts consumed downstream.

### Jobs

The workflow runs two jobs in parallel:

| Job | Purpose |
| --- | --- |
| `build` | Web UI build, .NET solution build, .NET tests, MSI installers, artifacts |
| `web-tests` | The `dorc-web` vitest suite |

They are independent: the MSI harvests `dorc-web\dist`, which `build` produces
itself, so the web suite gates nothing downstream and does not need to sit on the
critical path.

> **Branch protection:** both `build` and `web-tests` need to be required checks.
> A rule that only requires `build` will no longer see web test failures.

### Build Environment

- **Runner**: `windows-latest`
- **Requirements**:
  - .NET 8.x SDK — used as pre-installed on the runner image. There is no
    `actions/setup-dotnet` step; installing a second copy of the SDK cost ~38s
    per run. The `Report .NET toolchain` step prints `dotnet --list-sdks` and
    `--list-runtimes` so an image change that breaks this is visible in the log.
  - Node.js 20.x
  - MSBuild
  - WiX Toolset 6.0.1
  - .NET Framework 4.8 (pre-installed on Windows runners)

### Build Process (`build` job)

1. **Setup Phase**
   - Checkout code
   - Report the runner's .NET toolchain
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
   - Restore .NET dependencies using `pipelines/NuGet.config`
   - Build entire solution in Release configuration, with `/m` so independent
     projects compile in parallel
   - Generate MSI installers using WiX

6. **Testing**
   - Run all test assemblies with `dotnet test`
   - Tests continue on error, and are only gated on `main` / `release/**`

7. **Artifact Collection**
   - Install scripts (*.ps1, *.json)
   - Database files (*.dacpac, *.sql)
   - DOrc MSI installer
   - Test Acceptance MSI installer
   - PowerShell Cmdlet files (*.ps1, *.psm1, *.psd1)

8. **Artifact Publishing**
   - Upload artifacts with name: `dorc-release-<version>`
   - Retention: 400 days on `main` / `release/**`, 14 days elsewhere

### Web Tests (`web-tests` job)

The suite runs under Playwright. Which engines it runs against depends on the
branch, via the `VITEST_BROWSERS` environment variable read by
`src/dorc-web/vitest.config.ts`:

| Ref | Engines |
| --- | --- |
| `main`, `develop`, `release/**` | chromium, firefox, webkit |
| Pull requests and everything else | chromium |

Running all three engines on every PR meant three browser downloads on any cache
miss for little extra signal. Locally, `npm test` with `VITEST_BROWSERS` unset
still runs all three; set it to reproduce a CI run — `VITEST_BROWSERS=chromium
npm test`.

The Playwright browser cache is keyed on both the Playwright version and the
engine list, so a chromium-only cache entry is never reused for a run that needs
all three.

### NuGet packages are not cached

There is intentionally no `actions/cache` step for `~/.nuget/packages`. Caching it
on Windows means moving tens of thousands of small files: measured over run
`30527995114` it cost 1m04s to restore and 10s to save, in order to speed up a
restore that takes 27s warm. The key also hashed `**/*.csproj`, so any project
file edit paid the full cost for no hit.

### Profiling the build

Run the workflow manually (**Actions → DOrc Build → Run workflow**) with
**Capture an MSBuild binary log** ticked. The build then runs with `/bl` and
uploads `build-binlog-<version>`, readable with the
[MSBuild Structured Log Viewer](https://msbuildlog.com/), to see which targets
dominate build time.

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
dotnet restore src/Dorc.sln --configfile pipelines/NuGet.config

# Build solution (/m builds independent projects in parallel)
msbuild src/Dorc.sln /p:Configuration=Release /p:Platform="Any CPU" /p:RunWixToolsOutOfProc=true /p:Version=24.10.08.1 /m
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
