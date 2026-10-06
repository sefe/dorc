---
name: SPEC-S-010 — Servers-for-daemon query + Delete confirmation warning
description: JIT Specification for F-1a — new GetServersForDaemon source method + GET endpoint + UI wiring so the Delete confirmation dialog can show the attached-server count and names.
type: spec
status: APPROVED
---

# SPEC-S-010 — Servers-for-daemon query + Delete confirmation warning

| Field       | Value                                                   |
|-------------|---------------------------------------------------------|
| **Status**  | APPROVED                                                |
| **Step**    | S-010                                                   |
| **Author**  | Agent                                                   |
| **Date**    | 2026-04-24                                              |
| **Parent**  | FOLLOW-UPS.md F-1a                                      |
| **HLPS**    | HLPS-daemons-modernisation.md (APPROVED; this extends SD-6 / U-7 with the deferred attached-server-count piece) |
| **Folder**  | docs/daemons-modernisation/                             |

---

## 1. Context

### What this step addresses
HLPS U-7 / SPEC-S-008 §2.5 specified that the Delete confirmation dialog on `page-daemons-list.ts` should warn when the daemon is attached to ≥1 servers, ideally with the count/names. S-008 dropped this because the useful query (servers-by-daemon) didn't exist; the current dialog warns unconditionally. This SPEC adds the query and wires it into the dialog.

### Scope
- `src/Dorc.PersistentData/Sources/Interfaces/IDaemonsPersistentSource.cs` — add `GetServersForDaemon(int)` returning `IEnumerable<ServerApiModel>`.
- `src/Dorc.PersistentData/Sources/DaemonsPersistentSource.cs` — implement.
- `src/Dorc.Api/Controllers/ServerDaemonsController.cs` — new `[HttpGet("by-daemon/{daemonId:int}")]` action returning `List<ServerApiModel>`. Authenticated-only (read endpoint; matches `Get(int serverId)` convention).
- `src/Dorc.Api/Dorc.Api.csproj` / swagger.json — endpoint added + regenerated TS.
- `src/dorc-web/src/apis/dorc-api/apis/ServerDaemonsApi.ts` — new method.
- `src/dorc-web/src/pages/page-daemons-list.ts` — `requestDelete` probes the new endpoint; dialog shows count + names when non-empty.

Out of scope:
- Any change to the existing `GET /ServerDaemons/{serverId}` endpoint.
- Pagination on the attached-server list (assumed small — typical daemon is attached to <10 servers).
- RBAC on the read (authenticated-only matches the rest of the read surface).

### Governing constraints
- **HLPS C-02**: new endpoint + new TS API method are additive.
- **HLPS SC-07**: confirmation UX should be informative. Matches the original U-7 intent.
- **SPEC-S-008 AC-7**: this SPEC supersedes the unconditional warning text with the conditional version originally specified.

---

## 2. Production Code Change

### 2.1 `IDaemonsPersistentSource.cs`

Add signature:
```csharp
IEnumerable<ServerApiModel> GetServersForDaemon(int daemonId);
```

### 2.2 `DaemonsPersistentSource.cs`

Implementation walks the inverse of `GetDaemonsForServer`:
```
public IEnumerable<ServerApiModel> GetServersForDaemon(int daemonId)
{
    using var context = _contextFactory.GetContext();
    var daemon = context.Daemons.Include(d => d.Server).FirstOrDefault(d => d.Id == daemonId);
    if (daemon == null) return Enumerable.Empty<ServerApiModel>();
    return daemon.Server.Select(s => new ServerApiModel { ServerId = s.Id, Name = s.Name }).ToList();
}
```

`ServerApiModel` is the shape already used by `GetDaemonsForServer`'s return path. Reuses `Daemon.Server` nav collection (skip-navigation through `deploy.ServerDaemon`).

### 2.3 `ServerDaemonsController.cs`

Add action:
```csharp
[HttpGet("by-daemon/{daemonId:int}")]
[SwaggerResponse(StatusCodes.Status200OK, Type = typeof(List<ServerApiModel>))]
public IActionResult GetServersForDaemon(int daemonId)
{
    var servers = _daemonsPersistentSource.GetServersForDaemon(daemonId).ToList();
    return Ok(servers);
}
```

No RBAC gate beyond class-level `[Authorize]` — it's a read of existing mapping data, parity with the existing `Get(int serverId)`.

### 2.4 TS regen

- swagger.json: new `/ServerDaemons/by-daemon/{daemonId}` path + response schema reuse.
- `ServerDaemonsApi.ts`: new `serverDaemonsByDaemonDaemonIdGet({ daemonId })` method (or whatever casing the generator picks — mirror the existing `serverDaemonsServerIdGet` style).
- FILES manifest: unchanged (same ServerDaemonsApi.ts).

### 2.5 `page-daemons-list.ts`

Add state:
```typescript
@state() private pendingDeleteAttachedServers: string[] = [];
```

In `requestDelete(daemon)`: before opening the confirm dialog, call `new ServerDaemonsApi().serverDaemonsByDaemonDaemonIdGet({ daemonId: daemon.Id })`, populate `pendingDeleteAttachedServers` from the response (map to `Name`), then open the dialog. On error, fall through with an empty list — dialog shows the unconditional warning.

Update the confirm-dialog template to render:
```
Delete daemon **<Name>**? This cannot be undone.
${this.pendingDeleteAttachedServers.length > 0
    ? html`<br />Currently attached to ${count} server(s):
          <ul>${names.map(n => html`<li>${n}</li>`)}</ul>
          Deleting will detach from all of them.`
    : html`<br />No server mappings to remove.`}
```

### 2.6 Database test

Add a fourth `[TestMethod]` to `DaemonSchemaMigrationTests.cs`:
`SC10_GetServersForDaemon_ReturnsAttachedServers` — publish, seed a daemon + two ServerDaemon rows via direct INSERT (no API path — the test is about the persistence-level query), call the query via direct SQL (not API; the test is DB-focused, the API is mechanical around it). Asserts both servers are returned for that daemon, and zero are returned for a different daemon.

Actually — DB test for a persistence method is awkward via DacFx+SQL. Cleaner: unit test at the persistent-source level using an EF in-memory or sqlite provider. But existing tests use the real DacFx path. Simplest path: keep the DB integration test to schema/migration concerns and skip a dedicated test for this method. Manual QA (L-3 via the dialog) will cover it.

**Decision**: no new automated test. Manual QA per SPEC-S-008 §4 Test 3 (end-to-end daemon lifecycle) already exercises attach → delete; this SPEC just adds a richer UX path.

---

## 3. Branch

Continues on `feat/649-daemons-modernisation`.

---

## 4. Test Approach

- `dotnet build` of `Dorc.Api` and `Dorc.PersistentData`: zero errors.
- `npx tsc --noEmit` for dorc-web: zero errors.
- Manual QA: as an Admin, attach a daemon to 2 servers, click Delete, verify the confirmation dialog lists the two server names.

---

## 5. Commit Strategy

Single commit — the four files (interface + source + controller + TS + UI) are tightly coupled.

---

## 6. Acceptance Criteria

| ID | Criterion |
|----|-----------|
| AC-1 | `IDaemonsPersistentSource.GetServersForDaemon(int)` exists and returns `IEnumerable<ServerApiModel>`. |
| AC-2 | `DaemonsPersistentSource.GetServersForDaemon` walks the skip-navigation via `Include(d => d.Server)`. |
| AC-3 | `ServerDaemonsController` has a new `GET /ServerDaemons/by-daemon/{daemonId:int}` action, class-level `[Authorize]` only. |
| AC-4 | swagger.json + TS client updated (new method on `ServerDaemonsApi`). |
| AC-5 | `page-daemons-list.ts` `requestDelete` probes the new endpoint, `pendingDeleteAttachedServers` populated, confirmation dialog renders the list when non-empty. |
| AC-6 | `dotnet build` + `npx tsc --noEmit`: zero errors each. |
| AC-7 | No changes outside `Dorc.PersistentData/`, `Dorc.Api/Controllers/ServerDaemonsController.cs`, `dorc-web/src/apis/dorc-api/`, `dorc-web/src/pages/page-daemons-list.ts`. |
