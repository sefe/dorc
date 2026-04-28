---
name: SPEC-S-004 — DaemonsPersistentSource correctness and 409 translation
description: JIT Specification for S-004 — Update persists Name (DF-1); Delete clears nav collection (DF-2); Add pre-checks for duplicates and throws DaemonDuplicateException (DF-3); controller translates to 409.
type: spec
status: APPROVED
---

# SPEC-S-004 — DaemonsPersistentSource correctness and 409 translation

| Field       | Value                                                   |
|-------------|---------------------------------------------------------|
| **Status**  | APPROVED                                                |
| **Step**    | S-004                                                   |
| **Author**  | Agent                                                   |
| **Date**    | 2026-04-24                                              |
| **IS**      | IS-daemons-modernisation.md (APPROVED)                  |
| **HLPS**    | HLPS-daemons-modernisation.md (APPROVED)                |
| **Folder**  | docs/daemons-modernisation/                             |

---

## 1. Context

### What this step addresses
Three correctness bugs in the daemon CRUD path, plus one tactical cleanup:
- **DF-1** — `DaemonsPersistentSource.Update` (current lines 107–132) copies `ServiceType`, `AccountName`, `DisplayName` but **not** `Name`. A PUT that renames a daemon returns `200` with the renamed payload, but the DB is unchanged.
- **DF-2** — `DaemonsPersistentSource.Delete` (current lines 91–105) does `Find` + `Remove` + `SaveChanges` without clearing the `Server` nav collection. Today, with legacy `dbo.SERVER_SERVICE_MAP` and no FK cascade, this throws `DbUpdateException` on the FK constraint and bubbles out as an opaque 500. With S-001's `FK_ServerDaemon_Daemon ON DELETE CASCADE`, the DB resolves the cascade for us — but EF-level defence-in-depth is added anyway so the fix is robust against a future migration accidentally dropping the cascade.
- **DF-3** — `DaemonsPersistentSource.Add` (current lines 73–89) does no pre-check against `UQ_Daemon_Name` / `UQ_Daemon_DisplayName`. A duplicate triggers a DB constraint violation that bubbles out as a 500. Must become a typed domain exception that the controller translates into `409 Conflict` with a readable message.
- **Minor cleanup** — `Add`'s post-save `Where(d => d.Name.Equals(…)).First()` is redundant: `SaveChanges` populates `mapToDatabase.Id` from SQL Server's IDENTITY. Simplify to `return Map(mapToDatabase)`.

### Scope
Three files for the defect fixes:
- `src/Dorc.PersistentData/Sources/DaemonsPersistentSource.cs` — `Add`, `Update`, `Delete` method bodies.
- `src/Dorc.PersistentData/Exceptions/DaemonDuplicateException.cs` — **new file**, new folder (the folder does not exist today).
- `src/Dorc.Api/Controllers/RefDataDaemonsController.cs` — the `Post` action becomes a block-bodied method with a per-action `try/catch`.

**Amendment (2026-04-24, mid-execution)**: `Services` → `Daemons` internal rename folded into this SPEC at user request. The DbSet `IDeploymentContext.Services` (of type `DbSet<Daemon>`) and the navigation `Server.Services` (of type `ICollection<Daemon>`) are grab-bag legacy names that predate the `SERVICE` → `Daemon` schema rename. Neither is on the wire contract. Additional files in scope:
- `src/Dorc.PersistentData/Contexts/IDeploymentContext.cs` — `DbSet<Daemon> Services` → `DbSet<Daemon> Daemons`.
- `src/Dorc.PersistentData/Contexts/DeploymentContext.cs` — same.
- `src/Dorc.PersistentData/Model/Server.cs` — `ICollection<Daemon> Services` → `ICollection<Daemon> Daemons`.
- `src/Dorc.PersistentData/EntityTypeConfigurations/ServerEntityTypeConfiguration.cs` — `.HasMany(s => s.Services)` → `.HasMany(s => s.Daemons)`.
- `src/Dorc.PersistentData/Sources/ServersPersistentSource.cs` — two call-site updates (`server.Services.ToList()`, `server.Services.Remove(daemon)`).
- All call sites inside `DaemonsPersistentSource.cs` — `context.Services` → `context.Daemons`, `server.Services` / `s.Services` / `d.Services` → `.Daemons`. Roughly 15 updates.

Explicitly **not** renamed: `VariableValueServers.Services` (Runner wire contract), `EnvironmentContentWindowsServicesApiModel.WindowsServices` (different concept), `builder.Services` (ASP.NET DI container), `Dorc.Api.Services` namespace.

Out of scope:
- `AttachDaemonToServer` and `DetachDaemonFromServer` in the same source file (current lines 134–166) are correct and unchanged.
- `DiscoverAndMapDaemonsForServer` — deleted entirely in S-005, not touched here.
- The `IDaemonsPersistentSource` interface — no signature changes.
- The `Put` and `Delete` controller actions — already handle their cases correctly (Put returns 200 with whatever Update returns; Delete already returns 404 on not-found). No changes needed.
- RBAC gates — S-006 adds those.
- Audit writes — S-007 adds those inside `Post` alongside the catch.

### Governing constraints
- **HLPS SC-03**: every defect in §2 has a regression test at the layer the defect lives in.
- **HLPS SC-07**: on failure the UI sees `403` / `409` with a message it can display, not a generic opaque 500.
- **HLPS C-02**: no breaking change to the `DaemonApiModel` wire contract. This step preserves the existing POST body / response shape and only changes HTTP status codes on failure.
- **Round 1 review (R2-H3)**: `DaemonDuplicateException` lives in `Dorc.PersistentData.Exceptions`, consistent with `Dorc.Core.Exceptions.WrongComponentsException`. Translation is **per-action try/catch**, not via `DefaultExceptionHandler`.
- **Round 1 review (R2-M1)**: all DF-1/2/3 fixes are layer-assigned explicitly in this SPEC (persistence / controller / both).

---

## 2. Production Code Change

### 2.1 `DaemonDuplicateException.cs` (new)

**Target**: new file `src/Dorc.PersistentData/Exceptions/DaemonDuplicateException.cs`. Create the `Exceptions/` folder — it does not exist today.

**Shape**: a straightforward exception type consistent with `Dorc.Core.Exceptions.WrongComponentsException`:
- `[Serializable]` attribute.
- Three constructors: default `()`, `(string message)`, `(string message, Exception inner)`.
- Namespace `Dorc.PersistentData.Exceptions`.

No special properties (like `ConflictingField` / `ConflictingValue`). The message carries the human-readable explanation; the controller pastes it into the 409 body. If richer structured error surfacing is wanted later, it can be added without breaking callers.

### 2.2 `DaemonsPersistentSource.Add` — pre-check + cleanup

**Current behaviour**: blindly calls `context.Services.Add(mapToDatabase)`, `SaveChanges()`, then re-queries by `Name` and maps.

**New behaviour**:
1. Inside the `using` block, **before** `Add`, query `context.Services.Any(d => d.Name == daemonApiModel.Name)`. If true, throw `DaemonDuplicateException("A daemon with Name '<value>' already exists")`.
2. If `daemonApiModel.DisplayName` is non-null and non-empty, query `context.Services.Any(d => d.DisplayName == daemonApiModel.DisplayName)`. If true, throw `DaemonDuplicateException("A daemon with DisplayName '<value>' already exists")`.
3. `Add`, `SaveChanges` — unchanged.
4. Replace the post-save re-query with `return Map(mapToDatabase)`. After `SaveChanges`, EF has populated `mapToDatabase.Id` from SQL Server's IDENTITY; no round-trip required.

**Race note**: between the `Any(…)` pre-check and the `SaveChanges`, another transaction could insert a colliding row. The DB's `UQ_Daemon_Name` / `UQ_Daemon_DisplayName` unique indexes are the authoritative guard — a rare race produces `DbUpdateException` which the controller can catch as a fallback (covered in §2.5). The application-level pre-check converts the common case into a clean typed exception without an exception-on-happy-path cost.

### 2.3 `DaemonsPersistentSource.Update` — include `Name`

**Current behaviour**: copies `ServiceType`, `AccountName`, `DisplayName`. Does **not** copy `Name`.

**New behaviour**: add `existingDaemon.Name = updatedDaemon.Name;` alongside the other three assignments. No other change to `Update`.

**Not addressed here**: what if a rename collides with an existing `Name`? The DB's `UQ_Daemon_Name` unique index rejects it on `SaveChanges`, producing `DbUpdateException` → opaque 500 via the default pipeline. Making Update's collision handling as clean as Add's is **out of scope for S-004** — it would widen the change. Tracked as a follow-up; today's behaviour for rename-collision is unchanged (still a 500).

### 2.4 `DaemonsPersistentSource.Delete` — clear nav collection

**Current behaviour**: `Find` + `Remove` + `SaveChanges`. The nav collection is not loaded; EF issues `DELETE FROM deploy.Daemon WHERE Id = ?` and relies on the DB cascade (new after S-001).

**New behaviour**: eagerly load the server mappings before the delete, clear them in EF, then delete.
1. Replace `context.Services.Find(daemonApiModelId)` with a query that eager-loads the nav: `context.Services.Include(d => d.Server).FirstOrDefault(d => d.Id == daemonApiModelId)`.
2. If the daemon exists, call `daemon.Server.Clear()` — EF tracks this as deletes against the shadow join entity.
3. Then `context.Services.Remove(daemon)` and `SaveChanges`. EF issues the map-row deletes first, then the daemon delete. The DB's `ON DELETE CASCADE` would catch any map rows EF missed, but with the explicit clear EF sees a clean delete with zero child rows.

Why both EF-level and DB-level defence: if someone later authors a migration that weakens the cascade (e.g., changes to `ON DELETE NO ACTION`), the EF-level clear keeps `Delete` working without the DB layer doing the cleanup. Robust against a single point of accidental change.

### 2.5 `RefDataDaemonsController.Post` — 409 translation

**Current behaviour**: expression-bodied, `Post([FromBody] DaemonApiModel model) => Ok(_daemonsPersistentSource.Add(model));`. Any exception bubbles up to the default pipeline and yields a 500.

**New behaviour**: block-bodied with a per-action `try/catch (DaemonDuplicateException ex)` returning `Conflict(ex.Message)` (HTTP 409 with the message as body). Add a Swagger response annotation for 409.

Also catch `DbUpdateException` where the inner exception indicates a unique-index violation (SQL Server error number 2601 or 2627), translating to 409 with a generic "Daemon Name or DisplayName conflicts with an existing row" message. This handles the narrow race window between the Add pre-check and SaveChanges described in §2.2. Don't catch all `DbUpdateException` — only rethrow unless the inner error number matches.

The `using Dorc.PersistentData.Exceptions;` using directive must be added.

No change to `Get`, `Put`, or `Delete` controller actions.

---

## 3. Branch

Continues on `feat/649-daemons-modernisation`.

---

## 4. Test Approach

### Rationale
All three fixes have observable behaviour changes at clearly-defined layers. Tests live where the behaviour is: unit tests on `DaemonsPersistentSource` for DF-1 and DF-2 (persistence-layer assertions); controller tests on `RefDataDaemonsController.Post` for DF-3 (HTTP 409 translation).

Pre-existing `Dorc.Api.Tests` project has daemon-controller tests today. The SPEC author adds the new regressions there alongside.

### Test 1 — DF-1 regression: Update persists Name (unit, new)
Create a daemon with `Name = "daemon-a"`, call `Update` with `Name = "daemon-b"`, re-read by Id, assert `Name == "daemon-b"`.

**Pass**: `Name` persists. Pre-fix this test fails (value stays `"daemon-a"`).

### Test 2 — DF-2 regression: Delete cascades server mappings (integration, new)
Create a daemon, attach it to two test servers, call `Delete`, re-read both server's `Services`, assert the daemon is gone from both. Also assert the daemon itself is deleted. Do **not** delete the servers — they should remain intact.

**Pass**: daemon gone, mappings gone, servers intact.

### Test 3 — DF-3 regression: Add with duplicate Name returns 409 (controller, new)
POST a daemon with `Name = "duplicate-test"`; assert 200. POST again with the same `Name` (any other fields); assert 409 with a response body containing `"duplicate-test"` and the word `"Name"`.

**Pass**: second POST returns 409 with actionable message.

### Test 4 — DF-3 regression: Add with duplicate DisplayName returns 409 (controller, new)
Same as Test 3 but collision on `DisplayName` (with distinct `Name` values).

**Pass**: second POST returns 409 with message containing `"DisplayName"`.

### Test 5 — Add race-fallback: DbUpdateException → 409 (controller, new)
Harder to exercise deterministically in a unit test because it requires the pre-check to miss. Simulate by mocking `IDaemonsPersistentSource.Add` to throw `DbUpdateException` with an inner `SqlException` of error number 2601. Controller returns 409 with the generic message.

**Pass**: 409 on the simulated race; any other `DbUpdateException` (unrelated) rethrows and hits the default pipeline.

### Test 6 — Existing happy-path tests (regression, pre-existing)
Every existing `RefDataDaemonsControllerTests` test that exercises `Post` on a non-colliding body must still return 200. Every existing `Delete` and `Update` test must still pass. The cleanup in `Add` (removing the post-save re-query) must not change the returned payload shape — the new `Id` is populated from `SaveChanges`, same as before.

**Pass**: no regression in the existing daemon-controller suite.

### Test note — pre-existing build issue in `Dorc.Api.Tests`
During S-003 execution the `Dorc.Api.Tests` project failed to compile due to a pre-existing `System.DirectoryServices.Fakes` reference gap (unrelated to daemon work). The SPEC author must confirm during S-004 whether the test project compiles on the CI environment (Fakes are typically available there) or is blocked locally. Tests 1–5 above are authored regardless; if local execution is blocked, CI is the authoritative gate.

### Existing tests
No behavioural change in `AttachDaemonToServer`, `DetachDaemonFromServer`, or `DiscoverAndMapDaemonsForServer` in S-004 (the last is deleted in S-005). Existing tests for those methods must still pass.

---

## 5. Commit Strategy

Reasonable splits:
- **One commit** covering the exception type + source fixes + controller catch. Coherent and small.
- **Two commits** if preferred: (a) add exception type + source fixes; (b) controller catch + swagger annotation. Each is individually reviewable.

No logical reason for more than two commits.

---

## 6. Acceptance Criteria

| ID   | Criterion |
|------|-----------|
| AC-1 | `src/Dorc.PersistentData/Exceptions/DaemonDuplicateException.cs` exists in a new `Exceptions/` folder, namespaced `Dorc.PersistentData.Exceptions`, with three constructors matching the `WrongComponentsException` pattern. `[Serializable]` attribute applied. |
| AC-2 | `DaemonsPersistentSource.Add` checks for existing `Name` and (when present) `DisplayName` collisions **before** `Add`/`SaveChanges`, throwing `DaemonDuplicateException` with a message naming the conflicting field and value. |
| AC-3 | `DaemonsPersistentSource.Add` returns `Map(mapToDatabase)` directly after `SaveChanges`; the prior post-save `Where(...).First()` re-query is removed. |
| AC-4 | `DaemonsPersistentSource.Update` copies `Name` from the incoming model to the tracked entity, alongside the existing `ServiceType` / `AccountName` / `DisplayName` copies. |
| AC-5 | `DaemonsPersistentSource.Delete` eager-loads the daemon's `Server` nav collection via `.Include(d => d.Server)`, calls `.Clear()` on it, then removes the daemon and saves. `Find` is replaced with `FirstOrDefault(d => d.Id == id)`. |
| AC-6 | `RefDataDaemonsController.Post` is block-bodied with a `try/catch (DaemonDuplicateException ex)` returning `Conflict(ex.Message)`. A secondary catch translates `DbUpdateException` with inner SQL error number 2601 or 2627 to 409 with a generic conflict message; any other `DbUpdateException` rethrows. Swagger response annotation for 409 is added. |
| AC-7 | All six tests from §4 pass (Tests 1–5 new; Test 6 pre-existing). If local build of `Dorc.Api.Tests` is blocked by the unrelated `System.DirectoryServices.Fakes` issue, CI is the authoritative gate and its green status suffices. |
| AC-8 | `Dorc.PersistentData` and `Dorc.Api` projects build clean (0 errors). The `IDaemonsPersistentSource` interface signature is unchanged. |
| AC-9 | No changes outside `src/Dorc.PersistentData/` and `src/Dorc.Api/Controllers/RefDataDaemonsController.cs`. |
| AC-10 | The existing daemon-controller happy-path tests (`Get`, non-colliding `Post`, `Put`, `Delete` on existent id, `Delete` on non-existent id → 404) continue to pass. |
