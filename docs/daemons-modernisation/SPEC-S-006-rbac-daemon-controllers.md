---
name: SPEC-S-006 — RBAC on daemon controllers
description: JIT Specification for S-006 — inject IRolePrivilegesChecker into RefDataDaemonsController and ServerDaemonsController; gate POST/PUT + attach/detach on PowerUser|Admin; gate daemon DELETE on Admin only; DaemonStatusController unchanged.
type: spec
status: APPROVED
---

# SPEC-S-006 — RBAC on daemon controllers

| Field       | Value                                                   |
|-------------|---------------------------------------------------------|
| **Status**  | APPROVED                                                |
| **Step**    | S-006                                                   |
| **Author**  | Agent                                                   |
| **Date**    | 2026-04-24                                              |
| **IS**      | IS-daemons-modernisation.md (APPROVED)                  |
| **HLPS**    | HLPS-daemons-modernisation.md (APPROVED)                |
| **Folder**  | docs/daemons-modernisation/                             |

---

## 1. Context

### What this step addresses
**DF-9** — `RefDataDaemonsController` (POST/PUT/DELETE) and `ServerDaemonsController` (attach/detach) have only class-level `[Authorize]`. Any authenticated user can create, edit, delete, attach, or detach daemons. Every other RefData controller (Projects, Scripts, Environments, Permissions, SqlPorts) gates mutations on Admin or PowerUser via `IRolePrivilegesChecker`. Daemons are the outlier.

S-006 brings the two daemon controllers into line: inject `IRolePrivilegesChecker`, gate the mutating actions at the HTTP entry point, return `403 Forbidden` with a readable message when the caller lacks the required role.

### Scope
Two controller files:
- `src/Dorc.Api/Controllers/RefDataDaemonsController.cs` — `Post`, `Put`, `Delete`.
- `src/Dorc.Api/Controllers/ServerDaemonsController.cs` — `Attach` (POST), `Detach` (DELETE).

Out of scope:
- `DaemonStatusController` — retains its existing `CanModifyEnvironment` gate on `PutDaemonState`. Aligning RefData RBAC with environment-scoped RBAC is deferred (HLPS §3 out-of-scope / SD-4).
- `RefDataDaemonsController.Get` and `ServerDaemonsController.Get` — read endpoints remain open to any authenticated user, matching the Projects/Scripts/Environments convention.
- `DaemonAuditController` (new in S-007) — its read gate is decided in S-007 per U-11 resolution (open-read, matching project audit).
- Any changes to the underlying persistence layer. S-006 is a pure controller-gating change.

### Governing constraints
- **HLPS SC-04**: `RefDataDaemonsController` POST/PUT and `ServerDaemonsController` POST/DELETE return 403 for callers without PowerUser **or** Admin. `RefDataDaemonsController` DELETE returns 403 for non-Admins. `DaemonStatusController` unchanged.
- **HLPS SC-07**: the 403 body carries a message the UI can display (not a generic opaque error).
- **Reference pattern**: `src/Dorc.Api/Controllers/RefDataEnvironmentsController.cs:86-101` for POST. Uses nested-if with an inner Admin-required check for prod; our daemons don't have an analogue, so the shape simplifies.
- **Role-check API**: `IRolePrivilegesChecker.IsPowerUser(ClaimsPrincipal)` and `.IsAdmin(ClaimsPrincipal)` in `src/Dorc.PersistentData/RolePrivilegesChecker.cs`.

### Ordering note
S-006 has no hard prerequisite. It could land before S-003 chronologically. It is ordered here because S-007 depends on it (both controllers are edited again for audit wiring, and S-007 picks up the role-checker injection without re-editing the constructor).

---

## 2. Production Code Change

### 2.1 `RefDataDaemonsController`

**Constructor**: inject `IRolePrivilegesChecker` alongside the existing `IDaemonsPersistentSource`. Store in a readonly field.

**Post (Create)** — after the existing `DaemonDuplicateException` try/catch from S-004 is in place:
- First statement in the try block: if `!_rolePrivilegesChecker.IsPowerUser(User) && !_rolePrivilegesChecker.IsAdmin(User)`, return `StatusCode(403, "Daemons can only be created by PowerUsers or Admins!")`.
- Otherwise proceed to `_daemonsPersistentSource.Add(model)`.
- Keep the S-004 `DaemonDuplicateException` and `DbUpdateException` catches unchanged.

**Put (Update)** — currently expression-bodied:
- Convert to block-bodied. First statement: if `!_rolePrivilegesChecker.IsPowerUser(User) && !_rolePrivilegesChecker.IsAdmin(User)`, return `StatusCode(403, "Daemons can only be edited by PowerUsers or Admins!")`.
- Otherwise `return Ok(_daemonsPersistentSource.Update(model));`.
- Swagger `[SwaggerResponse(403)]` annotation added.

**Delete** — currently returns `IResult`:
- First statement: if `!_rolePrivilegesChecker.IsAdmin(User)`, return `Results.Forbid()` with a message body. Since `Results.Forbid()` does not accept a body, switch the return type to `IActionResult` for consistency with Post/Put and use `StatusCode(403, "Daemons can only be deleted by Admins!")`.
- Otherwise the existing `Delete` behaviour (Ok / NotFound) unchanged.
- Swagger `[SwaggerResponse(403)]` annotation added.

### 2.2 `ServerDaemonsController`

**Constructor**: inject `IRolePrivilegesChecker` alongside the existing `IDaemonsPersistentSource`.

**Attach (POST)**:
- First statement: if `!_rolePrivilegesChecker.IsPowerUser(User) && !_rolePrivilegesChecker.IsAdmin(User)`, return `StatusCode(403, "Daemons can only be attached to servers by PowerUsers or Admins!")`.
- Otherwise the existing Attach logic.
- Swagger `[SwaggerResponse(403)]` annotation added.

**Detach (DELETE)**:
- First statement: if `!_rolePrivilegesChecker.IsPowerUser(User) && !_rolePrivilegesChecker.IsAdmin(User)`, return `StatusCode(403, "Daemons can only be detached from servers by PowerUsers or Admins!")`.
- Otherwise the existing Detach logic.
- Swagger `[SwaggerResponse(403)]` annotation added.

**Get (server-scoped read)**: unchanged.

### 2.3 Helper (optional)

If the repeated `!IsPowerUser && !IsAdmin` check becomes visually noisy across the five mutating actions (three on `RefDataDaemonsController`, two on `ServerDaemonsController`), a private helper on each controller — e.g. `private bool HasPowerOrAdmin()` — is acceptable. Keep it a local private method, not a new shared extension; the pattern is controller-local and we don't want to grow shared scaffolding for a 2-line check. Defer this optimisation unless the reviewer flags repetition — if each action has a single `if`-return, the duplication is minor and readable.

### 2.4 Message convention

Messages match the existing convention in `RefDataEnvironmentsController` (short English sentences, no structured payload, no exception stack trace). The UI pastes the response body directly into an error banner.

---

## 3. Branch

Continues on `feat/649-daemons-modernisation`.

---

## 4. Test Approach

### Rationale
Role-gating is a pure HTTP-boundary behaviour change. Authentication/role semantics are best exercised via controller tests where a `ClaimsPrincipal` is constructed with specific roles and the action is invoked. Existing controller tests in `Dorc.Api.Tests` follow this pattern.

### Test 1 — RefDataDaemonsController.Post 403 for authenticated-only (controller test, new)
Caller has no role claims. POST a valid daemon. Assert 403 with body containing "PowerUsers or Admins".

**Pass**: 403 returned; Add is never invoked.

### Test 2 — RefDataDaemonsController.Post 200 for PowerUser (controller test, new)
Caller has the `PowerUser` role claim. POST a valid daemon. Assert 200 and the returned `DaemonApiModel`.

**Pass**: 200 returned; Add is invoked exactly once.

### Test 3 — RefDataDaemonsController.Post 200 for Admin (controller test, new)
Same as Test 2 but with the `Admin` role claim.

**Pass**: 200 returned.

### Test 4 — RefDataDaemonsController.Delete 403 for PowerUser (controller test, new)
Caller has PowerUser (not Admin). DELETE. Assert 403 with body "Admins".

**Pass**: 403 returned; Delete on the persistent source is never invoked.

### Test 5 — RefDataDaemonsController.Delete 200 for Admin (controller test, new)
Caller has Admin. DELETE. Assert the existing Ok / NotFound branches fire.

**Pass**: 200 on existent id, 404 on missing.

### Test 6 — ServerDaemonsController.Attach and Detach 403 / 200 (controller test, new)
Matrix: for each of Attach and Detach, one test with no roles → 403, one test with PowerUser → 200, one test with Admin → 200.

**Pass**: all six matrix cells behave as specified.

### Test 7 — RefDataDaemonsController.Put 403 / 200 (controller test, new)
No roles → 403; PowerUser → 200; Admin → 200.

**Pass**: three cells behave as specified.

### Test 8 — Compile gate
`dotnet build Dorc.Api.csproj` succeeds with zero errors. The `using Dorc.PersistentData;` directive must be added to both controllers (it's where `IRolePrivilegesChecker` lives).

**Pass**: build clean.

### Pre-existing build block
As noted in S-004, `Dorc.Api.Tests` has a pre-existing `System.DirectoryServices.Fakes` issue locally. Tests can be authored regardless; CI is the authoritative gate.

---

## 5. Commit Strategy

Single commit is natural — both controllers change for the same reason and the diff is small (roughly 5–10 lines per controller). Two commits (one per controller) is acceptable if the implementer prefers atomic review.

---

## 6. Acceptance Criteria

| ID   | Criterion |
|------|-----------|
| AC-1 | `RefDataDaemonsController` constructor injects `IRolePrivilegesChecker`. |
| AC-2 | `RefDataDaemonsController.Post` returns 403 with the message `"Daemons can only be created by PowerUsers or Admins!"` (or equivalent) when the caller is neither PowerUser nor Admin. Otherwise the S-004 Add / 409 / 500 flow is unchanged. |
| AC-3 | `RefDataDaemonsController.Put` returns 403 with a similar message when the caller is neither PowerUser nor Admin. Otherwise Update proceeds. |
| AC-4 | `RefDataDaemonsController.Delete` returns 403 with `"Daemons can only be deleted by Admins!"` when the caller is not Admin (PowerUser is **not** sufficient). Otherwise the existing 200/404 flow is unchanged. Return type switched to `IActionResult` for consistency. |
| AC-5 | `ServerDaemonsController` constructor injects `IRolePrivilegesChecker`. |
| AC-6 | `ServerDaemonsController.Attach` returns 403 when the caller is neither PowerUser nor Admin. Otherwise the existing Attach flow is unchanged. |
| AC-7 | `ServerDaemonsController.Detach` returns 403 when the caller is neither PowerUser nor Admin. Otherwise the existing Detach flow is unchanged. |
| AC-8 | `DaemonStatusController` is unchanged. |
| AC-9 | All 403 responses carry a string body the UI can display (not an empty body or a generic framework error). |
| AC-10 | Swagger `[SwaggerResponse(StatusCodes.Status403Forbidden, Type = typeof(string))]` annotation is present on every newly-403-emitting action for API-doc visibility. |
| AC-11 | `dotnet build Dorc.Api.csproj` succeeds with zero errors. |
| AC-12 | Controller tests (new, in `Dorc.Api.Tests/Controllers/`) cover the role matrix per §4 Tests 1–7. Locally may be blocked by the pre-existing `System.DirectoryServices.Fakes` issue; CI is authoritative. |
| AC-13 | No changes outside `src/Dorc.Api/Controllers/RefDataDaemonsController.cs` and `src/Dorc.Api/Controllers/ServerDaemonsController.cs` (plus the two corresponding test files under `Dorc.Api.Tests`). |
