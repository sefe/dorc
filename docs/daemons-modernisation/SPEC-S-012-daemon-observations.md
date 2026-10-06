---
name: SPEC-S-012 — Daemon observations (last-seen tracking)
description: JIT Specification for S-012 — new deploy.DaemonObservation table, persistent source, probe-path write, Last Seen column on daemons list, DaemonObservationController read endpoint.
type: spec
status: APPROVED
---

# SPEC-S-012 — Daemon observations end-to-end

| Field       | Value                                                   |
|-------------|---------------------------------------------------------|
| **Status**  | APPROVED                                                |
| **Step**    | S-012                                                   |
| **Author**  | Agent                                                   |
| **Date**    | 2026-04-24                                              |
| **IS**      | IS-daemon-observations.md (APPROVED)                    |
| **HLPS**    | HLPS-daemon-observations.md (APPROVED)                  |
| **Folder**  | docs/daemons-modernisation/                             |

---

## 1. Context

Implements F-2 per HLPS-daemon-observations. Each UI-triggered probe writes an observation row per daemon per server — captures success + failure. The daemons list page surfaces the most recent observation as a "Last seen" column.

### Scope

**DB**:
- `src/Dorc.Database/deploy/Tables/DaemonObservation.sql` — new table.
- `src/Dorc.Database/Dorc.Database.sqlproj` — new `<Build Include>`.

**`Dorc.PersistentData`**:
- `Model/DaemonObservation.cs` — new entity.
- `EntityTypeConfigurations/DaemonObservationEntityTypeConfiguration.cs` — fluent config.
- `Contexts/DeploymentContext.cs` + `IDeploymentContext.cs` — new DbSet + OnModelCreating wiring.
- `Sources/Interfaces/IDaemonObservationPersistentSource.cs` — new.
- `Sources/DaemonObservationPersistentSource.cs` — new.
- `Sources/DaemonsPersistentSource.cs` — `GetDaemons()` and `GetDaemonById()` compute `LastSeenDate` / `LastSeenStatus` via a correlated subquery or a left-join (EF shape decided during execution).
- `PersistentDataRegistry.cs` — register the new source.

**`Dorc.ApiModel`**:
- `DaemonApiModel.cs` — add optional `DateTime? LastSeenDate` and `string LastSeenStatus` (Dorc.ApiModel targets netstandard2.0 with NRT disabled; use `DateTime?` and non-nullable `string` per the existing pattern; callers check for null/empty).
- `DaemonObservationApiModel.cs` — new DTO for the read endpoint (Id, ServerId, ServerName, DaemonId, ObservedAt, ObservedStatus, ErrorMessage).
- `GetDaemonObservationListResponseDto.cs` — paged response.

**`Dorc.Api`**:
- `Controllers/DaemonObservationController.cs` — new read-only controller with `GET /DaemonObservation?daemonId=…&serverId=…&page=…&limit=…` (auth-only, mirror `DaemonAuditController`).

**`Dorc.Core`**:
- `ServiceStatus.cs` — `BuildDaemonList` also carries `ServerId` + `DaemonId` on the `DaemonStatus` items; `ProbeDaemonStatuses` writes an observation row per probed daemon after the status result is recorded (or alongside the error-capture). All observation writes wrapped in try/catch; failures logged via `_logger.LogWarning` and do not alter the probe return.

**`Dorc.Core.DaemonStatus` (internal model)**:
- Add `int? ServerId` and `int? DaemonId` (nullable — not every code path populates them; the wire `DaemonStatusApiModel` stays unchanged).

**`dorc-web`**:
- `swagger.json` — new `/DaemonObservation` path + `DaemonObservationApiModel` + `GetDaemonObservationListResponseDto` schemas + `DaemonApiModel` gains `LastSeenDate` + `LastSeenStatus` nullable properties.
- `apis/DaemonObservationApi.ts` — new (mirrors `DaemonAuditApi.ts` shape).
- `models/DaemonObservationApiModel.ts` — new.
- `models/GetDaemonObservationListResponseDto.ts` — new.
- `models/DaemonApiModel.ts` — regenerated to include new fields.
- `apis/index.ts` + `models/index.ts` + `.openapi-generator/FILES` — wire-up.
- `pages/page-daemons-list.ts` — new "Last Seen" sortable grid column with a relative-time renderer (exact timestamp in tooltip; "Never" for null).

**Tests**:
- `Tests.Acceptance/DatabaseTests/DaemonSchemaMigrationTests.cs` — new `OBS_ObservationTableCreatedAndWritable` test: publish, insert one row via raw SQL, read it back. Guards against table / index drift.

### Out of scope
- Observation retention / purge job.
- Bulk cleanup UI.
- Per-daemon observation history dialog (U-3 deferred).
- Any materialised `LastSeenDate` column on `deploy.Daemon` (U-2 deferred).
- Scheduled/background probe (only UI-triggered probes write observations).

### Governing constraints
- **HLPS C-01**: observation writes must not touch `deploy.ServerDaemon` mapping rows. Only insert into `deploy.DaemonObservation`.
- **HLPS C-02**: parallel-safe insert (no read-modify-write).
- **HLPS C-03**: probe path continues on observation-write failure (try/catch + log warning).
- **HLPS C-05**: wire additions only (new nullable fields on existing model + new model + new endpoint).
- **`ProbeDaemonStatuses` Parallel.ForEach** — observation writes happen from inside the `Parallel.ForEach` lambda. Must not share a DbContext across threads; each observation write opens its own context (pattern already used by `DaemonAuditPersistentSource`).

---

## 2. Production Code Change

### 2.1 `DaemonObservation.sql`

```sql
CREATE TABLE [deploy].[DaemonObservation] (
    [Id]              BIGINT         IDENTITY(1,1) NOT NULL,
    [ServerId]        INT            NOT NULL,
    [DaemonId]        INT            NOT NULL,
    [ObservedAt]      DATETIME       NOT NULL,
    [ObservedStatus]  NVARCHAR(50)   NULL,
    [ErrorMessage]    NVARCHAR(MAX)  NULL,
    CONSTRAINT [PK_DaemonObservation] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_DaemonObservation_Server] FOREIGN KEY ([ServerId]) REFERENCES [dbo].[SERVER] ([Server_ID]) ON DELETE NO ACTION,
    CONSTRAINT [FK_DaemonObservation_Daemon] FOREIGN KEY ([DaemonId]) REFERENCES [deploy].[Daemon] ([Id]) ON DELETE NO ACTION
);
GO

CREATE NONCLUSTERED INDEX [IX_DaemonObservation_DaemonId_ObservedAt]
    ON [deploy].[DaemonObservation] ([DaemonId] ASC, [ObservedAt] DESC);
GO

CREATE NONCLUSTERED INDEX [IX_DaemonObservation_ServerId_ObservedAt]
    ON [deploy].[DaemonObservation] ([ServerId] ASC, [ObservedAt] DESC);
```

### 2.2 `DaemonObservation.cs` + config

Entity has `long Id`, `int ServerId`, `int DaemonId`, `DateTime ObservedAt`, `string? ObservedStatus`, `string? ErrorMessage`. No navigation properties — simplest shape; we won't need to walk from an observation back to a Daemon or Server entity (queries use joins or ID lookups).

Config uses fluent `.ToTable("DaemonObservation", "deploy")`. No `[Table]` attribute.

### 2.3 `DeploymentContext` + interface

Add `DbSet<DaemonObservation> DaemonObservations { get; set; }` on both; wire config in `OnModelCreating`.

### 2.4 `IDaemonObservationPersistentSource` + impl

```csharp
public interface IDaemonObservationPersistentSource
{
    void InsertObservation(int serverId, int daemonId, DateTime observedAt, string? observedStatus, string? errorMessage);
    GetDaemonObservationListResponseDto GetObservations(int daemonId, int? serverId, int limit, int page);
    IDictionary<int, (DateTime ObservedAt, string? Status)> GetLastSeenByDaemon(IEnumerable<int> daemonIds);
}
```

- `InsertObservation`: open context, `context.DaemonObservations.Add(...)`, `SaveChanges()`. No dedup, no idempotency — each probe produces a new row.
- `GetObservations`: paged query on `(DaemonId, ServerId optional)`, ordered by `ObservedAt DESC`.
- `GetLastSeenByDaemon(IEnumerable<int>)`: bulk "latest observation per daemon" via grouped query:
  ```
  context.DaemonObservations
      .Where(o => daemonIds.Contains(o.DaemonId))
      .GroupBy(o => o.DaemonId)
      .Select(g => new { DaemonId = g.Key, Latest = g.OrderByDescending(o => o.ObservedAt).First() })
      .ToDictionary(...)
  ```
  Used by `DaemonsPersistentSource.GetDaemons()` to populate `LastSeenDate` + `LastSeenStatus` on each returned daemon.

### 2.5 `DaemonsPersistentSource.GetDaemons()`

Integrate the last-seen lookup:
```csharp
public IEnumerable<DaemonApiModel> GetDaemons()
{
    using var context = _contextFactory.GetContext();
    var daemons = context.Daemons.ToList();
    var lastSeen = _daemonObservationPersistentSource.GetLastSeenByDaemon(
        daemons.Select(d => d.Id));

    return daemons.Select(daemon =>
    {
        var api = Map(daemon);
        if (lastSeen.TryGetValue(daemon.Id, out var observation))
        {
            api.LastSeenDate = observation.ObservedAt;
            api.LastSeenStatus = observation.Status;
        }
        return api;
    }).ToList();
}
```

Constructor gains `IDaemonObservationPersistentSource` alongside the existing `IDeploymentContextFactory`. Same treatment for `GetDaemonById` — include last-seen in its return.

### 2.6 `ServiceStatus` changes

- Inject `IDaemonObservationPersistentSource` into `ServiceStatus` constructor.
- In `BuildDaemonList`, populate `ServerId` and `DaemonId` on each `DaemonStatus` item (pulled from the `ServerApiModel` and `DaemonApiModel` already in scope).
- In `ProbeDaemonStatuses`, after each `resultsDict.TryAdd(...)` call site (there are four — success, service-controller-error, ping-failure-with-error, ping-status-not-success), also write an observation:
  ```
  try
  {
      if (daemon.ServerId.HasValue && daemon.DaemonId.HasValue)
      {
          _daemonObservationPersistentSource.InsertObservation(
              daemon.ServerId.Value,
              daemon.DaemonId.Value,
              DateTime.Now,
              observedStatus,
              errorMessage);
      }
  }
  catch (Exception obsEx)
  {
      _logger.LogWarning(obsEx, "Failed to record daemon observation for {Server}/{Daemon}",
          daemon.ServerName, daemon.DaemonName);
  }
  ```

### 2.7 `DaemonObservationController`

Parallel to `DaemonAuditController`. GET with query params `daemonId`, optional `serverId`, `page`, `limit`. Returns `GetDaemonObservationListResponseDto`. `[Authorize]` only.

### 2.8 UI — Last Seen column

In `page-daemons-list.ts`, add a fifth sortable grid column after "Type":
```html
<vaadin-grid-sort-column
  path="LastSeenDate"
  header="Last Seen"
  resizable
  .renderer="${this._lastSeenRenderer}"
></vaadin-grid-sort-column>
```

Renderer: if `LastSeenDate` is null or undefined → "Never" in muted colour. Otherwise → relative time (5 min ago / 3 days ago) with `title="<exact timestamp>"` tooltip, and `${LastSeenStatus}` colour coding (green for Running, muted for Stopped, red for error or null-with-ErrorMessage). Use a small helper function `_formatRelativeTime(date: Date): string` — inline in the component; this is a 10-liner (minutes/hours/days branches).

### 2.9 TS regen

Same pattern as S-007 — hand-edit swagger.json, hand-write the TS models/APIs to match the generator output, update `.openapi-generator/FILES` and barrel exports.

### 2.10 DB integration test

`DaemonSchemaMigrationTests.cs`:
```csharp
[TestMethod]
[TestCategory("Database")]
public void OBS_ObservationTableCreatedAndWritable()
{
    PublishDacpac();

    Assert.AreNotEqual(0, ExecuteScalarInt("SELECT ISNULL(OBJECT_ID('deploy.DaemonObservation', 'U'), 0)"));

    // Seed dependencies: one server + one daemon
    ExecuteEphemeral(@"
        INSERT INTO [dbo].[SERVER] (Server_Name) VALUES (N'obs-test-srv');
        DECLARE @sid INT = SCOPE_IDENTITY();
        SET IDENTITY_INSERT [deploy].[Daemon] ON;
        INSERT INTO [deploy].[Daemon] (Id, Name) VALUES (500, N'obs-test-daemon');
        SET IDENTITY_INSERT [deploy].[Daemon] OFF;
        INSERT INTO [deploy].[DaemonObservation] (ServerId, DaemonId, ObservedAt, ObservedStatus)
        VALUES (@sid, 500, SYSDATETIME(), N'Running');
    ");

    var count = ExecuteScalarInt(
        "SELECT COUNT(*) FROM [deploy].[DaemonObservation] WHERE DaemonId = 500 AND ObservedStatus = 'Running'");
    Assert.AreEqual(1, count);
}
```

---

## 3. Branch

Continues on `feat/649-daemons-modernisation`.

---

## 4. Test Approach

- `dotnet build` of `Dorc.PersistentData`, `Dorc.Core`, `Dorc.Api`, `Tests.Acceptance`: zero errors each.
- `npx tsc --noEmit`: zero errors.
- `[TestCategory("Database")]` tests pass against an available SQL instance; `Assert.Inconclusive` otherwise.
- Manual QA: Load Daemons in UI, verify observations accumulate (query DB). Verify Last Seen column shows relative time; sort works.

---

## 5. Commit Strategy

Two commits natural:
1. Backend (schema, entity, source, probe wiring, controller, ApiModel, TS regen, DB test).
2. UI (Last Seen column + renderer).

Or one if preferred. No more than two.

---

## 6. Acceptance Criteria

| ID | Criterion |
|----|-----------|
| AC-1 | `src/Dorc.Database/deploy/Tables/DaemonObservation.sql` exists with shape per §2.1. PK + two indexes + two FKs all `ON DELETE NO ACTION`. Added to sqlproj. |
| AC-2 | `DaemonObservation.cs` + `DaemonObservationEntityTypeConfiguration.cs` + `DbSet<DaemonObservation>` on `(I)DeploymentContext` + `OnModelCreating` wiring. Fluent `.ToTable`, no `[Table]` attribute. |
| AC-3 | `IDaemonObservationPersistentSource` + impl with `InsertObservation`, `GetObservations`, `GetLastSeenByDaemon`. Registered in `PersistentDataRegistry`. |
| AC-4 | `DaemonApiModel` gains nullable `DateTime? LastSeenDate` and `string LastSeenStatus` fields. `DaemonsPersistentSource.GetDaemons()` and `GetDaemonById()` populate them via `GetLastSeenByDaemon`. |
| AC-5 | `DaemonObservationApiModel` + `GetDaemonObservationListResponseDto` exist in `Dorc.ApiModel`. |
| AC-6 | `DaemonObservationController` exists with `GET /DaemonObservation?daemonId=…&serverId=…&page=…&limit=…`, authenticated-only. |
| AC-7 | `Dorc.Core.DaemonStatus` gains `int? ServerId` and `int? DaemonId`. `BuildDaemonList` populates them from the source models. |
| AC-8 | `ServiceStatus.ProbeDaemonStatuses` writes an observation row at every probe result point (success, service-controller-error, ping-failure, ping-not-success). Writes wrapped in try/catch with `LogWarning` on failure; the probe return value is unaffected by observation-write errors. |
| AC-9 | `page-daemons-list.ts` has a "Last Seen" sortable column. Renderer shows relative time with tooltip for populated dates; "Never" in muted style for null. Status colour applied. |
| AC-10 | swagger.json has the new `/DaemonObservation` endpoint + three schemas (`DaemonObservationApiModel`, `GetDaemonObservationListResponseDto`, `LastSeenDate`/`LastSeenStatus` on DaemonApiModel). `.openapi-generator/FILES` updated. TS models + API regenerated (hand-match to generator output). |
| AC-11 | `npx tsc --noEmit`: exit 0. `dotnet build` for all touched projects: zero errors. |
| AC-12 | `DaemonSchemaMigrationTests.OBS_ObservationTableCreatedAndWritable` passes (when SQL available; `Assert.Inconclusive` otherwise). |
| AC-13 | No changes to `deploy.ServerDaemon` mappings on the probe path (HLPS C-01). No changes to the daemon-audit tables or logic. |
