---
name: SPEC-S-002 — Windows worker project scaffold + loopback host + shared-secret auth
description: JIT Specification for S-002 — creates the Dorc.Api.WindowsWorker project with a 127.0.0.1-only Kestrel host, X-Worker-Key authentication scheme, FromPrimary authorization policy, /health endpoint, and unit tests covering the auth scheme. No Windows operations yet.
type: spec
status: APPROVED
---

# SPEC-S-002 — `Dorc.Api.WindowsWorker` project scaffold

| Field       | Value                                              |
|-------------|----------------------------------------------------|
| **Status**  | APPROVED (auto-pilot per user direction)           |
| **Step**    | S-002                                              |
| **Author**  | Agent                                              |
| **Date**    | 2026-05-28                                         |
| **IS**      | [IS-api-split.md](IS-api-split.md) (APPROVED)      |
| **HLPS**    | [HLPS-api-split.md](HLPS-api-split.md) (APPROVED)  |

---

## 1. Context

S-002 is the worker-scaffold step: a new ASP.NET Core project with the host, auth scheme, and health endpoint in place, ready for S-004/S-005/S-006 to add Windows-only controllers. The worker is intentionally endpoint-empty for actual Windows operations.

Per HLPS D-1, the worker is a separate Windows-only process bound to `127.0.0.1` only. Per HLPS D-3, requests are authenticated via a shared-secret header `X-Worker-Key`; missing or wrong header returns `401` with `{"error":"worker_key_invalid"}`. Authorization decisions are made in the primary; the worker trusts that any call reaching it has already been authz'd.

This step resolves HLPS U-4 (project name): the project is named `Dorc.Api.WindowsWorker` for specificity.

## 2. Production code change

### 2.1 Project file `src/Dorc.Api.WindowsWorker/Dorc.Api.WindowsWorker.csproj`

- `Microsoft.NET.Sdk.Web` SDK.
- TargetFramework: `net8.0-windows` (Windows-only, matching HLPS Scope B).
- Nullable enabled.
- Project references: `Dorc.Core`, `Dorc.ApiModel`. (No reference to `Dorc.Api`. The two run as separate processes.)
- Package references: only what's needed for the host + auth scheme (`Swashbuckle.AspNetCore` deferred to later — no public API surface yet).

### 2.2 `Program.cs` — worker host

- `WebApplication.CreateBuilder(args)`.
- Kestrel configured to listen on `127.0.0.1` only, port read from `WindowsWorker:Port` config (default 5005).
- DI: `IConfiguration`, logging, the `WorkerKey` authentication scheme (see 2.3), the `FromPrimary` authorization policy (see 2.4).
- `MapHealthChecks("/health")` returns 200 with body `Healthy` — does **not** require the `FromPrimary` policy (the primary may poll without the key to detect process liveness; the loopback bind is the security control).
- Controllers (none in this step) gain `[Authorize(Policy = "FromPrimary")]` via convention or per-class attribute. For S-002 we add convention-based `RequireAuthorization(...)` only on controller routes.

### 2.3 Authentication scheme `WorkerKey`

`src/Dorc.Api.WindowsWorker/Authentication/WorkerKeyAuthenticationHandler.cs`:

- `AuthenticationHandler<WorkerKeyAuthenticationOptions>`.
- Reads `Request.Headers["X-Worker-Key"]`. Missing header → `AuthenticateResult.NoResult()` (so the authorization layer can emit a clean 401 with the documented body).
- Compares (constant-time, via `CryptographicOperations.FixedTimeEquals` on UTF-8 bytes) the incoming key against `WindowsWorker:SharedKey` from configuration. Mismatch → `AuthenticateResult.Fail("worker_key_invalid")`.
- On success: emits a `ClaimsPrincipal` with a single claim `"WorkerCaller"="primary"` so endpoints can `[Authorize(Policy = "FromPrimary")]` and reach `User.Identity.IsAuthenticated == true`.
- On failure: a custom `OnChallenge` handler writes `{"error":"worker_key_invalid"}` JSON to the response body alongside the 401.

`src/Dorc.Api.WindowsWorker/Authentication/WorkerKeyAuthenticationOptions.cs`:
- Empty options class extending `AuthenticationSchemeOptions`. Reserved for future expansion (e.g. per-route policy data).

### 2.4 Authorization policy `FromPrimary`

In `Program.cs`:
```
services.AddAuthorization(opt =>
    opt.AddPolicy("FromPrimary", p =>
        p.AddAuthenticationSchemes("WorkerKey").RequireAuthenticatedUser()));
```

### 2.5 `appsettings.json`

```
{
  "WindowsWorker": {
    "Port": 5005,
    "SharedKey": ""   // populated at install time per S-008
  },
  "Logging": { ... default minimal ... }
}
```

`appsettings.Development.json` is **not** added — the worker is not run in Development mode against fake data; tests cover the auth path directly.

### 2.6 Solution wiring

Add the project to `src/Dorc.sln`. Two `Project(...)` / `EndProject` blocks plus the standard `GlobalSection` configuration rows for Debug|Any CPU and Release|Any CPU.

## 3. Test plan

`src/Dorc.Api.WindowsWorker.Tests/Dorc.Api.WindowsWorker.Tests.csproj`:
- MSTest + `Microsoft.AspNetCore.Mvc.Testing` (for `WebApplicationFactory<Program>`).
- Reference `Dorc.Api.WindowsWorker`.

`WorkerKeyAuthenticationTests.cs`:
- `Health_NoHeader_Returns200` — `/health` doesn't require the policy.
- `Health_AnyHeader_Returns200` — same (health always open).
- `Protected_NoHeader_Returns401` — using a test controller registered in a per-test factory, asserts 401 + body `{"error":"worker_key_invalid"}` when no header is sent.
- `Protected_WrongHeader_Returns401_SameBody` — same 401 + body on mismatched key.
- `Protected_CorrectHeader_Returns200` — confirms a request with the right key passes the policy.
- `Loopback_Only_NonLocalAccess` — defer to integration / smoke testing in S-010; documented as out of scope here.

The "test controller" used by the auth tests is added as an internal `[ApiController]` exposed only to the test assembly via `InternalsVisibleTo`. This isolates the auth-scheme test from any real Windows endpoint (which arrive in later S-steps).

## 4. Verification

- `dotnet build src/Dorc.Api.WindowsWorker/Dorc.Api.WindowsWorker.csproj` succeeds (Windows-only — pipeline runs on `windows-latest`).
- `dotnet test src/Dorc.Api.WindowsWorker.Tests/` — all five auth tests pass.
- The new project appears in `src/Dorc.sln` and the Windows CI workflow's solution-level build picks it up.

## 5. Out of scope

- Any Windows-only controllers (S-004 / S-005 / S-006).
- MSI installer wiring (S-008).
- `IWindowsWorkerClient` on the primary side (S-003).
- Health-check that actually probes Windows operations (S-005 may add).
- Documentation of customer-facing config (S-010).
