---
name: SPEC-S-009 — Database integration tests (merged into Tests.Acceptance)
description: JIT Specification for S-009 (revised) — DB-level tests for SC-01 / SC-02 / SC-05b authored inside the existing Tests.Acceptance project, using DacFx for programmatic dacpac publish. Replaces the IS plan to create a new Dorc.Database.IntegrationTests project.
type: spec
status: APPROVED
---

# SPEC-S-009 — Database integration tests (revised — merged into `Tests.Acceptance`)

| Field       | Value                                                   |
|-------------|---------------------------------------------------------|
| **Status**  | APPROVED                                                |
| **Step**    | S-009                                                   |
| **Author**  | Agent                                                   |
| **Date**    | 2026-04-24                                              |
| **IS**      | IS-daemons-modernisation.md (APPROVED, with amendment — see §1) |
| **HLPS**    | HLPS-daemons-modernisation.md (APPROVED)                |
| **Folder**  | docs/daemons-modernisation/                             |

---

## 1. Context

### Amendment to IS S-009
The IS specified a **new** `Dorc.Database.IntegrationTests` project. On execution review, a cheaper path was identified: the existing `Tests.Acceptance` project already meets every prerequisite — MSTest runner, live CI with SQL + API, `Microsoft.Data.SqlClient` already referenced, `DOrcConnectionString` slot in `appsettings.test.json` already wired — and merging the new tests into it costs one NuGet addition and one folder, with no new csproj to maintain. The BDD-style Reqnroll `.feature` tests and plain `[TestClass]` MSTest tests coexist in one project; the only cost is minor style inconsistency (BDD vs plain unit-test). User approved the pivot 2026-04-24.

The IS's SC→step ownership table still holds — S-009 still owns SC-02 and SC-05b verification. The **mechanism** (project location) is the only thing that changed.

### What this step addresses
Automated verification of three HLPS success criteria:

- **SC-01 migration-completeness**: after publish, legacy tables are gone and `deploy.Daemon` / `deploy.ServerDaemon` / `deploy.DaemonAudit` exist with the expected columns / constraints.
- **SC-02 populated-DB migration**: a DB with representative `dbo.SERVICE` + `dbo.SERVER_SERVICE_MAP` rows migrates cleanly. `Id` values preserved. NULL-Name and orphan-Server_ID rows filtered and logged. A repeat publish is a no-op.
- **SC-05b seed idempotency**: `deploy.RefDataAuditAction` ends up with exactly five rows regardless of whether the table was empty or pre-seeded before publish, and a project PUT (which exercises the `.First(...)` lookup) succeeds against both states.

### Scope
- `src/Tests.Acceptance/Tests.Acceptance.csproj` — add one NuGet: `Microsoft.SqlServer.DacFx`.
- `src/Tests.Acceptance/DatabaseTests/` — new folder.
  - `DatabaseTestBase.cs` — shared helpers: resolve the connection string, create and drop an ephemeral DB per test, locate the built dacpac path, invoke `DacServices.Deploy`.
  - `DaemonSchemaMigrationTests.cs` — the three tests described below.
- `src/Tests.Acceptance/DatabaseTests/Fixtures/LegacyDaemonSeed.sql` — a small SQL resource (or inline string) that recreates the legacy `dbo.SERVICE` + `dbo.SERVER_SERVICE_MAP` shape and inserts representative rows. This simulates a pre-modernisation production DB.

Out of scope:
- Any changes outside `Tests.Acceptance`. Production code is untouched.
- Replacing the existing `Tests.Acceptance` Reqnroll structure or removing any `@ignore`'d feature.
- Adding CI pipeline configuration (the existing pipeline that runs `Tests.Acceptance` is assumed to continue working; no workflow changes in this PR).

### Governing constraints
- **HLPS SC-02 verification procedure**: (a) snapshot legacy COUNT/MAX, (b) publish, (c) COUNT/MAX/EXCEPT assertion, (d) repeat publish idempotency proof. Realised as code per §2.
- **HLPS SC-05b**: two test variants — empty `RefDataAuditAction` and pre-seeded. Both must end in a working project-PUT path (the `ManageProjectsPersistentSource.InsertRefDataAudit` `.First(…)` lookup must succeed).
- **HLPS SC-08**: existing tests continue to pass.
- **U-3 fallback**: tests run against a CI-ephemeral DB provisioned by the existing `Tests.Acceptance` SQL connection. If no CI SQL is available (U-3 still unresolved), the tests can be run against a developer's LocalDB via `DOrcConnectionString` in `appsettings.test.json`.

---

## 2. Production Code Change

### 2.1 `Tests.Acceptance.csproj`

Add one NuGet reference:
```xml
<PackageReference Include="Microsoft.SqlServer.DacFx" Version="170.0.0" />
```

(The SPEC author should pick the latest stable 170.x or 162.x compatible with net8.0 at build time — 170.0.0 is the current line as of 2026-04. Resolve during execution.)

### 2.2 `DatabaseTests/DatabaseTestBase.cs`

Shared helpers for every database test:

- `protected readonly IConfiguration Config` — loaded from `appsettings.test.json` via the existing pattern in `Tests.Acceptance/Support/`.
- `protected string GetMasterConnectionString()` — takes the `DOrcConnectionString`, replaces the Initial Catalog with `master`.
- `protected string CreateEphemeralDatabase()` — returns a unique DB name (e.g. `dorc_test_{guid.Substring(0,8)}`), connects to `master`, runs `CREATE DATABASE [name]`, returns a connection string targeting it. Stored in a member field so `DropEphemeralDatabase` can clean up.
- `protected void DropEphemeralDatabase()` — called from `[TestCleanup]`; `ALTER DATABASE ... SET SINGLE_USER WITH ROLLBACK IMMEDIATE;` then `DROP DATABASE`.
- `protected string LocateDacpac()` — resolves the path to `Dorc.Database.dacpac` produced by the SSDT build. Relative to the test-output directory: `../../../../Dorc.Database/sql/debug/Dorc.Database.dacpac` or similar — the SPEC author walks up from `Tests.Acceptance.dll`'s directory to the repo root, then into `src/Dorc.Database/sql/debug/`. If the path can't be resolved at runtime, fail the test with a clear "dacpac not found" message.
- `protected void PublishDacpac(string targetConnectionString)` — uses `Microsoft.SqlServer.Dac.DacServices`:
  ```
  var dacServices = new DacServices(targetConnectionString);
  using var package = DacPackage.Load(LocateDacpac());
  dacServices.Deploy(package, targetDbName, upgradeExisting: true, options: new DacDeployOptions {
      BlockOnPossibleDataLoss = false, // allow the pre/post-deploy migration to empty legacy tables before SSDT drops them
      CreateNewDatabase = false         // we've already CREATED the ephemeral DB
  });
  ```
  Actually — `BlockOnPossibleDataLoss = false` should **not** be needed because pre-deploy empties the legacy tables before the schema phase drops them (S-002 design). Keep the flag at its default (`true`) in the test harness to prove SC-02's data-loss-safety claim is real. If the default blocks publish, the test fails — and that's a correct failure signalling the staging-table pattern isn't working.

- `protected int Scalar(string sql, string conn)` — helper to `ExecuteScalar` an `int` (used for COUNT/MAX assertions).

### 2.3 `DatabaseTests/Fixtures/LegacyDaemonSeed.sql`

A SQL resource (embedded via `Content` `CopyToOutputDirectory`, or inlined as a C# string constant) that:
1. `CREATE TABLE [dbo].[SERVICE]` with the pre-modernisation shape (columns + PK + nullable NVARCHAR(250) — mirroring the deleted `SERVICE.sql`).
2. `CREATE TABLE [dbo].[SERVER_SERVICE_MAP]` with the pre-modernisation shape.
3. Seeded `SET IDENTITY_INSERT` inserts for:
   - 3 well-formed SERVICE rows (Ids 100, 200, 300).
   - 1 row with `Service_Name IS NULL` (Id 400) — exercises the NULL filter in the post-deploy migration.
   - 3 SERVER_SERVICE_MAP rows referencing valid `Server_ID`s (from `dbo.SERVER` — assumed to have at least one row; the test harness inserts a test server if needed) pointing at Service_IDs 100, 200, 300.
   - 1 SERVER_SERVICE_MAP row pointing at an **orphan** `Server_ID` (e.g. 99999) — exercises the orphan filter.

Keep the SQL minimal — this is a fixture, not a schema clone. `dbo.SERVER` is outside daemon scope (HLPS C-03) so we have to either (a) ensure the test DB has at least one server row before seeding, or (b) insert a test server row as part of the fixture. Go with (b) for self-containment.

### 2.4 `DatabaseTests/DaemonSchemaMigrationTests.cs`

Three `[TestMethod]` tests:

**Test A — `SC01_PublishToEmptyDb_CreatesDeploySchema`**
1. Create ephemeral DB.
2. Publish dacpac.
3. Assert:
   - `OBJECT_ID('deploy.Daemon') IS NOT NULL`
   - `OBJECT_ID('deploy.ServerDaemon') IS NOT NULL`
   - `OBJECT_ID('deploy.DaemonAudit') IS NOT NULL`
   - `OBJECT_ID('dbo.SERVICE') IS NULL`
   - `OBJECT_ID('dbo.SERVER_SERVICE_MAP') IS NULL`
   - `SELECT COUNT(*) FROM deploy.RefDataAuditAction` = 5 (post-deploy seed ran).

**Test B — `SC02_PopulatedLegacyDb_MigratesCleanly`**
1. Create ephemeral DB.
2. Run `LegacyDaemonSeed.sql` to populate legacy tables.
3. Snapshot: `SELECT COUNT(*), MAX(Service_ID) FROM dbo.SERVICE` (store values).
4. Publish dacpac.
5. Assert:
   - `deploy.Daemon` COUNT = (snapshot count − 1 for NULL-name row skipped).
   - `deploy.Daemon MAX(Id) = snapshot MAX(Service_ID)` (Id preservation).
   - Row-level: `NOT EXISTS (SELECT 1 FROM deploy.Daemon WHERE Id=100) FAIL_IF_TRUE` etc. — specifically assert Ids 100, 200, 300 are present, 400 is not.
   - `deploy.ServerDaemon` COUNT = 3 (3 valid - 0 orphan was for server, - 0 skipped for daemon because the orphan-daemon filter only skips via NULL-name which would've been Id 400 but no map row points at 400 in fixture). The fixture has 3 valid + 1 orphan-Server_ID = 3 rows insertable; orphan is filtered.
   - Legacy tables absent.
6. Publish dacpac a **second** time.
7. Assert row counts on `deploy.Daemon` and `deploy.ServerDaemon` unchanged — idempotency proof.

**Test C — `SC05b_SeedIdempotency_WorksOnEmptyAndPreSeeded`**
1. Create ephemeral DB.
2. Publish dacpac (seed fires; 5 rows in `deploy.RefDataAuditAction`).
3. Snapshot: `SELECT COUNT(*) FROM deploy.RefDataAuditAction` (should be 5).
4. Run the seed SQL (from `SeedRefDataAuditActions.sql` — re-read the file content) directly a second time (simulating a repeat publish's post-deploy).
5. Assert count is still 5 — no duplicates.
6. TRUNCATE the table.
7. Run the seed SQL again.
8. Assert count is 5 — seeds from empty.
9. Assert all five expected `Action` values (`Create`, `Update`, `Delete`, `Attach`, `Detach`) are present.

### 2.5 Test-category attribute

Each `[TestMethod]` gets `[TestCategory("Database")]` so CI can run the DB tests as a named group (and skip them if no SQL is available). This matches the pattern Reqnroll uses for the feature tests.

### 2.6 Dacpac output path expectation

The SPEC author confirms `src/Dorc.Database/sql/debug/Dorc.Database.dacpac` is the output path (validated during earlier S-001 execution — MSBuild logged this path). If the Release configuration changes the path to `sql/release/`, `LocateDacpac` should probe both.

---

## 3. Branch

Continues on `feat/649-daemons-modernisation`.

---

## 4. Test Approach

The tests themselves are the test approach for SC-02 / SC-05b. The project's own gate is:

### Gate 1 — Build
`dotnet build Tests.Acceptance.csproj` succeeds after adding the DacFx NuGet.

### Gate 2 — Tests execute against an available SQL instance
With `DOrcConnectionString` pointing at a reachable SQL instance, `dotnet test --filter TestCategory=Database Tests.Acceptance.csproj` runs green.

### Gate 3 — Non-regression
Existing Reqnroll feature tests continue to work unchanged.

If the local SQL connection string is empty (as it is today in `appsettings.test.json`), the DB tests should **Assert.Inconclusive** with a clear message rather than throw — so local developer runs of the test project don't red-fail on infra they don't have.

---

## 5. Commit Strategy

Single commit is natural — the csproj edit, new folder, new helper, new test class, new fixture SQL all ship together. Two commits (csproj + fixture; then test class) acceptable.

---

## 6. Acceptance Criteria

| ID   | Criterion |
|------|-----------|
| AC-1 | `Tests.Acceptance.csproj` has a `<PackageReference Include="Microsoft.SqlServer.DacFx">` entry. |
| AC-2 | `Tests.Acceptance/DatabaseTests/DatabaseTestBase.cs` exists with the helpers in §2.2. Each helper is clearly named and unit-testable in isolation (though dedicated tests of the helpers are out of scope). |
| AC-3 | `Tests.Acceptance/DatabaseTests/Fixtures/LegacyDaemonSeed.sql` exists with the shape in §2.3. |
| AC-4 | `Tests.Acceptance/DatabaseTests/DaemonSchemaMigrationTests.cs` exists with exactly three `[TestMethod]` tests — `SC01_PublishToEmptyDb_CreatesDeploySchema`, `SC02_PopulatedLegacyDb_MigratesCleanly`, `SC05b_SeedIdempotency_WorksOnEmptyAndPreSeeded` — each tagged `[TestCategory("Database")]`. |
| AC-5 | `DatabaseTestBase` skips tests (`Assert.Inconclusive(...)`) with a clear message when `DOrcConnectionString` is empty, so local developer runs do not fail on unavailable infra. |
| AC-6 | `dotnet build src/Tests.Acceptance/Tests.Acceptance.csproj` succeeds with zero errors. |
| AC-7 | `dotnet test --filter TestCategory=Database` passes against a reachable SQL instance. If CI has the SQL available, this runs green on the PR branch. |
| AC-8 | Existing `Tests.Acceptance` feature tests (non-Database category) continue to pass, unchanged. |
| AC-9 | No changes outside `src/Tests.Acceptance/**`. |
