---
name: SPEC-S-001 — New deploy.* daemon schema tables and legacy SSDT source removal
description: JIT Specification for S-001 — creates deploy.Daemon / deploy.ServerDaemon / deploy.DaemonAudit and removes legacy dbo.SERVICE / dbo.SERVER_SERVICE_MAP / UC_Service_* SSDT sources. Schema only — no data migration, no EF, no code.
type: spec
status: APPROVED
---

# SPEC-S-001 — New `deploy.*` daemon schema tables and legacy SSDT source removal

| Field       | Value                                                   |
|-------------|---------------------------------------------------------|
| **Status**  | APPROVED                                                |
| **Step**    | S-001                                                   |
| **Author**  | Agent                                                   |
| **Date**    | 2026-04-24                                              |
| **IS**      | IS-daemons-modernisation.md (APPROVED)                  |
| **HLPS**    | HLPS-daemons-modernisation.md (APPROVED)                |
| **Folder**  | docs/daemons-modernisation/                             |

---

## 1. Context

### What this step addresses
S-001 is the foundation step for the daemons modernisation: the SSDT project gains the three new `deploy.*` tables (`Daemon`, `ServerDaemon`, `DaemonAudit`) and loses the four legacy `dbo.*` sources (`SERVICE`, `SERVER_SERVICE_MAP`, `UC_Service_Service_Name`, `UC_Service_Display_Name`). Every later step builds on the new schema being present in the SSDT project. The step contains **no data migration** (S-002) and **no EF changes** (S-003).

### Scope
- **New SSDT table sources** under `src/Dorc.Database/deploy/Tables/`:
  - `Daemon.sql`
  - `ServerDaemon.sql`
  - `DaemonAudit.sql`
- **Legacy SSDT source removals**:
  - `src/Dorc.Database/dbo/Tables/SERVICE.sql`
  - `src/Dorc.Database/dbo/Tables/SERVER_SERVICE_MAP.sql`
  - `src/Dorc.Database/dbo/Tables/Constraints/UC_Service_Service_Name.sql`
  - `src/Dorc.Database/dbo/Tables/Constraints/UC_Service_Display_Name.sql`
- **`Dorc.Database.sqlproj`** — add three new `<Build Include>` entries for the new sources; remove four `<Build Include>` entries for the deleted ones.

Nothing else is in scope at this step — no migration scripts, no EF, no controllers, no UI, no tests beyond the SSDT project building cleanly.

### Governing constraints
- **HLPS SC-01** (schema half): the new tables exist in the SSDT project with the specified columns and constraints.
- **HLPS C-01** (no data loss): this SPEC does not migrate data; it only declares the target shape. Preservation is S-002's concern.
- **HLPS C-03**: `dbo.SERVER` is untouched. `deploy.ServerDaemon` references it via a new FK declared in this SPEC.
- **HLPS C-04**: the SSDT project must build cleanly and be publishable by the standard pipeline with no manual DBA steps.
- **CLAUDE.md naming**: new tables use PascalCase cohesive names (`Daemon`, `ServerDaemon`, `DaemonAudit`). No grab-bag suffixes.

---

## 2. Production Code Change

### 2.1 `deploy.Daemon` table

**Target**: new file `src/Dorc.Database/deploy/Tables/Daemon.sql`.

**Shape**:
- `Id` — `INT IDENTITY(1,1) NOT NULL`, primary key named `PK_Daemon`, clustered.
- `Name` — `NVARCHAR(250) NOT NULL`.
- `DisplayName` — `NVARCHAR(250) NULL`.
- `AccountName` — `NVARCHAR(250) NULL`.
- `Type` — `NVARCHAR(250) NULL`. (The column is named `Type`. The C# property on the EF entity remains `ServiceType`; mapping is added in S-003.)

**Uniqueness indexes** (filtered so nullable columns with multiple NULLs do not violate uniqueness):
- `UQ_Daemon_Name` — standard `CREATE UNIQUE NONCLUSTERED INDEX` on `(Name)`. `Name` is NOT NULL, so a non-filtered unique index is correct here.
- `UQ_Daemon_DisplayName` — filtered unique index `CREATE UNIQUE NONCLUSTERED INDEX … ON deploy.Daemon (DisplayName) WHERE DisplayName IS NOT NULL`. This matches the legacy behaviour (the legacy UNIQUE constraint in `UC_Service_Display_Name` also tolerated a single NULL by SQL Server quirk, but a filtered index makes the intent explicit and allows multiple NULL rows).

Indexes live in the same `.sql` file as the table (an inline `CREATE INDEX` after the `CREATE TABLE`, or co-located in a `Constraints/` peer folder if the repo's existing convention requires separation — the SPEC author should match whichever convention `deploy/Tables/RefDataAudit.sql` and `deploy/Tables/EnvironmentServer.sql` use today).

### 2.2 `deploy.ServerDaemon` table

**Target**: new file `src/Dorc.Database/deploy/Tables/ServerDaemon.sql`.

**Shape**:
- `ServerId` — `INT NOT NULL`.
- `DaemonId` — `INT NOT NULL`.
- **Primary key**: composite, clustered, named `PK_ServerDaemon` on `(ServerId, DaemonId)`. **No surrogate `Id` column** (per HLPS U-10 — preserves EF skip-navigation shape).
- **Foreign keys**:
  - `FK_ServerDaemon_Daemon` on `DaemonId` → `deploy.Daemon(Id)` with `ON DELETE CASCADE`. Deleting a daemon removes its server mappings — this is the DB-level guarantee that S-004's EF `.Clear()` complements as defence-in-depth.
  - `FK_ServerDaemon_Server` on `ServerId` → `dbo.SERVER(Server_ID)` with `ON DELETE NO ACTION`. Explicit `NO ACTION` (not the SQL Server default inferred as `NO ACTION` anyway) to make future cascade additions a deliberate decision and avoid SQL Server error 1785 cycles.

### 2.3 `deploy.DaemonAudit` table

**Target**: new file `src/Dorc.Database/deploy/Tables/DaemonAudit.sql`.

**Shape**:
- `Id` — `BIGINT IDENTITY(1,1) NOT NULL`, primary key named `PK_DaemonAudit`, clustered. `BIGINT` (not `INT`) because audit tables accumulate unboundedly.
- `DaemonId` — `INT NULL`. **No foreign key** to `deploy.Daemon(Id)`. Audit rows must survive their daemon's deletion (for Delete / Detach actions where the daemon is gone or about to be gone). The application layer understands `DaemonId = NULL` means "daemon no longer exists".
- `RefDataAuditActionId` — `INT NOT NULL` with foreign key `FK_DaemonAudit_RefDataAuditAction` → `deploy.RefDataAuditAction(RefDataAuditActionId)`. The seed in S-007 guarantees the referenced rows exist at first write.
- `Username` — `NVARCHAR(MAX) NOT NULL`.
- `Date` — `DATETIME NOT NULL`.
- `FromValue` — `NVARCHAR(MAX) NULL`. Populated per HLPS SC-05 conventions (JSON of the daemon before mutation, or `{ ServerId, DaemonId }` for Detach, or null for Create/Attach).
- `ToValue` — `NVARCHAR(MAX) NULL`. Populated per HLPS SC-05 conventions.

### 2.4 Legacy SSDT source removal

Four files are deleted:
- `src/Dorc.Database/dbo/Tables/SERVICE.sql`
- `src/Dorc.Database/dbo/Tables/SERVER_SERVICE_MAP.sql`
- `src/Dorc.Database/dbo/Tables/Constraints/UC_Service_Service_Name.sql`
- `src/Dorc.Database/dbo/Tables/Constraints/UC_Service_Display_Name.sql`

The empty `dbo/Tables/Constraints/` directory is left in place if other constraint files remain there. If the constraint folder ends up empty after this step, it may be removed — the SPEC author should check.

### 2.5 `Dorc.Database.sqlproj`

In the `<ItemGroup>` containing the existing `<Build Include="deploy\Tables\RefDataAudit.sql" />` family:
- **Add** three `<Build Include>` entries: `deploy\Tables\Daemon.sql`, `deploy\Tables\ServerDaemon.sql`, `deploy\Tables\DaemonAudit.sql`.
- **Remove** four `<Build Include>` entries: `dbo\Tables\SERVICE.sql`, `dbo\Tables\SERVER_SERVICE_MAP.sql`, `dbo\Tables\Constraints\UC_Service_Service_Name.sql`, `dbo\Tables\Constraints\UC_Service_Display_Name.sql`.

No other sqlproj changes (no changes to pre/post-deploy references — those are S-002's scope).

### 2.6 SSDT publish note (informational, not a code change)

If anyone attempts to publish the S-001 state to a populated DB **before** S-002 and S-007 land, the publish will:
- Attempt to drop `dbo.SERVICE` / `dbo.SERVER_SERVICE_MAP` and fail with `BlockOnPossibleDataLoss` (data still present).
- Create the new `deploy.*` tables (empty).
- Fail to reference `deploy.RefDataAuditAction` seed rows (because the seed SQL is in S-007's post-deploy).

This is expected and safe. S-001's SSDT project is coherent on its own; it just isn't publish-complete until S-002 and S-007 land alongside. Since all three steps ship in one PR (HLPS C-06), this never surfaces outside local dev.

---

## 3. Branch

`feature/649-S-001-daemon-schema-tables`

Branches off the existing `feat/649-daemons-modernisation` feature branch (already created at the start of this work and already merged with origin/main).

---

## 4. Test Approach

### Rationale
S-001 is pure SSDT schema authoring. The meaningful automated verification is in S-009's `Dorc.Database.IntegrationTests` project, which does not yet exist and is not in scope for this SPEC. For S-001 on its own, the test burden is limited to: the SSDT project builds, and a manual publish smoke-test produces the expected schema.

### Test 1 — SSDT project build (automated, existing CI gate)
`dotnet build src/Dorc.Database/Dorc.Database.sqlproj` must succeed with zero errors. Existing CI already runs this; no new test infrastructure required.

**Pass condition**: build succeeds; no SSDT warnings about unresolved references (e.g. a FK referring to a table the project doesn't know about).

### Test 2 — Local publish smoke (manual, one-shot)
Against a throwaway empty SQL instance (LocalDB or developer SQL Server), perform `DacServices.Deploy` (or SSDT GUI publish) of the built dacpac and query the system catalogs:
- `OBJECT_ID('deploy.Daemon') IS NOT NULL`
- `OBJECT_ID('deploy.ServerDaemon') IS NOT NULL`
- `OBJECT_ID('deploy.DaemonAudit') IS NOT NULL`
- `OBJECT_ID('dbo.SERVICE') IS NULL`
- `OBJECT_ID('dbo.SERVER_SERVICE_MAP') IS NULL`
- `PK_ServerDaemon` is a composite PK on `(ServerId, DaemonId)` (query `sys.indexes` + `sys.index_columns`).
- `UQ_Daemon_DisplayName` is a filtered unique index with `has_filter = 1` (query `sys.indexes`).
- Foreign keys `FK_ServerDaemon_Daemon` (CASCADE) and `FK_ServerDaemon_Server` (NO ACTION) are declared with the expected actions (query `sys.foreign_keys`).

**Pass condition**: all assertions hold. A checklist of the SQL snippets for each assertion is expected in the PR description for S-001.

This manual smoke is superseded by S-009's automated `SC-01 migration-completeness` assertion once that project lands; the manual smoke remains the proof for S-001 in isolation.

### Test 3 — No regression in the existing sqlproj (automated, existing CI gate)
All other SSDT build outputs produced by the CI build must be unchanged except for the additions / removals above. Spot-check: the total number of `<Build Include>` entries in `Dorc.Database.sqlproj` decreases by 1 net (4 removed, 3 added).

### Existing tests
No code-level tests exist for SSDT schema in the repo today. The manual smoke stands in until S-009 arrives. All other existing tests (`Dorc.Api.Tests`, `Dorc.Monitor.Tests`, `Dorc.Monitor.IntegrationTests`) must continue to pass; S-001 does not touch any C# code.

---

## 5. Commit Strategy

S-001 is small enough that one commit is sufficient:
- Create the three new `.sql` files.
- Delete the four legacy `.sql` files.
- Update `Dorc.Database.sqlproj`.

If the implementer prefers to split by "additions" and "removals" for reviewability, two commits (one adding the new sources + sqlproj additions; one removing the legacy sources + sqlproj removals) is acceptable. The split along that axis is idempotent — the second commit can land minutes or hours after the first without breaking the intervening state (though the build will fail between the two commits because the FK/reference graph is temporarily inconsistent; if splitting, flag this in the commit messages so reviewers know not to bisect that range).

---

## 6. Acceptance Criteria

| ID   | Criterion |
|------|-----------|
| AC-1 | `src/Dorc.Database/deploy/Tables/Daemon.sql` exists with `Id`, `Name`, `DisplayName`, `AccountName`, `Type` columns and widths per §2.1; `PK_Daemon` clustered PK on `Id`; `UQ_Daemon_Name` unique index on `Name`; `UQ_Daemon_DisplayName` filtered unique index on `DisplayName`. |
| AC-2 | `src/Dorc.Database/deploy/Tables/ServerDaemon.sql` exists with `ServerId`, `DaemonId`; `PK_ServerDaemon` composite clustered PK on `(ServerId, DaemonId)`; `FK_ServerDaemon_Daemon` with `ON DELETE CASCADE` to `deploy.Daemon(Id)`; `FK_ServerDaemon_Server` with `ON DELETE NO ACTION` to `dbo.SERVER(Server_ID)`. **No `Id` surrogate column.** |
| AC-3 | `src/Dorc.Database/deploy/Tables/DaemonAudit.sql` exists with `Id BIGINT IDENTITY PK`, nullable `DaemonId INT`, `RefDataAuditActionId INT NOT NULL` FK to `deploy.RefDataAuditAction(RefDataAuditActionId)`, `Username NVARCHAR(MAX) NOT NULL`, `Date DATETIME NOT NULL`, nullable `FromValue NVARCHAR(MAX)`, nullable `ToValue NVARCHAR(MAX)`. **No FK from `DaemonId` to `deploy.Daemon`.** |
| AC-4 | The four legacy files listed in §2.4 no longer exist in the repository. |
| AC-5 | `Dorc.Database.sqlproj` has three new `<Build Include>` entries for the new `.sql` files and has removed the four entries for the deleted files. Net change in `<Build Include>` count is −1. |
| AC-6 | `dotnet build src/Dorc.Database/Dorc.Database.sqlproj` succeeds with zero errors and zero new warnings (compared to the `feat/649-daemons-modernisation` branch baseline). |
| AC-7 | Manual local SSDT publish against an empty DB produces the schema asserted in Test 2. The PR description for S-001 includes the SQL catalog-query output for each assertion. |
| AC-8 | No changes to any file outside the SSDT project (`src/Dorc.Database/**`). No C#, TypeScript, `Dorc.sln`, or other project files are touched. |
| AC-9 | All pre-existing tests in the repo (C# test projects, TypeScript test suites) continue to pass on a clean build. This is a safety net — S-001 does not touch code, so regressions are not expected, but CI verification is required. |
