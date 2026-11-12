# S-001 Toolchain Record — 2026-07-17 (remote Linux dev environment)

| # | Check | Result | Evidence / notes |
|---|-------|--------|------------------|
| 1 | Backend builds | **YES** (with platform caveat) | .NET 8 SDK 8.0.423 installed via dotnet-install. All net8.0 projects build (Dorc.Api, Dorc.Core, Dorc.PersistentData, Dorc.ApiModel, test projects). Solution-level failures confined to Windows-only projects: WiX installers (`Setup.*`, heat.exe) and `Dorc.NetFramework.Runner` (net48) — none touched by this feature. |
| 2 | `Dorc.Core.Tests` run | **YES** | Suite runs; includes the 2 new characterization tests (both pass; mutation check: removing one emission fails both). |
| 3 | `Dorc.Api.Tests` run | **YES** (22 pre-existing failures) | 213/235 pass. All 22 failures are "Windows Principal functionality is not supported on this platform" in `PropertyValuesServiceTests`/`CreateRequest*` — platform-bound, present without any change from this feature. Treated as the baseline: gates compare against 213 passing. |
| 4 | `Dorc.Monitor.IntegrationTests` executable | **NO** | Requires a real SQL Server (`DOrcConnectionString` placeholder); no SQL instance and docker daemon unavailable. R-6 gates therefore use DB-free container-resolution tests, as the IS already specifies. |
| 5 | `Dorc.Api` serves `/swagger/v1/swagger.json` locally | **YES** | HTTP 200, 270,849 bytes. Required dummy `OpenSearchSettings` + connection string in the build-output `appsettings.json` (registry reads config from `AppContext.BaseDirectory`); the DB is never contacted for the swagger endpoint. S-006's regen path is viable here. |
| 6 | `dorc-web` installs/builds | **YES** | `npm ci` + `npm run build` clean (vite). |
| 7 | `dorc-web` tests run | **YES** (chromium project) | 118/118 pass via `npx vitest run --project chromium`. Firefox/webkit projects skipped — those browsers are not preinstalled. Headless-shell shim: symlinked preinstalled `chromium_headless_shell-1194` binary into the `-1223` path the pinned Playwright expects (env forbids `playwright install`). |
| 8 | Full stack (API + SQL DB + web dev server) | **NO** | No SQL Server available (docker daemon unavailable). **SC-2 evidence mode decided now per IS S-001:** the manual UI round-trip transfers to the user's environment; test-level evidence (controller + web component tests) gates S-007. |
| 9 | SSDT dacpac build | **NO** (Linux) | `Dorc.Database.sqlproj` is legacy SSDT (net48 targets), unbuildable with the Linux SDK. **S-002 gate amendment:** "dacpac produced" is satisfied by CI/Windows build; here the gate is the DDL↔EF comparison plus SSDT project-file inclusion review. |

## Consequences applied
- S-006 (client regen): viable in this environment — recorded YES on the halting condition.
- S-007 (UI): SC-2 manual round-trip → user's environment; noted for the S-007 gate.
- S-002 (schema): dacpac-build portion of the gate delegated to CI; textual dual-source
  comparison remains the primary local gate.
- R-6 checks: DB-free container-resolution tests (as planned).
