---
name: SPEC-S-003 — EF configuration and model aligned to new deploy.* schema
description: JIT Specification for S-003 — point EF at deploy.Daemon and deploy.ServerDaemon, map the preserved ServiceType property to the new Type column, loosen StringLength to 250, and confirm the grep-audit is clean.
type: spec
status: APPROVED
---

# SPEC-S-003 — EF configuration and model aligned to new `deploy.*` schema

| Field       | Value                                                   |
|-------------|---------------------------------------------------------|
| **Status**  | APPROVED                                                |
| **Step**    | S-003                                                   |
| **Author**  | Agent                                                   |
| **Date**    | 2026-04-24                                              |
| **IS**      | IS-daemons-modernisation.md (APPROVED)                  |
| **HLPS**    | HLPS-daemons-modernisation.md (APPROVED)                |
| **Folder**  | docs/daemons-modernisation/                             |

---

## 1. Context

### What this step addresses
S-001 created the new `deploy.*` tables. S-002 wrote the migration that populates them on publish. S-003 is the EF half: the `DeploymentContext` is re-pointed at the new tables so that every downstream read/write path — `DaemonsPersistentSource`, `ServerDaemonsController`, `DaemonStatusController`, the Monitor's daemon queries — binds against `deploy.Daemon` and `deploy.ServerDaemon` instead of the legacy `dbo.SERVICE` and `dbo.SERVER_SERVICE_MAP`.

The C# property `Daemon.ServiceType` is **preserved** (Round 1 review R2-H1 — renaming it would break 13 downstream references including `DaemonApiModel.ServiceType`, `VariableValueDaemons.ServiceType`, the generated TypeScript client, and the `Tools.PostRestoreEndurCLI` tool). Only the DB column name changes (`Service_Type` → `Type`); the EF mapping bridges the two via `.HasColumnName("Type")`.

### Scope
Three files under `src/Dorc.PersistentData/`:
- `EntityTypeConfigurations/DaemonEntityTypeConfiguration.cs`
- `EntityTypeConfigurations/ServerEntityTypeConfiguration.cs` (the skip-navigation block on lines 35–43)
- `Model/Daemon.cs`

Plus a grep-audit verification pass across `src/` using the narrow regex patterns from HLPS SD-1.

Out of scope: `DaemonsPersistentSource` bug fixes (S-004), `ServiceStatus` hygiene (S-005), any controller changes, any tests beyond the automated `dotnet build` gate and the existing `DaemonsPersistentSource` tests continuing to pass.

### Governing constraints
- **HLPS C-02**: no breaking change to `DaemonApiModel.ServiceType` or `VariableValueDaemons.ServiceType`. The C# property name on the EF entity is preserved; only the underlying column is renamed.
- **HLPS SC-01** (EF half): after this step the EF model binds to `deploy.Daemon` and `deploy.ServerDaemon` with PascalCase column names.
- **HLPS SD-1 / Round 2 review R2-M5**: the `UsingEntity` call on the skip-navigation must explicitly declare the composite PK via `.HasKey("ServerId", "DaemonId")` so EF does not infer a conflicting shadow key.

---

## 2. Production Code Change

### 2.1 `DaemonEntityTypeConfiguration.cs`

**Target**: the `Configure` method.

**Change**:
- `.ToTable("SERVICE")` → `.ToTable("Daemon", "deploy")`.
- Remove the four legacy `HasColumnName` mappings for `Id`, `Name`, `DisplayName`, `AccountName` — after the rename these map 1:1 by EF convention (C# property name matches DB column name) and the explicit mappings become stale no-ops.
- Keep the `ServiceType` property's `HasColumnName` mapping but change its argument from `"Service_Type"` to `"Type"`. This is the one remaining property where the C# name and the DB column name differ — preserved C# name, renamed DB column.

The final shape: one `.ToTable(...)` call and one `.Property(e => e.ServiceType).HasColumnName("Type")` call. All other mappings are implicit.

### 2.2 `ServerEntityTypeConfiguration.cs` (skip-navigation, lines 35–43)

**Target**: the `UsingEntity` block for the `Server` ↔ `Daemon` many-to-many on `SERVER_SERVICE_MAP`.

**Change**: switch from the 3-argument `UsingEntity(string name, …, …)` overload to the 4-argument `UsingEntity(…, …, configureJoinEntityType)` overload (the same overload already used by the `EnvironmentServer` skip-navigation on lines 45–56 of the same file). This lets the join entity be configured with both a schema-qualified table name and an explicit composite PK. The new configuration:

- `DaemonId` FK to `Daemon` (replacing `Service_ID` → `Daemon`).
- `ServerId` FK to `Server` (replacing `Server_ID` → `Server`).
- `configureJoinEntityType`:
  - `.ToTable("ServerDaemon", schema: "deploy")` — matches the DB table created in S-001.
  - `.HasKey("ServerId", "DaemonId")` — declares the composite PK on the shadow join entity so it matches `PK_ServerDaemon` in the DB.

Order of the two `.HasKey` columns (`ServerId` first, `DaemonId` second) matches the DDL in S-001's `ServerDaemon.sql`. This alignment matters so EF's generated migrations (if any consumer runs `dotnet ef migrations`) don't produce a "differing key column order" diff.

### 2.3 `Model/Daemon.cs`

**Target**: the four `[StringLength(50)]` attributes on `Name`, `DisplayName`, `AccountName`, `ServiceType`.

**Change**: `[StringLength(50)]` → `[StringLength(250)]` on all four. The DB columns are `NVARCHAR(250)` (HLPS U-6 resolution). This is a validation-loosening — strings between 51 and 250 characters that were previously rejected at MVC binding are now accepted. Additive per C-02.

### 2.4 Grep-audit pass

After the three edits land, run the narrow-regex grep over `src/` per HLPS SD-1:

- `\bdbo\.SERVICE\b`
- `\bdbo\.SERVER_SERVICE_MAP\b`
- `\bSERVER_SERVICE_MAP\b`
- `\bService_ID\b`
- `\bService_Name\b`
- `\bService_Type\b`

Exclusions: `**/obj/**`, `**/bin/**`, `*.csproj`, `*.sln`, `*swagger.json`.

**Expected result**: zero hits in `src/` — legacy references are either in deleted SSDT files (already removed in S-001) or in the EF config we're changing in this step. Any remaining hit is either a missed rename (fix it) or a genuine false positive (confirm and document).

The pattern list intentionally does **not** include `ServiceType` (the C# property name, preserved).

---

## 3. Branch

Continues on `feat/649-daemons-modernisation` (single feature branch per HLPS C-06).

---

## 4. Test Approach

### Rationale
EF configuration changes are exercised end-to-end by any test that reads or writes `Daemon` entities. The automated gate is:
- `dotnet build Dorc.sln` succeeds with zero errors.
- Existing unit tests that touch daemon CRUD paths continue to pass — specifically the tests in `Dorc.Api.Tests` that exercise `RefDataDaemonsController` and `ServerDaemonsController`.

Full DB-roundtrip verification is in S-009 (`Dorc.Database.IntegrationTests`). For S-003 in isolation, build + existing tests are the gate.

### Test 1 — Solution build (automated, existing CI gate)
`dotnet build Dorc.sln` completes with zero errors. A build break here would most likely come from:
- A typo in a `HasColumnName` string — but S-003 removes four such calls and leaves only one, reducing the typo surface.
- A missing `using` for the `.HasKey(params string[])` overload. (Lives in `Microsoft.EntityFrameworkCore`, already imported.)
- A mistake in the `UsingEntity` lambda signature. EF's 4-arg overload takes `Action<EntityTypeBuilder>` as the configureJoinEntityType parameter; the skip-navigation block for `EnvironmentServer` in the same file is the reference template.

**Pass**: build clean.

### Test 2 — Existing daemon tests (automated, existing CI gate)
Run the `Dorc.Api.Tests` project. Every test that hits a daemon-related controller must continue to pass. These tests use a test fixture (not a live DB), so they exercise the EF model-building path end-to-end but do not SQL-query. A regression here would indicate a mapping mistake that breaks the model's internal consistency.

**Pass**: all tests green.

### Test 3 — Grep-audit (manual, one-shot)
Run the six narrow regex patterns from §2.4. Expected: zero hits in `src/`.

**Pass**: zero hits. The SPEC author records the grep output (or a screenshot of zero matches) in the PR description.

### Test 4 — EF model diagnostic (manual, smoke)
From a dev environment connected to a test DB containing the S-001 + S-002 output (i.e., `deploy.Daemon` and `deploy.ServerDaemon` populated with migrated rows), start the API and issue `GET /RefDataDaemons`. The response should list the migrated daemons with correct `ServiceType` values (populated from the `Type` column). No 500. No `SqlException: Invalid object name 'SERVICE'`.

**Pass**: API returns 200 with the expected daemon list.

### Existing tests
All existing tests across the repo must pass. The EF change is load-bearing for the daemon feature surface; breakage outside daemons is not expected but CI confirms it.

---

## 5. Commit Strategy

Single commit is appropriate — all three files are tightly coupled and reviewable together. If the implementer prefers to split, the `Daemon.cs` `StringLength` change could land as a separate commit (it's independent of the EF re-pointing). Two commits maximum; no logical reason for more.

---

## 6. Acceptance Criteria

| ID   | Criterion |
|------|-----------|
| AC-1 | `DaemonEntityTypeConfiguration.Configure` calls `.ToTable("Daemon", "deploy")`. No other table name. |
| AC-2 | `DaemonEntityTypeConfiguration` has exactly one explicit `HasColumnName` call: `.Property(e => e.ServiceType).HasColumnName("Type")`. The four legacy `HasColumnName` calls for `Id`, `Name`, `DisplayName`, `AccountName` are removed. |
| AC-3 | `ServerEntityTypeConfiguration`'s skip-navigation block for `Server` ↔ `Daemon` uses the 4-argument `UsingEntity` overload, declares `ToTable("ServerDaemon", schema: "deploy")`, declares `.HasKey("ServerId", "DaemonId")`, uses FK names `ServerId` and `DaemonId` (not `Server_ID` / `Service_ID`). |
| AC-4 | `Model/Daemon.cs` has `[StringLength(250)]` on all four of `Name`, `DisplayName`, `AccountName`, `ServiceType`. No other change. The C# property name `ServiceType` is preserved. |
| AC-5 | `dotnet build Dorc.sln` succeeds with zero errors. |
| AC-6 | All pre-existing tests in `Dorc.Api.Tests` (and any other project that exercises daemon paths) continue to pass on a clean build. |
| AC-7 | Grep-audit using the six narrow regex patterns from HLPS SD-1 returns zero hits across `src/` (excluding `obj`, `bin`, `*.csproj`, `*.sln`, `*swagger.json`). |
| AC-8 | No changes to files outside `src/Dorc.PersistentData/`. |
| AC-9 | `DaemonApiModel.ServiceType` C# property is unchanged. `VariableValueDaemons.ServiceType` C# property is unchanged. The TypeScript-generated `DaemonApiModel.ts` is unchanged (no shape change on the wire). |
