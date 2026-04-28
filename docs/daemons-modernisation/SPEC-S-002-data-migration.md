---
name: SPEC-S-002 — Pre-deploy staging + post-deploy data migration
description: JIT Specification for S-002 — stages dbo.SERVICE / dbo.SERVER_SERVICE_MAP data in pre-deploy, copies from staging into deploy.Daemon / deploy.ServerDaemon in post-deploy. Idempotent, filters NULL Service_Name and orphan server rows.
type: spec
status: APPROVED
---

# SPEC-S-002 — Pre-deploy staging + post-deploy data migration

| Field       | Value                                                   |
|-------------|---------------------------------------------------------|
| **Status**  | APPROVED                                                |
| **Step**    | S-002                                                   |
| **Author**  | Agent                                                   |
| **Date**    | 2026-04-24                                              |
| **IS**      | IS-daemons-modernisation.md (APPROVED)                  |
| **HLPS**    | HLPS-daemons-modernisation.md (APPROVED)                |
| **Folder**  | docs/daemons-modernisation/                             |

---

## 1. Context

### What this step addresses
S-002 rescues the rows in `dbo.SERVICE` and `dbo.SERVER_SERVICE_MAP` across the SSDT publish that drops those tables. The rescue is split across SSDT phases:

- **Pre-deploy** runs before the schema phase. At that point `dbo.SERVICE` still exists (SSDT has not yet dropped it) but `deploy.Daemon` does not (SSDT has not yet created it). Pre-deploy therefore cannot copy directly from old to new; it stages the data into new `dbo.*_MIGRATION_STAGING` tables that survive the schema phase.
- **Post-deploy** runs after the schema phase. At that point `dbo.SERVICE` is gone, `deploy.Daemon` exists (empty), and the staging tables still hold the rescued rows. Post-deploy copies staging → new tables (preserving `Id` values via `IDENTITY_INSERT`) and drops the staging tables.

Emptying the legacy tables in pre-deploy (after staging) means SSDT's schema phase drops empty tables, so the default `BlockOnPossibleDataLoss=True` publish profile works unchanged — no deploy-profile override required.

### Scope
Two new SQL script files, their references from the existing entry-point scripts, and sqlproj `<None Include>` entries:
- `src/Dorc.Database/Scripts/Pre-Deployment/StageServicesForMigration.sql` — new.
- `src/Dorc.Database/Scripts/Post-Deployment/MigrateStagedServicesToDaemons.sql` — new.
- `src/Dorc.Database/Scripts/Pre-Deployment/Script.PreDeployment.sql` — add one `:r` reference to the new pre-deploy script.
- `src/Dorc.Database/Scripts/Post-Deployment/Script.PostDeployment.sql` — add one `:r` reference to the new post-deploy script.
- `src/Dorc.Database/Dorc.Database.sqlproj` — two new `<None Include>` entries.

Out of scope: EF config (S-003), any C# code, the seed for `deploy.RefDataAuditAction` (S-007 owns that post-deploy script).

### Governing constraints
- **HLPS C-01**: no data loss. Every row in `dbo.SERVICE` (except NULL-name rows; see §2.2) and every non-orphan row in `dbo.SERVER_SERVICE_MAP` must land in the corresponding `deploy.*` table with `Id` values preserved.
- **HLPS C-04**: idempotent. Repeated publishes produce no row deltas, no PK violations, no NOT-NULL violations.
- **HLPS C-07**: `deploy.RefDataAuditAction` is off-limits for this SPEC. Only S-007 owns that table's seed and schema.
- **IS S-002**: data-quality filters are prescriptive — NULL `Service_Name` rows are logged and skipped (the `Name` column in `deploy.Daemon` is `NOT NULL`); orphan `SERVER_SERVICE_MAP.Server_ID` rows referencing deleted servers are logged and skipped (the `FK_ServerDaemon_Server` constraint would otherwise reject them).

---

## 2. Production Code Change

### 2.1 Pre-deploy: `StageServicesForMigration.sql`

**Target**: new file `src/Dorc.Database/Scripts/Pre-Deployment/StageServicesForMigration.sql`.

**Behaviour**:
1. Guard the entire script on `IF OBJECT_ID('dbo.SERVICE') IS NOT NULL` — on second and subsequent publishes the legacy table no longer exists and the whole block is skipped.
2. If `dbo.SERVICE_MIGRATION_STAGING` does not yet exist, create it via `SELECT … INTO` from `dbo.SERVICE`. This captures every column including the original `Service_ID` IDENTITY values. `SELECT … INTO` produces a table without constraints or indexes — acceptable because the staging table is transient and read exactly once by post-deploy.
3. If `dbo.SERVER_SERVICE_MAP_MIGRATION_STAGING` does not yet exist, create it via `SELECT … INTO` from `dbo.SERVER_SERVICE_MAP`.
4. `DELETE FROM [dbo].[SERVER_SERVICE_MAP]` — empty the map table first (FK-order: children before parents).
5. `DELETE FROM [dbo].[SERVICE]` — empty the service table.

The staging-table existence guards protect against a previous pre-deploy succeeding and the publish then failing mid-way — on restart, we must not re-stage on top of existing staging rows (which would silently duplicate). The guards also short-circuit if an operator hand-created the staging tables for a manual dress rehearsal.

**Logging**: each step emits a `PRINT` line ("Staging dbo.SERVICE → dbo.SERVICE_MIGRATION_STAGING (N rows)" etc.) so the publish log is auditable.

### 2.2 Post-deploy: `MigrateStagedServicesToDaemons.sql`

**Target**: new file `src/Dorc.Database/Scripts/Post-Deployment/MigrateStagedServicesToDaemons.sql`.

**Behaviour**:
1. Guard on `IF OBJECT_ID('dbo.SERVICE_MIGRATION_STAGING') IS NOT NULL AND OBJECT_ID('deploy.Daemon') IS NOT NULL`. On a clean first deploy both are true; on a repeat deploy the staging tables no longer exist and the block is skipped.
2. **NULL `Service_Name` filter and log**: count rows in `dbo.SERVICE_MIGRATION_STAGING` where `Service_Name IS NULL`; if non-zero, `PRINT` the count and enumerate the `Service_ID` values. These rows are skipped because `deploy.Daemon.Name` is `NOT NULL`. A nameless daemon was never usable; synthesising a name would be guessing.
3. `SET IDENTITY_INSERT [deploy].[Daemon] ON` and insert from staging:
   ```
   INSERT INTO deploy.Daemon (Id, Name, DisplayName, AccountName, Type)
   SELECT Service_ID, Service_Name, Display_Name, Account_Name, Service_Type
   FROM dbo.SERVICE_MIGRATION_STAGING s
   WHERE s.Service_Name IS NOT NULL
     AND NOT EXISTS (SELECT 1 FROM deploy.Daemon d WHERE d.Id = s.Service_ID);
   ```
   Then `SET IDENTITY_INSERT [deploy].[Daemon] OFF`.
4. **Orphan `Server_ID` filter and log**: count rows in `dbo.SERVER_SERVICE_MAP_MIGRATION_STAGING` where `Server_ID NOT IN (SELECT Server_ID FROM dbo.SERVER)`; if non-zero, `PRINT` the count and enumerate the offending `(Server_ID, Service_ID)` pairs.
5. Insert the remaining map rows:
   ```
   INSERT INTO deploy.ServerDaemon (ServerId, DaemonId)
   SELECT Server_ID, Service_ID
   FROM dbo.SERVER_SERVICE_MAP_MIGRATION_STAGING m
   WHERE EXISTS (SELECT 1 FROM dbo.SERVER sv WHERE sv.Server_ID = m.Server_ID)
     AND EXISTS (SELECT 1 FROM deploy.Daemon d WHERE d.Id = m.Service_ID)
     AND NOT EXISTS (SELECT 1 FROM deploy.ServerDaemon sd
                     WHERE sd.ServerId = m.Server_ID AND sd.DaemonId = m.Service_ID);
   ```
   The second `EXISTS` filter (daemon still present) covers the case where the daemon was skipped in step 3 due to NULL name — its map rows must also be skipped to avoid the `FK_ServerDaemon_Daemon` violation.
6. `DROP TABLE [dbo].[SERVICE_MIGRATION_STAGING]` and `DROP TABLE [dbo].[SERVER_SERVICE_MAP_MIGRATION_STAGING]`. Staging tables are transient; clean up after successful migration.
7. **Logging**: each step emits a `PRINT` line with row counts — staged, inserted, skipped-null-name, skipped-orphan-server, skipped-orphan-daemon. The final line reports completion.

### 2.3 Entry-point script wiring

**`Scripts/Pre-Deployment/Script.PreDeployment.sql`**: currently contains only the template comment block. Add one line at the bottom:
```
:r .\StageServicesForMigration.sql
```

**`Scripts/Post-Deployment/Script.PostDeployment.sql`**: currently contains `:r .\CleanupOrphanedScripts.sql`. Add one line before or after — positioning does not affect correctness, but **before** the cleanup is recommended because the migration is higher priority (the cleanup is a general-purpose orphan sweeper that runs every publish). Result:
```
:r .\MigrateStagedServicesToDaemons.sql
:r .\CleanupOrphanedScripts.sql
```

### 2.4 `Dorc.Database.sqlproj` wiring

Two new `<None Include>` entries co-located with the existing `<None Include="Scripts\Pre-Deployment\AddCancelledFieldsToArchiveDeploymentRequests.sql" />` (or wherever the pre-deploy / post-deploy None entries currently live — the SPEC author should match the existing convention):
```xml
<None Include="Scripts\Pre-Deployment\StageServicesForMigration.sql" />
<None Include="Scripts\Post-Deployment\MigrateStagedServicesToDaemons.sql" />
```

The existing `<PreDeploy>` and `<PostDeploy>` item groups referring to the entry-point scripts are unchanged — only those two file paths are referenced by SSDT; the sub-scripts are pulled in transitively by `:r`.

### 2.5 Re-run safety note

The two `OBJECT_ID` guards and the `WHERE NOT EXISTS` clauses together give three independent layers of idempotency:
- **First run**: legacy tables exist → stage, then empty → post-deploy finds staging tables → migrate → drop staging.
- **Second run after success**: legacy tables gone → pre-deploy guard is false, skip → staging tables gone → post-deploy guard is false, skip.
- **Second run after pre-deploy succeeded but publish failed mid-way**: legacy tables empty (pre-deploy committed) → pre-deploy re-enters but the inner staging-table guards are also false (staging created on first attempt) → skip re-stage. Post-deploy then migrates from the surviving staging tables.
- **Second run after post-deploy succeeded but a later step failed**: staging tables dropped → post-deploy guard is false, skip. No double-insert.

---

## 3. Branch

Continues on `feat/649-daemons-modernisation` (all S-NNN work lands on the single feature branch per HLPS C-06). No sub-branch.

---

## 4. Test Approach

### Rationale
Automated verification belongs to **S-009** (`Dorc.Database.IntegrationTests` — DacFx + MSTest running `DacServices.Deploy` against an ephemeral DB and asserting COUNT / checksum). S-002's tests are carried in S-009 when that step lands. In the meantime, manual verification is the gate.

### Test 1 — SSDT project build (automated, existing CI gate)
`MSBuild Dorc.Database.sqlproj` must succeed. The two new SQL files are not SSDT-parsed as schema (they are SQLCMD scripts, not DDL), but their `<None Include>` presence means they are packed into the dacpac's scripts section. Build warnings about unresolved object references in the SQLCMD scripts would indicate a reference error — but because the scripts use runtime `OBJECT_ID` checks rather than static references, no build warnings are expected.

**Pass**: build clean; dacpac produced; no new warnings vs the baseline after S-001.

### Test 2 — Local publish smoke against a populated legacy DB (manual, one-shot)
This is the meaningful test for S-002 because it exercises the migration path end-to-end.

Procedure (the SPEC author performs this on a dev/LocalDB instance):
1. Start from a DB at the pre-S-001 state: `dbo.SERVICE` and `dbo.SERVER_SERVICE_MAP` exist and contain representative rows. If no such DB is handy, seed one manually with a handful of rows including at least one with NULL `Service_Name` and at least one orphan `Server_ID` — these exercise the filter paths.
2. Snapshot: `SELECT COUNT(*), MAX(Service_ID) FROM dbo.SERVICE` and the same for `dbo.SERVER_SERVICE_MAP`.
3. Publish the dacpac built from this step's branch.
4. Inspect the publish log: should see `PRINT` lines for staging counts, NULL-name count, orphan count, insert counts, and cleanup.
5. Query the new tables:
   - `SELECT COUNT(*), MAX(Id) FROM deploy.Daemon` — count should equal (original count − NULL-name skipped); `MAX(Id)` should equal the original `MAX(Service_ID)`.
   - `SELECT COUNT(*) FROM deploy.ServerDaemon` — count should equal (original count − orphan skipped − NULL-name-daemon-dependent skipped).
   - Row-level: `EXCEPT` check on `(Id, Name, DisplayName, AccountName, Type)` vs the pre-snapshot projection of non-NULL-name rows. Zero rows returned means perfect preservation.
6. Confirm `dbo.SERVICE`, `dbo.SERVER_SERVICE_MAP`, `dbo.SERVICE_MIGRATION_STAGING`, `dbo.SERVER_SERVICE_MAP_MIGRATION_STAGING` are all absent.
7. Run the publish a **second** time: log should show the pre-deploy and post-deploy guards short-circuiting. Row counts unchanged.

**Pass**: all assertions hold across both publishes. The SPEC author records the log output and assertion results in the PR description.

### Test 3 — Idempotency simulation (manual, one-shot)
Harder to reproduce locally: simulate a failure between pre-deploy and post-deploy. The simplest technique is to run the pre-deploy script manually against the populated legacy DB (using `sqlcmd -i StageServicesForMigration.sql`), verify the legacy tables are emptied and staging tables populated, **then** publish the dacpac. Pre-deploy re-runs (no-op on guards), schema phase drops empty legacy, post-deploy migrates from surviving staging. Confirms the resume path.

**Pass**: second-attempt publish completes cleanly; rows end up in the correct `deploy.*` tables with Ids preserved.

### Existing tests
No C# or TypeScript code changes in S-002 — existing tests are unaffected. S-009 will carry the automated versions of Tests 2 and 3 when that step lands.

---

## 5. Commit Strategy

Single commit covering the two new SQL files + the two entry-point-script edits + the two sqlproj additions. If the implementer prefers to separate pre-deploy and post-deploy into two commits, that is acceptable — they are logically decoupled by the staging tables and each is individually idempotent.

---

## 6. Acceptance Criteria

| ID   | Criterion |
|------|-----------|
| AC-1 | `Scripts/Pre-Deployment/StageServicesForMigration.sql` exists and implements the behaviour in §2.1: guarded by `OBJECT_ID('dbo.SERVICE') IS NOT NULL`, idempotent via staging-table existence checks, stages both legacy tables, empties both in FK order (map before service), emits row-count `PRINT` lines. |
| AC-2 | `Scripts/Post-Deployment/MigrateStagedServicesToDaemons.sql` exists and implements the behaviour in §2.2: guarded by staging-table + `deploy.Daemon` existence, filters NULL `Service_Name` (logs + skips), filters orphan `Server_ID` (logs + skips), uses `IDENTITY_INSERT` on `deploy.Daemon`, preserves `Id` values, uses `WHERE NOT EXISTS` clauses for idempotency, drops staging tables on completion, emits `PRINT` lines for staged / inserted / skipped counts. |
| AC-3 | `Scripts/Pre-Deployment/Script.PreDeployment.sql` references the new pre-deploy script via `:r .\StageServicesForMigration.sql`. |
| AC-4 | `Scripts/Post-Deployment/Script.PostDeployment.sql` references the new post-deploy script via `:r .\MigrateStagedServicesToDaemons.sql` ordered **before** the existing `:r .\CleanupOrphanedScripts.sql`. |
| AC-5 | `Dorc.Database.sqlproj` has two new `<None Include>` entries for the new SQL files. No other sqlproj changes in this step. |
| AC-6 | `MSBuild Dorc.Database.sqlproj` succeeds with zero errors and zero new warnings. |
| AC-7 | Manual local publish smoke (Test 2) against a DB containing representative legacy rows (including at least one NULL-name row and one orphan-server row) produces the expected row counts and `EXCEPT`-checksum zero row result. Log output and assertion results are pasted into the PR description. |
| AC-8 | Manual idempotency test (Test 3) — or a programmatic equivalent — passes. The PR description records which scenario was exercised (first-publish-on-populated-DB, repeat-publish-on-migrated-DB, resume-after-pre-deploy-only). |
| AC-9 | No changes to files outside `src/Dorc.Database/**`. No C#, TypeScript, or other project files are touched. |
| AC-10 | All pre-existing tests in the repo continue to pass on a clean build. S-002 touches no code, so this is a safety net rather than an expected regression point. |
