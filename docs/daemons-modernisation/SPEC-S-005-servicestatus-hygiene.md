---
name: SPEC-S-005 — ServiceStatus hygiene (remove side effect + surface probe errors)
description: JIT Specification for S-005 — delete DiscoverAndMapDaemonsForServer + PersistDiscoveredMappings (DF-7); add ErrorMessage to ServiceStatusApiModel and ServicesAndStatus, populate from probe catch blocks (DF-8); regenerate TypeScript client.
type: spec
status: APPROVED
---

# SPEC-S-005 — ServiceStatus hygiene

| Field       | Value                                                   |
|-------------|---------------------------------------------------------|
| **Status**  | APPROVED                                                |
| **Step**    | S-005                                                   |
| **Author**  | Agent                                                   |
| **Date**    | 2026-04-24                                              |
| **IS**      | IS-daemons-modernisation.md (APPROVED)                  |
| **HLPS**    | HLPS-daemons-modernisation.md (APPROVED)                |
| **Folder**  | docs/daemons-modernisation/                             |

---

## 1. Context

### What this step addresses
Two user-visible defects in the daemon-status read path:

- **DF-7** — the GET `/DaemonStatus` call writes to `deploy.ServerDaemon` as a side effect of probing: `ServiceStatus.PersistDiscoveredMappings` calls `IDaemonsPersistentSource.DiscoverAndMapDaemonsForServer` whenever a probe confirms a service is running on a server. A read-shaped endpoint mutating persistent state is the bug. Under S-006 this would also bypass the new RBAC gate on attach/detach. The fix is to **delete the discovery/persist path entirely**. If auto-discovery is desired in future it becomes a distinct, explicitly-gated endpoint (not in scope for this PR).

- **DF-8** — probe errors (Ping timeout, ServiceController exception) are logged at `LogInformation` or `LogDebug` and never surface to the API caller. The UI sees "no status" with no diagnostic. Failed probes today **drop the item entirely** — there's no row in the result set for the affected service. Fix: add a nullable `ErrorMessage` to the response shape; in each probe-failure catch, return a `ServicesAndStatus` with the message populated (rather than returning nothing); plumb the field through the mapper and regenerate the TypeScript client.

### Scope
Five files:
- `src/Dorc.Core/ServiceStatus.cs` — delete `PersistDiscoveredMappings` and its call site in `GetServicesAndStatusForEnvironment`; populate `ErrorMessage` in the three probe catch blocks that matter; populate `ErrorMessage` in `GetServiceStatus`'s blind catch.
- `src/Dorc.Core/ServiceAndStatus.cs` (the `ServicesAndStatus` model) — add nullable `ErrorMessage`.
- `src/Dorc.ApiModel/ServiceStatusApiModel.cs` — add nullable `ErrorMessage`.
- `src/Dorc.Api/Services/APIServices.cs` — two mapper methods (`MapToServiceStatusApiModel`, `MapToServicesAndStatus`) copy the new field.
- `src/Dorc.PersistentData/Sources/DaemonsPersistentSource.cs` — delete the `DiscoverAndMapDaemonsForServer` method body.
- `src/Dorc.PersistentData/Sources/Interfaces/IDaemonsPersistentSource.cs` — delete the `DiscoverAndMapDaemonsForServer` signature.
- `src/dorc-web/src/apis/dorc-api/models/ServiceStatusApiModel.ts` — auto-regenerated; `ErrorMessage?: string | null` appears.

Out of scope:
- The `ChangeServiceState` PUT path (start/stop/restart) — current behaviour unchanged.
- Runner-side Windows service probe mechanism itself (the `ServiceController` calls) — per HLPS scope.
- The `BuildServicesEnvironment` top-level catch (line ~168) which handles a catastrophic "couldn't enumerate servers" failure — too coarse-grained to attach to a single row; keeps log-only behaviour.

**Amendment (2026-04-24, pre-execution)**: `ApiServices` cleanup folded into this SPEC at user request. `ApiServices` is a grab-bag per CLAUDE.md Naming (it mixes projects/environments/daemons/releases). This SPEC extracts only the daemon-specific bits; the rest of `ApiServices` stays untouched as a follow-up PR target. Additional scope:
- **New file** `src/Dorc.Api/Services/ServiceStatusMapping.cs` — static helper class with two static methods (`ToApi(ServicesAndStatus)` and `ToCore(ServiceStatusApiModel)`) carrying the two mapping helpers currently private inside `ApiServices`.
- **Rename** file `src/Dorc.Api/Services/APIServices.cs` → `ApiServices.cs` (fixes the existing class-vs-file casing mismatch — the class is already `ApiServices`).
- **Rename** file `src/Dorc.Api/Interfaces/IAPIServices.cs` → `IApiServices.cs` (same reason).
- **`IApiServices.cs`** — delete the three daemon-related signatures: `GetEnvDaemonsStatuses(int)`, `GetEnvDaemonsStatuses(string, ClaimsPrincipal)`, `ChangeServiceState(ServiceStatusApiModel, ClaimsPrincipal)`.
- **`ApiServices.cs`** — delete the three method bodies and the two private `MapToServiceStatusApiModel` / `MapToServicesAndStatus` helpers. Remove the `_serviceStatus` field and its constructor injection; remove the now-unused `using Dorc.Core;` and `using Dorc.Core.Interfaces;` imports if no other code in the file needs them.
- **`DaemonStatusController.cs`** — switch constructor injection from `IApiServices` to `IServiceStatus`. Call `_serviceStatus.GetServicesAndStatus(...).Select(ServiceStatusMapping.ToApi).ToList()` in the two GETs. In the PUT, call `_serviceStatus.ChangeServiceState(ServiceStatusMapping.ToCore(value), User)` and map result with `ServiceStatusMapping.ToApi`.
- **DI** — the existing registration pattern is checked during execution (reflection-scan via `ApiRegistry`, or explicit `Program.cs` additions). Whatever pattern is used, `IServiceStatus` must be resolvable by `DaemonStatusController` after the switch. The existing `IApiServices` registration remains for the non-daemon methods.

### Governing constraints
- **HLPS C-02**: additive wire change only. `ErrorMessage` is a new nullable field on `ServiceStatusApiModel`; existing callers continue to work.
- **HLPS SC-03**: probe-error surfacing has a regression test.
- **HLPS SD-3 / Round 1 review R1-H3 / R2-M5**: the interface method deletion is **explicit** (interface, implementation, call site, caller method) — not "remove the side effect and leave the dead nav".
- **Round 2 review R1**: TypeScript regeneration is named with the exact command.

---

## 2. Production Code Change

### 2.1 Interface + implementation deletion (DF-7)

**`IDaemonsPersistentSource.cs`**: delete the line `public void DiscoverAndMapDaemonsForServer(int serverId, IEnumerable<string> confirmedServiceNames);`.

**`DaemonsPersistentSource.cs`**: delete the `DiscoverAndMapDaemonsForServer` method body entirely. The `_contextFactory` usage inside it is removed along with the method; no other method in the class depends on it.

### 2.2 Call-site + helper deletion in `ServiceStatus.cs` (DF-7)

**Delete** the `PersistDiscoveredMappings` method (current lines 232–255) entirely.

**Delete** the call to it in `GetServicesAndStatusForEnvironment` (current line 99: `PersistDiscoveredMappings(probeResults, servers);`). No replacement; `probeResults` is still returned on line 100 unchanged.

**Retain** the `_daemonsPersistentSource` field on `ServiceStatus` — `BuildServicesEnvironment` still reads daemons via `GetDaemonsForServer` / `GetDaemons`. Only the write side is removed.

### 2.3 `ErrorMessage` field — models (DF-8)

**`Dorc.Core.ServicesAndStatus`** (`ServiceAndStatus.cs`): add `public string? ErrorMessage { get; set; }` alongside the four existing properties.

**`Dorc.ApiModel.ServiceStatusApiModel`** (`ServiceStatusApiModel.cs`): add `public string? ErrorMessage { get; set; }` alongside the four existing properties.

Both are nullable reference types. Default `null` indicates "no error" — the UI renders status normally. Non-null indicates the probe failed; the UI renders "unreachable: <ErrorMessage>" (actual UI rendering is S-008).

### 2.4 Mapper updates in `APIServices.cs` (DF-8)

**`MapToServiceStatusApiModel`** (current lines 86–95): add `ErrorMessage = ss.ErrorMessage` to the object initializer. Same pattern as the existing four field copies.

**`MapToServicesAndStatus`** (current lines 75–84): add `ErrorMessage = ss.ErrorMessage` for symmetry even though the reverse-mapping is only used for the PUT `ChangeServiceState` path (where `ErrorMessage` is usually null on the inbound request). Keeping both mappers symmetric avoids silent information loss if a caller ever round-trips.

### 2.5 Populate `ErrorMessage` in probe catch blocks (DF-8)

**`ServiceStatus.ProbeServiceStatuses`** (current lines 176–230):

- **Inner catch on `ServiceController` failure** (current lines 207–213): instead of silently logging and dropping the item, construct a `ServicesAndStatus` with `ServerName`, `ServiceName`, `EnvName` from `sa`, `ServiceStatus = null`, and `ErrorMessage = "Service query failed: <ex.Message>"`. `resultsDict.TryAdd((int)index, ...)` so the item still appears in the result set. Keep the existing `LogDebug` call for ops.
- **Outer catch on Ping failure** (current lines 215–219): same pattern. `ErrorMessage = "Server unreachable: <ex.Message>"` and `ServiceStatus = null`.

**`ServiceStatus.GetServiceStatus`** (current lines 330–387):
- **Blind catch at line 382**: construct a `ServicesAndStatus` with the parameters passed in (`envName`, `server`, `service`), `ServiceStatus = null`, and `ErrorMessage = "Service query failed: <ex.Message>"`. Today this returns `new ServicesAndStatus()` with all fields null — lossy.

Error-message string format: prefer a short actionable prefix (`"Server unreachable: "`, `"Service query failed: "`) followed by `ex.Message` (not `ex.ToString()` — no stack trace leakage). The UI pastes the message directly into a tooltip or inline indicator; keep it short and user-friendly.

**Not changed**:
- `BuildServicesEnvironment` catches (lines 152–158 + 161–165 + 168–171). These are DB-enumeration failures, not probe failures; they happen before probing begins and do not naturally attach to a specific `ServicesAndStatus` row. Log-only behaviour preserved.
- `ProbeServiceStatuses`'s top-level catch (lines 222–225) — catastrophic parallel-for failure. Log-only.

### 2.6 TypeScript regeneration

After the C# changes compile, run from `src/dorc-web/`:
```
npm run dorc-api-gen
```
This runs `openapi-generator-cli generate -g typescript-rxjs -i ./src/apis/dorc-api/swagger.json -o ./src/apis/dorc-api/`. Before running, `swagger.json` must be up-to-date with the new C# model — either run the API locally to regenerate swagger.json, or hand-edit `swagger.json`'s `definitions.ServiceStatusApiModel` to add `ErrorMessage: { type: "string", nullable: true }` ahead of the codegen.

**Verify** `src/dorc-web/src/apis/dorc-api/models/ServiceStatusApiModel.ts` has `ErrorMessage?: string | null;` (or equivalent) after regeneration. The UI consumes this in S-008.

---

## 3. Branch

Continues on `feat/649-daemons-modernisation`.

---

## 4. Test Approach

### Rationale
DF-7 (side-effect removal) is verified by deletion plus the compile gate — if the interface, implementation, and call site are all gone, the side effect cannot occur. There's no positive assertion to write beyond "the old behaviour is deleted". DF-8 (error surfacing) has an observable behaviour change suitable for a unit test.

### Test 1 — DF-7 deletion verification (static, one-shot)
Grep-audit for remaining references to `DiscoverAndMapDaemonsForServer` and `PersistDiscoveredMappings` across `src/`. Expected: zero hits.

**Pass**: zero hits.

### Test 2 — DF-8 regression: Ping failure surfaces ErrorMessage (unit, new)
Author a unit test that simulates a Ping failure against `ProbeServiceStatuses`. The current implementation instantiates `new Ping()` directly inside the lambda, which makes mocking awkward — the SPEC author may need to extract a small seam (pass a `Func<string, PingReply?>` or similar into `ProbeServiceStatuses`) or rely on a test server that is genuinely unreachable.

Rather than refactor for testability in this SPEC, accept that this test targets the integration layer via an unreachable dummy server name: construct a `ServicesAndStatus` with `ServerName = "definitely-not-a-real-host.invalid"`, call the internals under a test harness that bypasses `RunImpersonated`, assert the returned result contains an item with `ErrorMessage != null` and `ServiceStatus == null`.

If the existing testing surface makes even this impractical, document as a manual / integration-only verification in the PR description: run the API locally, issue a GET for an environment with at least one unreachable server, inspect the response body for the new `ErrorMessage` field.

**Pass**: a probe failure produces an item with `ErrorMessage` populated (not a silent drop).

### Test 3 — DF-8 happy path unchanged (unit, new or update)
Existing tests that exercise `ProbeServiceStatuses` with reachable servers must continue to return successful items with `ErrorMessage = null`. If no such test exists today, adding one is cheap: construct a `ServicesAndStatus` pointing at a known-reachable server (or mock the Ping), assert the result has `ServiceStatus` populated and `ErrorMessage == null`.

**Pass**: reachable probes are unaffected; `ErrorMessage` is null on success.

### Test 4 — Build and compilation
`dotnet build` of `Dorc.Core`, `Dorc.Api`, `Dorc.PersistentData` must succeed. The interface deletion may surface any missed callers — the compile gate catches them.

**Pass**: all three projects build with zero errors.

### Test 5 — TypeScript regeneration verification
After `npm run dorc-api-gen`, inspect `src/dorc-web/src/apis/dorc-api/models/ServiceStatusApiModel.ts`. Expected: `ErrorMessage?: string | null` field present.

**Pass**: field appears in the generated TS.

### Existing tests
`DaemonStatusController` tests (if any) must still pass — the wire shape is additive. Any test that asserts the exact field list on `ServiceStatusApiModel` must be updated to include the new nullable field.

---

## 5. Commit Strategy

Two commits is natural:
1. **Interface + implementation deletion** (DF-7): delete `DiscoverAndMapDaemonsForServer` from interface + source, delete `PersistDiscoveredMappings` from `ServiceStatus`, delete the call site. Compile-verify.
2. **Error surfacing** (DF-8): add `ErrorMessage` to both models, update mappers, populate in catch blocks, regenerate TS. Compile-verify.

One commit is also acceptable if the implementer prefers.

---

## 6. Acceptance Criteria

| ID   | Criterion |
|------|-----------|
| AC-1 | `IDaemonsPersistentSource.DiscoverAndMapDaemonsForServer` signature is removed. `DaemonsPersistentSource.DiscoverAndMapDaemonsForServer` implementation is removed. |
| AC-2 | `ServiceStatus.PersistDiscoveredMappings` method is removed. The call site in `ServiceStatus.GetServicesAndStatusForEnvironment` is removed. The `_daemonsPersistentSource` field is retained (still used by `BuildServicesEnvironment`). |
| AC-3 | Grep-audit returns zero hits for `DiscoverAndMapDaemonsForServer` and `PersistDiscoveredMappings` across `src/`. |
| AC-4 | `Dorc.Core.ServicesAndStatus` has a nullable `string? ErrorMessage { get; set; }` property. |
| AC-5 | `Dorc.ApiModel.ServiceStatusApiModel` has a nullable `string? ErrorMessage { get; set; }` property. |
| AC-6 | `APIServices.MapToServiceStatusApiModel` and `MapToServicesAndStatus` both copy `ErrorMessage` alongside the existing four fields. |
| AC-7 | `ServiceStatus.ProbeServiceStatuses` — the two inner catches (`ServiceController` failure and Ping failure) each construct a `ServicesAndStatus` with `ErrorMessage` populated and add it to `resultsDict` (not silently drop the item). Log-level behaviour at `LogDebug` is unchanged. |
| AC-8 | `ServiceStatus.GetServiceStatus` — the blind catch constructs a `ServicesAndStatus` with `ErrorMessage` populated and the envName/server/service parameters copied in, instead of returning an all-null `new ServicesAndStatus()`. |
| AC-9 | `src/dorc-web/src/apis/dorc-api/models/ServiceStatusApiModel.ts` has `ErrorMessage?: string | null` (or equivalent) after `npm run dorc-api-gen`. `swagger.json` is updated. |
| AC-10 | `dotnet build` succeeds for `Dorc.Core`, `Dorc.Api`, `Dorc.PersistentData` with zero errors. |
| AC-11 | Probe-failure regression test (or manual verification documented in PR description) demonstrates that an unreachable-server probe yields an item with `ErrorMessage` populated and `ServiceStatus == null`. |
| AC-12 | All pre-existing tests that touch `ServiceStatus` or `DaemonStatusController` continue to pass. If any test asserted the exact field list on `ServiceStatusApiModel`, it is updated to accept the additional `ErrorMessage` field. |
| AC-13 | No changes outside `src/Dorc.Core/`, `src/Dorc.ApiModel/`, `src/Dorc.Api/Services/`, `src/Dorc.PersistentData/`, and `src/dorc-web/src/apis/dorc-api/`. |
