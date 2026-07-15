---
name: SPEC-S-007 — Daemon audit
description: JIT Specification for S-007 — new DaemonAudit entity/config, IDaemonAuditPersistentSource + impl, post-deploy seed for RefDataAuditAction, Attach/Detach enum values, audit wiring in RefDataDaemonsController + ServerDaemonsController, new DaemonAuditController read endpoint, TS regen.
type: spec
status: APPROVED
---

# SPEC-S-007 — Daemon audit

| Field       | Value                                                   |
|-------------|---------------------------------------------------------|
| **Status**  | APPROVED                                                |
| **Step**    | S-007                                                   |
| **Author**  | Agent                                                   |
| **Date**    | 2026-04-24                                              |
| **IS**      | IS-daemons-modernisation.md (APPROVED)                  |
| **HLPS**    | HLPS-daemons-modernisation.md (APPROVED)                |
| **Folder**  | docs/daemons-modernisation/                             |

---

## 1. Context

### What this step addresses
- **DF-10** — daemon mutations leave no audit trail. Other RefData areas (Projects) write to `deploy.RefDataAudit`. Daemons need an equivalent.
- **DF-12** — the existing `ManageProjectsPersistentSource.InsertRefDataAudit` relies on `deploy.RefDataAuditAction` rows that are not seeded in the repo; the project-audit path is latent-broken. The post-deploy seed in this step fixes it as a side effect without touching project-audit source code.

S-007 introduces the daemon audit end-to-end:
- New `deploy.DaemonAudit` entity/config bound to the table created in S-001.
- `ActionType` enum extended with `Attach` and `Detach`.
- Idempotent post-deploy seed of `deploy.RefDataAuditAction` rows (`Create`, `Update`, `Delete`, `Attach`, `Detach`), shared with the existing project-audit path.
- New `IDaemonAuditPersistentSource` / `DaemonAuditPersistentSource` with `InsertDaemonAudit` and paged `GetDaemonAudit`.
- Audit writes wired into `RefDataDaemonsController` (Create/Update/Delete) and `ServerDaemonsController` (Attach/Detach).
- New `DaemonAuditController` GET `/DaemonAudit?daemonId=…`, open to any authenticated user (U-11 resolution).
- TS regen for the new `DaemonAuditApi` and `DaemonAuditApiModel`.

### Scope
**C# / DB**:
- `src/Dorc.PersistentData/Model/DaemonAudit.cs` — new.
- `src/Dorc.PersistentData/EntityTypeConfigurations/DaemonAuditEntityTypeConfiguration.cs` — new (fluent `.ToTable(name, schema)`).
- `src/Dorc.PersistentData/Model/RefDataAuditAction.cs` — extend `ActionType` with `Attach`, `Detach`.
- `src/Dorc.PersistentData/Sources/Interfaces/IDaemonAuditPersistentSource.cs` — new.
- `src/Dorc.PersistentData/Sources/DaemonAuditPersistentSource.cs` — new.
- `src/Dorc.PersistentData/Contexts/DeploymentContext.cs` — add `DbSet<DaemonAudit>`; wire config in `OnModelCreating`.
- `src/Dorc.PersistentData/Contexts/IDeploymentContext.cs` — add `DbSet<DaemonAudit>`.
- `src/Dorc.PersistentData/PersistentDataRegistry.cs` — register `IDaemonAuditPersistentSource`.
- `src/Dorc.ApiModel/DaemonAuditApiModel.cs` — new.
- `src/Dorc.ApiModel/GetDaemonAuditListResponseDto.cs` — new.
- `src/Dorc.Api/Controllers/DaemonAuditController.cs` — new read endpoint.
- `src/Dorc.Api/Controllers/RefDataDaemonsController.cs` — inject `IDaemonAuditPersistentSource` + `IClaimsPrincipalReader`; write audit rows after successful Add/Update/Delete.
- `src/Dorc.Api/Controllers/ServerDaemonsController.cs` — same, for Attach/Detach.
- `src/Dorc.Database/Scripts/Post-Deployment/SeedRefDataAuditActions.sql` — new.
- `src/Dorc.Database/Scripts/Post-Deployment/Script.PostDeployment.sql` — add `:r .\SeedRefDataAuditActions.sql`.
- `src/Dorc.Database/Dorc.Database.sqlproj` — add `<None Include>` for the new SQL.

**TS regen**:
- `src/dorc-web/src/apis/dorc-api/models/DaemonAuditApiModel.ts` — new.
- `src/dorc-web/src/apis/dorc-api/models/GetDaemonAuditListResponseDto.ts` — new.
- `src/dorc-web/src/apis/dorc-api/apis/DaemonAuditApi.ts` — new.
- `src/dorc-web/src/apis/dorc-api/models/index.ts` + `apis/index.ts` — add exports.
- `src/dorc-web/src/apis/dorc-api/swagger.json` — add endpoint and schemas.

**Out of scope**:
- UI audit-view component — S-008.
- Test project `Dorc.Database.IntegrationTests` — S-009 creates it; SC-05 / SC-05b tests are written there.
- Any change to `ManageProjectsPersistentSource.InsertRefDataAudit` source code. Its fix comes free from the seed alone.
- `RefDataAuditAction.cs` uses the `[Table("deploy.RefDataAuditAction")]` anti-pattern. Not renamed here to avoid widening scope — it's pre-existing, not introduced by this work. New classes (`DaemonAudit`) use the fluent pattern.

### Governing constraints
- **HLPS SC-05**: audit payloads per action follow the HLPS SC-05 table. Update rows skipped when `FromValue == ToValue`.
- **HLPS SC-05b**: post-deploy seed is idempotent; handles both empty and pre-seeded states.
- **HLPS C-07**: `deploy.RefDataAuditAction` is a stable seed table; the seed is additive-only (`INSERT WHERE NOT EXISTS`) and must not cause IDENTITY churn.
- **HLPS U-4 resolution**: application code must **not** attempt to insert action rows — race-unsafe. The seed is the only write path to `RefDataAuditAction`.
- **HLPS U-11 resolution**: `DaemonAuditController` read endpoint gated on `[Authorize]` only (authenticated users), mirroring `RefDataProjectAuditController`.
- **Round 2 review R2-L3**: new entity configs use fluent `.ToTable("DaemonAudit", "deploy")` — no `[Table(...)]` attribute anti-pattern.
- **IS S-007 Dependencies**: depends on **S-001** (schema), **S-004** (controller `try/catch` structure), **S-006** (role-checker already injected into the daemon controllers).

---

## 2. Production Code Change

### 2.1 `DaemonAudit.cs` (new entity)

Properties:
- `Id` (`long`) — PK.
- `DaemonId` (`int?`) — nullable; no navigation property (HLPS SD-1: audit rows must survive daemon deletion).
- `RefDataAuditActionId` (`int`) — FK.
- `Action` (`RefDataAuditAction`) — navigation; required.
- `Username` (`string`) — `null!` initializer style (match `RefDataAudit`).
- `Date` (`DateTime`).
- `FromValue` (`string?`) — nullable.
- `ToValue` (`string?`) — nullable.

**No `[Table(...)]` attribute** — the fluent config handles this.

### 2.2 `DaemonAuditEntityTypeConfiguration.cs` (new)

Using `IEntityTypeConfiguration<DaemonAudit>`:
- `builder.ToTable("DaemonAudit", "deploy");`
- `builder.HasOne(x => x.Action).WithMany().HasForeignKey(x => x.RefDataAuditActionId).IsRequired();` — note `.WithMany()` without a nav-property argument: the `RefDataAuditAction.RefDataAudits` nav collection is project-audit-specific; we **don't** add a new nav for daemon audits back on `RefDataAuditAction` (would require a second collection like `DaemonAudits`, which isn't needed for querying). Using parameterless `.WithMany()` tells EF to configure the FK without requiring an inverse collection.
- Primary key is inferred on `Id` by EF convention.

### 2.3 `RefDataAuditAction.cs` — extend `ActionType`

Add `Attach` and `Detach` values to the `ActionType` enum. Existing order (`Create`, `Update`, `Delete`) preserved. The enum persists as string via `EnumToStringConverter` in `RefDataAuditActionConfiguration`, so adding values is additive (no stored-value reshuffling).

### 2.4 `IDaemonAuditPersistentSource.cs` + `DaemonAuditPersistentSource.cs` (new)

Interface:
- `void InsertDaemonAudit(string username, ActionType action, int? daemonId, string? fromValue, string? toValue);`
- `GetDaemonAuditListResponseDto GetDaemonAuditByDaemonId(int daemonId, int limit, int page, PagedDataOperators operators);`

Implementation (mirrors `ManageProjectsPersistentSource.InsertRefDataAudit` + `GetRefDataAuditByProjectId`):
- `InsertDaemonAudit`:
  - If `action == Update && fromValue == toValue`, early return (skip write, matching `ScriptsAuditPersistentSource.AddRecord`).
  - Open context; look up `RefDataAuditAction` via `context.RefDataAuditActions.First(x => x.Action == action)` — guaranteed to succeed post-seed.
  - Construct `DaemonAudit` with `Date = DateTime.Now`, populated fields.
  - `context.DaemonAudits.Add(audit); context.SaveChanges();`.
- `GetDaemonAuditByDaemonId`:
  - Query `context.DaemonAudits.Include(a => a.Action).Where(a => a.DaemonId == daemonId)`.
  - Apply `PagedDataOperators` filters/sort with the same pattern as `GetRefDataAuditByProjectId` (copy the `WhereAll` / `OrderEntries` / `GetExpressionForOrdering` helpers, or extract them into a shared utility — the shared extraction is tempting but is out of scope; duplicate-for-now is acceptable since both methods live in `Dorc.PersistentData.Sources`).
  - Default ordering: `OrderByDescending(s => s.Date)` if no explicit sort.
  - Return a `GetDaemonAuditListResponseDto` with paged items projected into `DaemonAuditApiModel`.

### 2.5 `DeploymentContext.cs` + `IDeploymentContext.cs`

- Add `public DbSet<DaemonAudit> DaemonAudits { get; set; }` (and its interface counterpart).
- In `OnModelCreating`: `new DaemonAuditEntityTypeConfiguration().Configure(modelBuilder.Entity<DaemonAudit>());`.

### 2.6 `PersistentDataRegistry.cs`

Add one line: `For<IDaemonAuditPersistentSource>().Use<DaemonAuditPersistentSource>().Scoped();`. Alphabetical order puts it after `IDaemonsPersistentSource`.

### 2.7 `DaemonAuditApiModel.cs` + `GetDaemonAuditListResponseDto.cs`

`DaemonAuditApiModel` shape:
- `long Id`
- `int? DaemonId`
- `int RefDataAuditActionId`
- `string Action` — stringified `ActionType`, same pattern as `RefDataAuditApiModel.Action`.
- `string Username`
- `DateTime Date`
- `string FromValue`
- `string ToValue`

`GetDaemonAuditListResponseDto` — identical shape to `GetRefDataAuditListResponseDto` but with `List<DaemonAuditApiModel> Items`.

### 2.8 `DaemonAuditController.cs` (new)

Per HLPS:
```
[Authorize]
[ApiController]
[Route("[controller]")]
public sealed class DaemonAuditController : ControllerBase
{
    private readonly IDaemonAuditPersistentSource _daemonAuditPersistentSource;
    // ctor injects the source
    [HttpPut]  // matches RefDataProjectAuditController's PUT-for-paged-read convention
    [SwaggerResponse(200, Type = typeof(GetDaemonAuditListResponseDto))]
    public IActionResult Put(int daemonId, [FromBody] PagedDataOperators operators, int page = 1, int limit = 50)
    {
        return StatusCode(200, _daemonAuditPersistentSource.GetDaemonAuditByDaemonId(daemonId, limit, page, operators));
    }
}
```

HTTP-verb note: the project audit uses `PUT` (`RefDataProjectAuditController.cs`) as an RPC-style paged-read that accepts a body (filters/sort). It's an unusual REST choice but matching the existing convention avoids inventing a new shape. Later consistency cleanup can be a separate PR.

### 2.9 Audit wiring — `RefDataDaemonsController`

Inject `IDaemonAuditPersistentSource` and `IClaimsPrincipalReader` alongside the existing two dependencies. Wire:

- **POST (Create)** — after the successful `Add` returns `daemon`, before `Ok(daemon)`:
  ```
  var user = _claimsPrincipalReader.GetUserFullDomainName(User);
  _daemonAuditPersistentSource.InsertDaemonAudit(
      user, ActionType.Create, daemon.Id,
      fromValue: null,
      toValue: JsonSerializer.Serialize(daemon, new JsonSerializerOptions { WriteIndented = true }));
  ```
- **PUT (Update)** — resolve the `before` daemon via `GetDaemons().FirstOrDefault(d => d.Id == model.Id)` (or equivalent; can use `context` if preferred, but via the persistent source keeps layering clean) **before** calling `Update`; serialize. Then call `Update`, serialize the result (`after`). Call `InsertDaemonAudit(user, Update, model.Id, fromValue: beforeJson, toValue: afterJson)` — the persistent source handles the `fromValue == toValue` skip.
- **DELETE** — fetch the `before` daemon before deleting (same query). If `Delete` returns true, call `InsertDaemonAudit(user, Delete, id, fromValue: beforeJson, toValue: null)`. If `Delete` returns false (404), do **not** write an audit row.

### 2.10 Audit wiring — `ServerDaemonsController`

Inject `IDaemonAuditPersistentSource` and `IClaimsPrincipalReader`. Wire:

- **Attach (POST)** — after `AttachDaemonToServer` returns true:
  ```
  var payload = JsonSerializer.Serialize(new { ServerId = serverId, DaemonId = daemonId });
  _daemonAuditPersistentSource.InsertDaemonAudit(user, ActionType.Attach, daemonId,
      fromValue: null, toValue: payload);
  ```
- **Detach (DELETE)** — after `DetachDaemonFromServer` returns true:
  ```
  var payload = JsonSerializer.Serialize(new { ServerId = serverId, DaemonId = daemonId });
  _daemonAuditPersistentSource.InsertDaemonAudit(user, ActionType.Detach, daemonId,
      fromValue: payload, toValue: null);
  ```

No audit row on the 403 or 404 paths — only successful mutations.

### 2.11 `SeedRefDataAuditActions.sql` (new)

Idempotent, runs on every publish:
```sql
INSERT INTO [deploy].[RefDataAuditAction] (Action)
SELECT v.Action
FROM (VALUES (N'Create'), (N'Update'), (N'Delete'), (N'Attach'), (N'Detach')) AS v(Action)
WHERE NOT EXISTS (
    SELECT 1 FROM [deploy].[RefDataAuditAction] a WHERE a.Action = v.Action
);
PRINT 'RefDataAuditAction seed complete (' + CAST((SELECT COUNT(*) FROM [deploy].[RefDataAuditAction]) AS VARCHAR(10)) + ' rows present)';
```

Wired via `:r .\SeedRefDataAuditActions.sql` into `Script.PostDeployment.sql`, placed **after** `MigrateStagedServicesToDaemons.sql` and **before** `CleanupOrphanedScripts.sql`:
```
:r .\MigrateStagedServicesToDaemons.sql
:r .\SeedRefDataAuditActions.sql
:r .\CleanupOrphanedScripts.sql
```

Rationale for ordering: the migration must land rows first; the seed is independent but benefits from running after any migration in case a future migration depends on action rows existing. Cleanup runs last.

`<None Include>` added to sqlproj.

### 2.12 TS regeneration

Steps:
1. Hand-edit `src/dorc-web/src/apis/dorc-api/swagger.json`: add the new `DaemonAuditApiModel`, `GetDaemonAuditListResponseDto` schemas, and the `/DaemonAudit` PUT endpoint definition (mirror `/RefDataProjectAudit`).
2. From `src/dorc-web/`: `npm run dorc-api-gen`.
3. Expected new files: `models/DaemonAuditApiModel.ts`, `models/GetDaemonAuditListResponseDto.ts`, `apis/DaemonAuditApi.ts`; updates to `models/index.ts` and `apis/index.ts`.

If codegen is unavailable locally, hand-write the three new TS files matching the generated style (see `ServiceStatusApiModel.ts` (now `DaemonStatusApiModel.ts`) for the template).

---

## 3. Branch

Continues on `feat/649-daemons-modernisation`.

---

## 4. Test Approach

### Rationale
S-007 introduces observable behaviour (audit rows written per mutation) but the layer-level tests live in the audit persistent source and controllers. The DB-level migration/seed tests live in the new `Dorc.Database.IntegrationTests` project — created in S-009. For S-007 in isolation, the gate is compile + audit-write unit tests.

### Test 1 — `DaemonAuditPersistentSource.InsertDaemonAudit` — Create action writes expected row (unit, new)
Mock or use an in-memory `IDeploymentContext`. Call `InsertDaemonAudit(user, Create, daemonId, null, someJson)`. Assert a row is added with the expected `RefDataAuditActionId` (looked up by enum value), `FromValue = null`, `ToValue = someJson`, `DaemonId`, `Username`, `Date ~= now`.

### Test 2 — `InsertDaemonAudit` — Update with unchanged values is skipped (unit, new)
Call with `ActionType.Update`, `fromValue == toValue`. Assert no row is added.

### Test 3 — `InsertDaemonAudit` — Update with changed values writes row (unit, new)
Call with `ActionType.Update`, different from/to. Assert row added with both values.

### Test 4 — Controller wiring — POST writes Create audit (controller test, new)
Mock `IDaemonAuditPersistentSource`. POST a daemon. Assert `InsertDaemonAudit` was called exactly once with `ActionType.Create`, `fromValue = null`, `toValue` containing the new daemon's Name/DisplayName.

### Test 5 — Controller wiring — DELETE writes Delete audit on success (controller test, new)
Mock. DELETE an existing daemon. Assert `InsertDaemonAudit` called once with `ActionType.Delete`, `fromValue` populated, `toValue = null`.

### Test 6 — Controller wiring — DELETE on missing daemon writes no audit (controller test, new)
Mock. DELETE a non-existent id (403/404 path). Assert `InsertDaemonAudit` was **not** called.

### Test 7 — `ServerDaemonsController.Attach/Detach` wiring (controller tests, new)
Mock. Attach on success writes `ActionType.Attach`. Detach on success writes `ActionType.Detach`. 403 / 404 paths do not audit.

### Test 8 — `GetDaemonAuditByDaemonId` returns page ordered DESC by Date (unit, new)
Insert 3 audit rows with different dates; call the method. Assert descending order and pagination metadata.

### Test 9 — Compile gate
`dotnet build Dorc.Api.csproj` succeeds.

### Test note — DB-level seed + migration tests
`Dorc.Database.IntegrationTests` project (created in S-009) carries the SC-02 + SC-05b DB-level tests. Not authored in S-007.

---

## 5. Commit Strategy

Two to three commits is natural:
1. DB + model: `DaemonAudit.cs`, `DaemonAuditEntityTypeConfiguration.cs`, `ActionType` extension, DeploymentContext wiring, sqlproj + seed script.
2. Persistent source + DI + DTOs.
3. Controllers + `DaemonAuditController` + TS regen.

One or two commits is also fine. No value in more than three for reviewability.

---

## 6. Acceptance Criteria

| ID   | Criterion |
|------|-----------|
| AC-1 | `src/Dorc.PersistentData/Model/DaemonAudit.cs` exists with the shape in §2.1. No `[Table(...)]` attribute. |
| AC-2 | `src/Dorc.PersistentData/EntityTypeConfigurations/DaemonAuditEntityTypeConfiguration.cs` exists; uses fluent `.ToTable("DaemonAudit", "deploy")`; FK to `RefDataAuditAction` configured with `.IsRequired()`. |
| AC-3 | `ActionType` enum has `Attach` and `Detach` values in addition to existing `Create`/`Update`/`Delete`. |
| AC-4 | `IDaemonAuditPersistentSource` / `DaemonAuditPersistentSource` exist with `InsertDaemonAudit` and `GetDaemonAuditByDaemonId` per §2.4. `InsertDaemonAudit` skips writes when `action == Update && fromValue == toValue`. |
| AC-5 | `DeploymentContext.DaemonAudits` DbSet exists; `IDeploymentContext` updated; `OnModelCreating` wires `DaemonAuditEntityTypeConfiguration`. |
| AC-6 | `PersistentDataRegistry` registers `IDaemonAuditPersistentSource` → `DaemonAuditPersistentSource` (scoped). |
| AC-7 | `DaemonAuditApiModel` and `GetDaemonAuditListResponseDto` exist in `Dorc.ApiModel`. |
| AC-8 | `DaemonAuditController` exists; `[Authorize]` class-level; no PowerUser/Admin role gate (per U-11). Reads use the PUT pattern from `RefDataProjectAuditController`. |
| AC-9 | `RefDataDaemonsController` writes audit rows on successful POST/PUT/DELETE with payloads per HLPS SC-05. Update with unchanged values produces no audit row (via source-level skip). 403/404 paths write no audit. |
| AC-10 | `ServerDaemonsController` writes audit rows on successful Attach/Detach with `{ ServerId, DaemonId }` payload per HLPS SC-05. 403/404 paths write no audit. |
| AC-11 | `Scripts/Post-Deployment/SeedRefDataAuditActions.sql` exists with idempotent `INSERT … WHERE NOT EXISTS` seeding five rows. Wired into `Script.PostDeployment.sql` between the migration and cleanup scripts. `<None Include>` added to sqlproj. |
| AC-12 | `swagger.json`, `DaemonAuditApiModel.ts`, `GetDaemonAuditListResponseDto.ts`, `DaemonAuditApi.ts`, `models/index.ts`, `apis/index.ts` updated. Regenerated via `npm run dorc-api-gen` or hand-written to match. `npx tsc --noEmit -p tsconfig.json` passes. |
| AC-13 | `dotnet build Dorc.Api.csproj` succeeds with zero errors. `Dorc.PersistentData` builds clean. |
| AC-14 | Tests 1–8 authored per §4 (in `Dorc.Api.Tests/Controllers/` for controller cases, `Dorc.Api.Tests/` or a new folder for persistent-source unit tests). CI-authoritative per the pre-existing `System.DirectoryServices.Fakes` block. |
| AC-15 | No changes outside the scope files listed in §1. In particular, `ManageProjectsPersistentSource.cs` is **not** modified — the post-deploy seed alone fixes DF-12. |
