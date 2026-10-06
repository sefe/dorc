# IS: Daemons Modernisation — Implementation Sequence

| Field       | Value                                                   |
|-------------|---------------------------------------------------------|
| **Status**  | APPROVED                                                |
| **Author**  | Agent                                                   |
| **Date**    | 2026-04-24                                              |
| **HLPS**    | HLPS-daemons-modernisation.md (APPROVED)                |
| **Folder**  | docs/daemons-modernisation/                             |
| **Issue**   | sefe/dorc#649                                           |

---

## Step Index

| ID    | Title                                                             | Addresses              | Depends On   |
|-------|-------------------------------------------------------------------|------------------------|--------------|
| S-001 | New `deploy.*` schema tables + legacy SSDT source removal         | SD-1 (schema)          | —            |
| S-002 | Pre-deploy + post-deploy data migration scripts                   | SD-1 (migration), SC-02 | S-001       |
| S-003 | EF config + model aligned to new schema                           | SD-1 (EF), DF-4        | S-001        |
| S-004 | `DaemonsPersistentSource` correctness + 409 translation           | SD-2, DF-1, DF-2, DF-3 | S-003        |
| S-005 | `ServiceStatus` hygiene — remove side effect + surface errors     | SD-3, DF-7, DF-8       | S-003        |
| S-006 | RBAC on daemon controllers                                        | SD-4, DF-9             | —            |
| S-007 | Daemon audit — table, source, seed, controllers, read endpoint    | SD-5, DF-10, DF-12     | S-001, S-004, S-006 |
| S-008 | UI bug fixes + Edit/Delete surface + audit view                   | SD-6, DF-4/5/6/11      | S-005, S-006, S-007 |
| S-009 | `Dorc.Database.IntegrationTests` project + SC-02 + SC-05b tests   | SC-02, SC-05b          | S-001, S-002, S-007 |

**Ordering note.** All nine steps land in one PR. The `Depends On` column captures *implementation order* — the order an author picks up the work — not deploy order. Deploy order is fixed by SSDT phases (see HLPS §7) and is insensitive to how the steps were authored.

**Parallelism.** S-005 and S-006 are independent of S-004 and each other; they can be authored in parallel by different contributors. S-009 can begin skeleton-only after S-001 lands and fill out as later steps complete.

---

## S-001 — New `deploy.*` schema tables and legacy SSDT source removal

### What changes
Three new SSDT table sources under `src/Dorc.Database/deploy/Tables/`: `Daemon.sql`, `ServerDaemon.sql`, `DaemonAudit.sql`. Each file uses the flat-layout convention already established by `RefDataAudit.sql` and `EnvironmentServer.sql`. Shapes per HLPS SD-1: `Daemon` has PK `Id`, plus `Name` NOT NULL, `DisplayName`/`AccountName`/`Type` nullable, all `NVARCHAR(250)`. `ServerDaemon` has composite PK `(ServerId, DaemonId)`, FK to `Daemon` with `ON DELETE CASCADE`, FK to `dbo.SERVER` with `ON DELETE NO ACTION`. `DaemonAudit` has `Id` BIGINT identity PK, nullable `DaemonId` (no FK — audit rows outlive daemons), FK to `RefDataAuditAction`, actor/time/from/to columns. Uniqueness on Daemon.Name via standard unique index; uniqueness on Daemon.DisplayName via filtered unique index (`WHERE DisplayName IS NOT NULL`).

Four legacy SSDT sources are removed: `dbo/Tables/SERVICE.sql`, `dbo/Tables/SERVER_SERVICE_MAP.sql`, `dbo/Tables/Constraints/UC_Service_Service_Name.sql`, `dbo/Tables/Constraints/UC_Service_Display_Name.sql`. Their `<Build Include>` entries in `Dorc.Database.sqlproj` are deleted and the new sources are added.

### Why it changes
Addresses **SD-1** and directly satisfies **SC-01** at the schema level. Without this step the modernisation cannot begin — the EF change in S-003 and the migration in S-002 both depend on the new tables existing in the SSDT project. Removing the legacy sources is part of SC-01's "no longer exist" requirement and tells SSDT to drop them on publish.

### Dependencies
None. This step touches only the SSDT project.

### Verification intent
SSDT project builds cleanly. A local SSDT publish against an empty DB produces `deploy.Daemon`, `deploy.ServerDaemon`, `deploy.DaemonAudit` with the specified columns, PKs, FKs, and filtered unique index on `DisplayName`. `dbo.SERVICE` and `dbo.SERVER_SERVICE_MAP` do not appear. (Full migration from populated legacy tables is verified by S-002.)

---

## S-002 — Pre-deploy staging + post-deploy data migration scripts

### What changes
Two new SQL scripts added to the SSDT project:
- `src/Dorc.Database/Scripts/Pre-Deployment/StageServicesForMigration.sql` — stages `dbo.SERVICE` and `dbo.SERVER_SERVICE_MAP` into `dbo.SERVICE_MIGRATION_STAGING` and `dbo.SERVER_SERVICE_MAP_MIGRATION_STAGING`, then empties the legacy tables (map first, service second — FK order). Guarded by `IF OBJECT_ID('dbo.SERVICE') IS NOT NULL` and by staging-table existence checks. Wired into `Script.PreDeployment.sql` via `:r`.
- `src/Dorc.Database/Scripts/Post-Deployment/MigrateStagedServicesToDaemons.sql` — copies staging → `deploy.Daemon` / `deploy.ServerDaemon` with `IDENTITY_INSERT ON` preserving `Id` values, using `WHERE NOT EXISTS` for idempotency, then drops the staging tables. Wired into `Script.PostDeployment.sql` via `:r`.

**Data-quality edge cases** in the staged legacy data (flagged during review; must be handled in-script):
- **NULL `Service_Name`**: `dbo.SERVICE.Service_Name` is `NVARCHAR(250) NULL` but `deploy.Daemon.Name` is `NOT NULL`. The post-deploy INSERT filters `WHERE s.Service_Name IS NOT NULL`. Any rows with NULL `Service_Name` are logged via `PRINT` (count + `Service_ID` list) and skipped. A nameless daemon was never usable and is not worth synthesising a name for; skipping is safe.
- **Orphan `SERVER_SERVICE_MAP` rows**: the legacy FK to `dbo.SERVER` has `ON DELETE NO ACTION` and no cascade, so stale mapping rows referencing deleted servers are possible. `deploy.ServerDaemon.ServerId` → `dbo.SERVER` is also `ON DELETE NO ACTION`, so the insert would FK-violate on orphan rows. The post-deploy INSERT filters `WHERE EXISTS (SELECT 1 FROM dbo.SERVER sv WHERE sv.Server_ID = m.Server_ID)`. Orphans are logged and skipped.
- Both filter clauses are logged in the script output for operator visibility; the counts inform the SC-02 verification that row-count differences between staged and migrated are accounted for by these filters and not by an unknown bug.

### Why it changes
Addresses **SD-1 (migration)** and **SC-02**. The staging-table approach is what makes the schema rename + drop sequence data-safe across a single SSDT publish. Pre-deploy runs before SSDT creates `deploy.Daemon`, so it cannot copy directly to the new table; it must stage to a `dbo` table that survives the schema phase. Emptying the legacy tables in pre-deploy lets SSDT drop them without `BlockOnPossibleDataLoss=False`.

### Dependencies
**S-001.** The post-deploy copy references `deploy.Daemon` / `deploy.ServerDaemon`, which must exist as SSDT sources before this step lands (otherwise the SQL will reference objects SSDT does not know about and the project build will warn).

### Verification intent
On a DB populated with representative `dbo.SERVICE` + `dbo.SERVER_SERVICE_MAP` rows, a single SSDT publish produces:
- `deploy.Daemon` row count equal to the prior `dbo.SERVICE` row count; `Id` values unchanged.
- `deploy.ServerDaemon` row count equal to the prior `dbo.SERVER_SERVICE_MAP` row count.
- Legacy tables absent.
- Staging tables absent (cleaned up by post-deploy).

A second publish is a no-op at row level — verifies idempotency. Automated assertions live in S-009; during S-002 authoring, a DBA reviewer can smoke-test locally by running `DacServices.Deploy` from a throwaway console harness or from the SSDT GUI and inspecting the staging-tables-gone / row-counts-equal outcome manually.

---

## S-003 — EF configuration and model aligned to new schema

### What changes
Three files in `src/Dorc.PersistentData/`:
- `EntityTypeConfigurations/DaemonEntityTypeConfiguration.cs` — `.ToTable("Daemon", "deploy")`. Column mappings align to PascalCase. The C# property `Daemon.ServiceType` is preserved (C-02); its column mapping changes to `.HasColumnName("Type")`.
- `EntityTypeConfigurations/ServerEntityTypeConfiguration.cs` — the `UsingEntity` skip-navigation call updated from `"SERVER_SERVICE_MAP"` / `"Server_ID"` / `"Service_ID"` to `"ServerDaemon"` (schema `"deploy"`) / `"ServerId"` / `"DaemonId"`. Inside the `UsingEntity` builder lambda, an explicit `.HasKey("ServerId", "DaemonId")` matches the DB-defined composite PK so EF does not infer a conflicting shadow key.
- `Model/Daemon.cs` — `[StringLength(50)]` → `[StringLength(250)]` on all four string properties. Property names unchanged.

Narrow grep-audit pass over the repository (patterns per HLPS SD-1) confirms no stale references to legacy table / column names remain in `src/`.

### Why it changes
Addresses **SD-1 (EF)** and satisfies the EF half of **SC-01**. Without this the application still binds to `dbo.SERVICE` at runtime and every daemon query throws against the renamed schema. Also fixes **DF-4** at the model layer (UI maxlength is fixed in S-008).

### Dependencies
**S-001** (new tables exist in the SSDT project so EF's type compiles cleanly against the expected schema).

### Verification intent
`dotnet build Dorc.sln` passes with no warnings from EF config. Existing unit tests that exercise `DaemonsPersistentSource` read/write paths pass against a DB produced by S-001 + S-002 (i.e. the integration harness in S-009). Grep-audit returns zero hits in `src/` using the narrow regex patterns listed in HLPS SD-1 (`\bdbo\.SERVICE\b`, `\bdbo\.SERVER_SERVICE_MAP\b`, `\bSERVER_SERVICE_MAP\b`, `\bService_ID\b`, `\bService_Name\b`, `\bService_Type\b`) with the same exclusions (`obj`, `bin`, `*.csproj`, `*.sln`, `swagger.json`).

---

## S-004 — `DaemonsPersistentSource` correctness and 409 translation

### What changes
- `src/Dorc.PersistentData/Sources/DaemonsPersistentSource.cs`:
  - `Update` copies `Name` alongside the other four fields (DF-1).
  - `Delete` clears `daemon.Server` and saves before the daemon delete (EF-level defence-in-depth; DB cascade is authoritative — DF-2).
  - `Add` pre-checks `Name` and `DisplayName` against `deploy.Daemon`; on collision throws `DaemonDuplicateException` (DF-3). The post-save re-query is simplified to `return Map(mapToDatabase)`.
- New folder `src/Dorc.PersistentData/Exceptions/` (does not currently exist) containing `DaemonDuplicateException.cs`. Consistent with `src/Dorc.Core/Exceptions/WrongComponentsException.cs` pattern.
- `src/Dorc.Api/Controllers/RefDataDaemonsController.cs` — `Post` wraps its work in a per-action `try/catch (DaemonDuplicateException ex)` returning `409 Conflict` with `ex.Message`. Per-action catch, not via `DefaultExceptionHandler` — matches the existing `RefDataEnvironmentsController` convention.

### Why it changes
Addresses **SD-2** and the user-facing halves of **DF-1/2/3** and **SC-07**. Before this step, rename is silently dropped, delete returns an opaque 500, duplicate insert returns an opaque 500. After, the UI can render actionable messages.

### Dependencies
**S-003** (EF bindings correct — the persistent source is reading/writing the new tables via the updated configuration).

### Verification intent
Unit tests on `DaemonsPersistentSource`: rename persists; delete of an attached daemon succeeds and clears mappings; duplicate insert throws `DaemonDuplicateException`. Controller test: duplicate POST returns 409 with the conflicting field name in the body.

---

## S-005 — `ServiceStatus` hygiene

### What changes
- `src/Dorc.Core/ServiceStatus.cs` — delete `PersistDiscoveredMappings` method and its call site in `GetServicesAndStatusForEnvironment`. The status GET path no longer writes to `deploy.ServerDaemon`.
- `src/Dorc.PersistentData/Sources/DaemonsPersistentSource.cs` — delete `DiscoverAndMapDaemonsForServer` implementation.
- `src/Dorc.PersistentData/Sources/Interfaces/IDaemonsPersistentSource.cs` — delete `DiscoverAndMapDaemonsForServer` method signature.
- `src/Dorc.ApiModel/ServiceStatusApiModel.cs` — add nullable `ErrorMessage` field. Additive only (C-02).
- `src/Dorc.Core/ServiceStatus.cs` — catch blocks on probe / ping / service-controller calls (previously `LogInformation`/`LogDebug`) additionally populate `ErrorMessage` on the returned `ServiceStatusApiModel`. Log levels stay where they are for ops.
- Regenerate the TypeScript client. The pipeline is: ensure `src/dorc-web/src/apis/dorc-api/swagger.json` reflects the new C# model (run the API or hand-edit the `definitions.ServiceStatusApiModel` entry), then run `npm run dorc-api-gen` from `src/dorc-web/`. That script invokes `openapi-generator-cli generate -g typescript-rxjs -i ./src/apis/dorc-api/swagger.json -o ./src/apis/dorc-api/`. Expect `ServiceStatusApiModel.ts` to gain `ErrorMessage?: string | null`.

### Why it changes
Addresses **SD-3** and **DF-7 / DF-8**. The write side effect on a read-shaped endpoint produces untraceable data changes and bypasses RBAC; removing it restores the read/write invariant. Surfacing probe errors turns silent "no status" into a UI-actionable diagnostic.

### Dependencies
**S-003** (rename-through-EF is stable) is sufficient. Independent of S-004, S-006.

### Verification intent
Unit test: simulate a ping timeout; the returned `ServiceStatusApiModel` has non-null `ErrorMessage` and no row is written to `deploy.ServerDaemon`. Unit test: a healthy probe returns `ServiceStatus = "Running"`, `ErrorMessage = null`, and still no row written. TypeScript check: generated `ServiceStatusApiModel.ts` has `ErrorMessage?: string | null`.

---

## S-006 — RBAC on daemon controllers

### What changes
- `src/Dorc.Api/Controllers/RefDataDaemonsController.cs` — inject `IRolePrivilegesChecker`. Gate `POST` and `PUT` on PowerUser **or** Admin; gate `DELETE` on Admin only. Return `403` with messages matching the existing convention (`"Daemons can only be deleted by Admins!"`, etc.).
- `src/Dorc.Api/Controllers/ServerDaemonsController.cs` — inject `IRolePrivilegesChecker`. Gate `POST` (attach) and `DELETE` (detach) on PowerUser **or** Admin.
- `src/Dorc.Api/Controllers/DaemonStatusController.cs` — unchanged (retains its existing `CanModifyEnvironment` gate).

### Why it changes
Addresses **SD-4** and **DF-9 / SC-04**. Every other RefData controller gates mutations on Admin/PowerUser via `IRolePrivilegesChecker`; daemons were the outlier.

### Dependencies
None. Independent of every other step. This step could land first chronologically if convenient, but the PR diff keeps it sequenced to allow reviewer mental-model alignment with earlier DB/EF work.

### Verification intent
Controller tests: anonymous / plain-authenticated callers get `403` on the mutating endpoints (with the expected message); PowerUser gets `200` on create/edit/attach/detach and `403` on delete; Admin gets `200` on all mutations. `DaemonStatusController` PUT behaviour unchanged — existing tests pass.

---

## S-007 — Daemon audit

### What changes
- `src/Dorc.PersistentData/Model/RefDataAuditAction.cs` — extend `ActionType` enum with `Attach` and `Detach`.
- `src/Dorc.PersistentData/Model/DaemonAudit.cs` — new entity. No `[Table(...)]` attribute.
- `src/Dorc.PersistentData/EntityTypeConfigurations/DaemonAuditEntityTypeConfiguration.cs` — new config using fluent `.ToTable("DaemonAudit", "deploy")`.
- `src/Dorc.PersistentData/Sources/Interfaces/IDaemonAuditPersistentSource.cs` and `src/Dorc.PersistentData/Sources/DaemonAuditPersistentSource.cs` — new source with `InsertDaemonAudit` and `GetDaemonAudit` (paged, filterable — mirror `ManageProjectsPersistentSource.GetRefDataAuditByProjectId`).
- `src/Dorc.Database/Scripts/Post-Deployment/SeedRefDataAuditActions.sql` — new seed script that upserts `Create` / `Update` / `Delete` / `Attach` / `Detach` rows if absent. Wired into `Script.PostDeployment.sql` via `:r`.
- `src/Dorc.Api/Controllers/RefDataDaemonsController.cs` — after a successful Create, Update (unless `before == after`), or Delete, call `InsertDaemonAudit` with payloads per HLPS SC-05.
- `src/Dorc.Api/Controllers/ServerDaemonsController.cs` — after a successful Attach / Detach, call `InsertDaemonAudit` with the `{ ServerId, DaemonId }` payload.
- `src/Dorc.Api/Controllers/DaemonAuditController.cs` — new read controller, `GET /DaemonAudit?daemonId=…`, authenticated-user-only gate (per U-11 resolution — mirrors `RefDataProjectAuditController`).
- `src/Dorc.Api/Dorc.Api.csproj` — DI wiring for the new source and controller (whatever existing DI pattern is in use — `Program.cs` or registration file).

### Why it changes
Addresses **SD-5**, **DF-10** (daemon audit), and **DF-12** (existing project audit is latent-broken without the seed). The post-deploy seed is the keystone: it guarantees `RefDataAuditAction` has rows at first use, so both the new daemon audit and the existing `ManageProjectsPersistentSource.InsertRefDataAudit` path become reliable. **No source change to `ManageProjectsPersistentSource` is needed** — the seed alone fixes DF-12.

### Dependencies
**S-001** (the `deploy.DaemonAudit` table must exist). **S-004** (both this step and S-004 edit `RefDataDaemonsController.cs` — S-004 adds a `try/catch` around `Post`; S-007 adds `InsertDaemonAudit` calls after `Post`/`Put`/`Delete`. S-007's controller wiring must be authored **after** S-004 has committed so the audit call sits inside the catch boundary. Otherwise, a duplicate-name POST could record a spurious `Create` audit row before the 409 translation fires.). **S-006** (sequencing convenience — the role-checker `ctor` injection in the controllers is more surgically touched once in S-006 rather than twice). Username for the audit row is read from `HttpContext.User` via the existing claims reader; that does not require S-006.

### Verification intent
- **Per-action payload unit tests** (one per action, six including the Update-skipped-when-unchanged case): asserts `RefDataAuditActionId` matches the expected action row, `Username` is populated from the current principal, `FromValue` / `ToValue` match the SC-05 conventions.
- **Seed test (i) — empty table**: programmatically truncate `deploy.RefDataAuditAction` in the CI DB, run the seed script, verify five rows present and a subsequent project PUT succeeds. Covers SC-05b test (i).
- **Seed test (ii) — pre-seeded table**: run the seed twice, verify the second run produces no row-count change and a project PUT succeeds. Covers SC-05b test (ii).
- **Read endpoint**: `GET /DaemonAudit?daemonId=…` returns the audit page for a given daemon, sorted by date descending, paginated.

---

## S-008 — UI bug fixes and Edit/Delete surface

### What changes
- `src/dorc-web/src/components/add-daemon.ts` — `maxFieldLength` 50 → 250 (DF-4). Remove `.readonly="${true}"` on AccountName and Type; initial values remain `"Local System Account"` / `"Windows Service"` (DF-6).
- `src/dorc-web/src/components/grid-button-groups/daemon-controls.ts` — invert the `userEditable` check in `startDisabled` / `stopDisabled` / `restartDisabled` getters (DF-5). Property name unchanged.
- `src/dorc-web/src/pages/page-daemons-list.ts` — add Edit (PowerUser+Admin visible) and Delete (Admin-only visible) row actions (DF-11). Role lookup uses the same pattern as `page-projects-list.ts`: import `GlobalCache from '../global-cache'`, subscribe to `GlobalCache.getInstance().allRolesResp`, populate `@property isAdmin` and `@property isPowerUser` booleans by checking `userRoles.find(p => p === 'Admin')` / `=== 'PowerUser'`, then gate visibility in the template via `.deleteHidden="${!this.isAdmin}"` (or equivalent for the vaadin-grid action column).
- New component `src/dorc-web/src/components/edit-daemon.ts` — same layout as `add-daemon.ts` but in edit mode; prefilled from the row, all fields editable, submits PUT.
- New component `src/dorc-web/src/components/daemon-audit-view.ts` — simple list rendering of per-daemon audit history from `GET /DaemonAudit?daemonId=…`, mirroring the existing project audit view structure. Surfaced on the daemon details page.
- Regenerate `src/dorc-web/src/apis/dorc-api/*` TypeScript client via `npm run dorc-api-gen` (see S-005 for the full command). Pulls in new `DaemonAuditApi`, unchanged `DaemonApiModel`, extended `ServiceStatusApiModel`.

### Why it changes
Addresses **SD-6** (DF-4, DF-5, DF-6, DF-11) and completes **SC-06** and **SC-07** at the UI layer.

### Dependencies
**S-005** (UI relies on the added `ServiceStatusApiModel.ErrorMessage` field and the regenerated TypeScript client; `DaemonApiModel` is unchanged so S-003 alone is not the blocker). **S-006** (role-gated visibility requires the backend gates to be in place so the UI's optimistic role check matches the server's authoritative one). **S-007** (daemon-audit-view depends on the `GET /DaemonAudit` read endpoint and its generated `DaemonAuditApi` TypeScript client).

### Verification intent
- Visual: Edit dialog renders, all fields editable, submits PUT, list refreshes.
- Visual: Delete button appears only for Admins; confirmation dialog warns when the daemon is attached to servers; successful delete removes the row and writes an audit row.
- Unit: `daemon-controls.ts` — four-state check table (running+editable/non-editable × stopped+editable/non-editable).
- Manual QA per HLPS SC-07.

---

## S-009 — Database migration/seed verification tests (merged into `Tests.Acceptance`)

**Amendment 2026-04-24**: SPEC-S-009 revised during authoring to merge into the existing `Tests.Acceptance` project rather than create a new `Dorc.Database.IntegrationTests`. `Tests.Acceptance` already has the MSTest runner, live CI SQL connection, and `Microsoft.Data.SqlClient`; adding the DB tests there costs one NuGet addition (`Microsoft.SqlServer.DacFx`) and one folder (`DatabaseTests/`). Originally-proposed new project is not created. See SPEC-S-009 for the revised shape. SC ownership is unchanged — S-009 still owns SC-02 and SC-05b verification; only the project location differs.

### What changes (original IS text — superseded by SPEC-S-009 amendment)
~~New test project `src/Dorc.Database.IntegrationTests/Dorc.Database.IntegrationTests.csproj`, wired into `Dorc.sln`.~~

**Test framework**: MSTest, matching the existing `Dorc.Monitor.IntegrationTests` precedent (`Microsoft.NET.Test.Sdk`, `MSTest.TestAdapter`, `MSTest.TestFramework`). Target framework `net8.0`.

**SSDT invocation** is programmatic via the DacFx NuGet package (`Microsoft.SqlServer.DacFx`), not the SqlPackage.exe CLI. The test harness wraps the build output `Dorc.Database.dacpac` and calls `DacServices.Deploy(...)` against a test database connection. DacFx ships the full publish pipeline (pre-deploy, schema diff, post-deploy) as an in-process API — no external tooling required.

**SQL instance**: two options, selected by an env var (`DORC_TEST_SQL_CONNECTION`):
- **Local dev**: developer's local SQL Server / LocalDB; connection string in `appsettings.test.json` (matches the `Dorc.Monitor.IntegrationTests` config pattern).
- **CI**: the CI pipeline must provide a SQL Server — either a `services:` container in the workflow yaml (SQL Server Linux container) or a self-hosted runner with SQL Server. Selection TBD at SPEC time, but the test code itself is CI-agnostic — it just needs a reachable connection string. The CI workflow change is part of this step; whichever option is chosen must be added to the same GitHub Actions workflow that runs `Dorc.Monitor.IntegrationTests`.

**Tests**:
- **SC-02 migration** — seeds representative rows into `dbo.SERVICE` and `dbo.SERVER_SERVICE_MAP` on an ephemeral test DB (pre-migration state: create the legacy schema objects directly via SQL, then insert rows — since the legacy tables are dropped in the dacpac, they won't be created by a normal publish). Run `DacServices.Deploy` against the dacpac; assert COUNT / MAX / EXCEPT-checksum per HLPS SC-02. Run deploy a second time; assert no row-count delta.
- **SC-05b test (i) — empty `RefDataAuditAction`** — fresh test DB; deploy dacpac; truncate `deploy.RefDataAuditAction`; re-execute `SeedRefDataAuditActions.sql` content programmatically (read from the dacpac or inline the five-row insert); assert five rows present; exercise a project PUT via the API's persistence source and assert it succeeds.
- **SC-05b test (ii) — pre-seeded** — fresh test DB; deploy dacpac (post-deploy seed runs as part of `DacServices.Deploy`); re-run the seed; assert row count unchanged at five; exercise a project PUT and assert success.
- **SC-01 migration-completeness** — after deploy, assert `OBJECT_ID('dbo.SERVICE') IS NULL`, `OBJECT_ID('dbo.SERVER_SERVICE_MAP') IS NULL`, `OBJECT_ID('deploy.Daemon') IS NOT NULL`, `OBJECT_ID('deploy.ServerDaemon') IS NOT NULL`, `OBJECT_ID('deploy.DaemonAudit') IS NOT NULL`.

**Test-DB isolation**: each test `[TestInitialize]` creates a uniquely-named test DB (`dorc_test_{guid}`) and drops it on `[TestCleanup]`. Tests do not share a DB fixture — this keeps SC-02's populated-legacy-tables and SC-05b's empty-seed scenarios cleanly separated (L-1 from review).

### Why it changes
Makes **SC-02** and **SC-05b** verifiable without a preprod DB (per HLPS U-3 fallback). Also carries SC-01 migration-completeness assertions.

**Coverage ownership for verification**:
- SC-01 → S-009 (migration-completeness test — this step) + S-003 (`dotnet build` passes).
- SC-02 → S-009.
- SC-03 → per-defect tests distributed across S-004, S-005, S-007, S-008.
- SC-04 → controller tests in the existing `Dorc.Api.Tests` project, authored as part of S-006.
- SC-05 → unit tests in `Dorc.Api.Tests` authored as part of S-007.
- SC-05b → S-009.
- SC-06 → S-008 (visual / manual QA for the role-gated actions, plus a UI unit test for `daemon-controls` state table).
- SC-07 → S-008 manual QA pass documented in the PR description.
- SC-08 → `dotnet build` + `npm run build` + full test-suite green on the PR branch; enforced by CI.

### Dependencies
Skeleton can land after **S-001** (dacpac references new tables). Migration assertions need **S-002**. Seed + project-PUT assertions need **S-007**. SC-01 migration-completeness assertion needs **S-001** and **S-002**.

### Verification intent
CI run is green. The tests can be run locally against any SQL instance reachable by the test harness. Assertions are row-count / row-content based; deterministic; not timing-dependent.
