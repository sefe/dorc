# S-001 Record — tag-capacity-expansion — 2026-07-17

Branch: `claude/tag-capacity-expansion` (main-based). Same container class as the prior
feature's TOOLCHAIN-S-001; .NET 8 SDK (8.0.423) and node22 confirmed present and working.

## 1. Test-suite baselines (this branch)

| Suite | Baseline |
|-------|----------|
| `Dorc.Core.Tests` | **136/136 pass** |
| `Dorc.Api.Tests` | **213 pass / 22 fail** — the pre-existing "Windows Principal not supported on this platform" set |
| `Dorc.Monitor.Tests` | **107 pass / 4 fail** — pre-existing platform set (process/cancellation tests) |
| `dorc-web` (chromium project) | **118/118 pass**; `npm run build` clean |

## 2. Toolchain re-check
Backend builds (net8.0 projects); all suites run; web builds/tests run. YES across the
board — consistent with the prior record; WiX/net48/SSDT still not buildable on Linux
(dacpac delegated to CI as the HLPS §4 constraint states).

## 3. Spec-update workflow feasibility
**FEASIBLE.** The prior feature proved the API serves `/swagger/v1/swagger.json` locally
in this container class (dummy OpenSearch/connection settings in the build-output
appsettings; DB never contacted). The splice technique is a ~20-line script re-derived
locally (fetch fresh spec → copy the affected model-schema fragments verbatim into the
committed spec). S-003 executes it; the Swashbuckle-in-CI fallback stands if the API
won't start on this branch.

## 4. U-4 determination (at S-001 execution time)
**#773 schema NOT present** — this branch is main-based; no component tables, DTOs, or
generated client files exist here (verified). **Scope: two columns**
(`Application_Server_Name`, `Array_Name`).

**Follow-up item created (owner: S-006 release note + PR #774 description):**
> When PR #773 merges, widen `deploy.Container.Tags`, `deploy.CloudResource.Tags`, and
> `deploy.ApiRegistration.Tags` from NVARCHAR(250) to NVARCHAR(4000) (SSDT + EF
> configurations + `[StringLength]` on the three DTO `Tags` properties), matching this
> feature's limit. Small mechanical change; the constants from S-003 are already in
> place.
