---
name: SPEC-S-003 — IWindowsWorkerClient contract + null-impl + worker-absence detection
description: JIT Specification for S-003 — defines IWindowsWorkerClient as the seam every later worker-move step extends, ships a typed HttpClient implementation, a null implementation for Linux installs, the WindowsWorker:Enabled config flag, and the typed-exception → 503 ExceptionFilter that turns worker-unavailable into a documented client response.
type: spec
status: APPROVED
---

# SPEC-S-003 — `IWindowsWorkerClient` contract + null-impl + worker-absence detection

| Field       | Value                                              |
|-------------|----------------------------------------------------|
| **Status**  | APPROVED (auto-pilot per user direction)           |
| **Step**    | S-003                                              |
| **Author**  | Agent                                              |
| **Date**    | 2026-05-28                                         |
| **IS**      | [IS-api-split.md](IS-api-split.md) (APPROVED)      |
| **HLPS**    | [HLPS-api-split.md](HLPS-api-split.md) (APPROVED)  |

---

## 1. Context

S-003 establishes the abstraction every later worker-move step (S-004 / S-005 / S-006) extends. It also resolves three non-blocking unknowns from the HLPS:

- **U-6** — worker URL discovery: config-file (`WindowsWorker:Url`).
- **U-7** — contract-test strategy: shared-DTO via `Dorc.ApiModel`.
- **U-11** — worker-absence detection: explicit `WindowsWorker:Enabled` boolean config flag; when `false`, register the null implementation.

The interface starts empty — concrete methods (e.g. `ResetPasswordAsync`, `GetRemoteServerInfoAsync`, `GetServiceStatusAsync`) arrive in their respective S-step PRs. This step just lands the seam.

## 2. Production code change

### 2.1 `Dorc.Api/Interfaces/IWindowsWorkerClient.cs` (new)

Empty marker interface — concrete methods added by later steps:

```csharp
namespace Dorc.Api.Interfaces
{
    // Seam for the Linux-incompatible Windows-worker calls. Concrete methods are
    // added by later S-steps as their endpoints move (S-004 registry, S-005 WMI,
    // S-006 password reset). Two implementations:
    //   - HttpWindowsWorkerClient: real HTTP loopback caller (Windows installs).
    //   - WorkerUnavailableClient: throws WorkerUnavailableException so the global
    //     ExceptionFilter translates it to 503 (Linux installs).
    public interface IWindowsWorkerClient { }
}
```

### 2.2 `Dorc.Api/Services/HttpWindowsWorkerClient.cs` (new)

- Takes an injected `HttpClient` (typed via `AddHttpClient<...>` in DI).
- Constructor reads `WindowsWorker:Url` from config (used as base address).
- A `DelegatingHandler` (registered alongside) injects the `X-Worker-Key` header on outbound calls.

### 2.3 `Dorc.Api/Services/WorkerKeyDelegatingHandler.cs` (new)

`DelegatingHandler` that reads `WindowsWorker:SharedKey` from `IConfiguration` and adds `X-Worker-Key` to outbound requests. Constant string, not derived from caller state.

### 2.4 `Dorc.Api/Services/WorkerUnavailableClient.cs` (new)

Throws `WorkerUnavailableException` from every interface method (none yet; per-method overrides added in later steps via partial class or by extending the interface).

### 2.5 `Dorc.Api/Exceptions/WorkerUnavailableException.cs` (new)

Typed exception carrying the endpoint name. Caught by the ExceptionFilter and translated to:

```
HTTP/1.1 503 Service Unavailable
Content-Type: application/json
{"error":"windows_worker_unavailable","endpoint":"<name>"}
```

### 2.6 `Dorc.Api/Services/WorkerUnavailableExceptionFilter.cs` (new)

`IExceptionFilter` (synchronous; sufficient for this concern). Registered globally in `Program.cs`. Translates `WorkerUnavailableException` → the documented `503` body.

### 2.7 DI wiring in `Program.cs`

```csharp
var workerEnabled = builder.Configuration.GetValue<bool>("WindowsWorker:Enabled");
if (workerEnabled)
{
    builder.Services.AddTransient<WorkerKeyDelegatingHandler>();
    builder.Services
        .AddHttpClient<IWindowsWorkerClient, HttpWindowsWorkerClient>(client =>
        {
            var url = builder.Configuration["WindowsWorker:Url"]
                ?? throw new InvalidOperationException("WindowsWorker:Enabled=true but WindowsWorker:Url is missing");
            client.BaseAddress = new Uri(url);
        })
        .AddHttpMessageHandler<WorkerKeyDelegatingHandler>();
}
else
{
    builder.Services.AddSingleton<IWindowsWorkerClient, WorkerUnavailableClient>();
}
```

And register the filter:

```csharp
builder.Services.AddControllers(opts => opts.Filters.Add<WorkerUnavailableExceptionFilter>());
```

### 2.8 `Dorc.Api/appsettings.json` additions

```json
"WindowsWorker": {
  "Enabled": false,
  "Url": "http://127.0.0.1:5005",
  "SharedKey": ""
}
```

`Enabled` defaults to `false` so the existing Windows-install topology continues to work unchanged at this commit (no worker process to call yet). The flag flips to `true` in S-008 when the installer provisions the worker.

## 3. Test plan

`src/Dorc.Api.Tests/WindowsWorkerClientTests.cs`:

- `WorkerKeyDelegatingHandler_AddsHeader` — exercises the handler against a mock inner handler; asserts the outbound request carries `X-Worker-Key` with the configured value.
- `WorkerUnavailableExceptionFilter_Translates503` — instantiates the filter, exercises against an `ExceptionContext` carrying a `WorkerUnavailableException`, asserts the `503` body shape.
- `WorkerUnavailableClient_HasNoMethodsYet` — placeholder assertion that the type compiles and is constructable; this is the seam, not the surface.

No tests against `HttpWindowsWorkerClient` itself yet — it has no methods. S-004/S-005/S-006 add per-method contract tests as they extend the interface.

## 4. Verification

- `dotnet build src/Dorc.Api/Dorc.Api.csproj` succeeds.
- `dotnet test src/Dorc.Api.Tests/` — three new tests pass; no existing-test regressions.
- On a Linux install (worker disabled): existing endpoints continue to behave identically. The new `WorkerUnavailableClient` is injected but no controller has called it yet.

## 5. Out of scope

- Concrete worker methods (S-004 / S-005 / S-006 add them).
- The actual `Dorc.Api.WindowsWorker` project (S-002 — shipped separately).
- Installer wiring (S-008).
- Documentation (S-010).
