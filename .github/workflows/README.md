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

The suite runs under Playwright against **chromium, firefox and webkit, on every
build, on every branch**. The job runs in parallel with `build`, which takes
roughly five times as long, so the full matrix costs nothing in wall-clock.

This was briefly narrowed to chromium on PRs and topic branches. Don't do that
again without a specific reason: it meant pull requests exercised a different
configuration from `main`, and the first bug that hit — an `actions/cache` key
containing a comma, which only the three-engine list produced — passed every PR
check and broke `main` on merge. A CI configuration that varies by branch cannot
tell you whether merging is safe.

`src/dorc-web/vitest.config.ts` still honours a `VITEST_BROWSERS` environment
variable, for local iteration when you don't want to wait on all three:

```bash
VITEST_BROWSERS=chromium npm test
```

CI does not set it. Setting it empty fails loudly rather than silently running
no tests.

### NuGet packages are cached

`~/.nuget/packages` is cached via `actions/cache`. This was removed in #797 on
the grounds that it cost more than it saved — 1m04s to restore plus 10s to save,
against a 27s warm restore — and restored immediately afterwards, because the
measurement that mattered had not been taken: how long an *uncached* restore
takes on this solution.

| Restore | Wall-clock |
| --- | --- |
| Cached (27s restore + 1m04s cache restore + 10s save) | ~1m41s |
| Uncached (run `30751703601`) | 2m04s |
| Uncached (run `30794820988`) | 2m41s |

Caching a Windows NuGet folder is genuinely slow, but a cold restore of 124
packages is slower. Keep the cache.

If this is revisited, the durable improvement is lock files
(`RestorePackagesWithLockFile`) so the key stops hashing `**/*.csproj` and
surviving a project-file edit.

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
│   ├── Setup.Dorc.Api.msi
│   ├── Setup.Dorc.Api.msi.json
│   ├── Setup.Dorc.Web.msi
│   ├── Setup.Dorc.Web.msi.json
│   ├── Setup.Dorc.Monitors.msi
│   ├── Setup.Dorc.Monitors.msi.json
│   ├── Setup.Dorc.Cli.msi
│   ├── Setup.Dorc.Cli.msi.json
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
