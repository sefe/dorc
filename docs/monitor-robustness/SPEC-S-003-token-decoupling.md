# SPEC-S-003: Decouple Service Shutdown from In-Flight Deployments

| Field       | Value                                                  |
|-------------|--------------------------------------------------------|
| **Status**  | APPROVED — Pending user approval                       |
| **Step**    | S-003                                                  |
| **Author**  | Agent                                                  |
| **Date**    | 2026-03-23                                             |
| **IS**      | IS-monitor-robustness.md (APPROVED)                    |
| **HLPS**    | HLPS-monitor-robustness.md (APPROVED)                  |
| **Branch**  | feat/monitor-robustness-s001-s002                      |
| **Must release with** | S-004 (see IS deployment note)               |

---

## 1. Context

Currently, every per-request `CancellationTokenSource` in `DeploymentRequestStateProcessor.ExecuteRequests` is linked to `monitorCancellationToken`. When the Monitor service receives a stop signal, `monitorCancellationToken` fires and all in-flight deployments are immediately aborted via their linked tokens. This is the root cause of FM-1.

The HLPS resolution of U-1 confirms the effective service stop timeout is ~30 seconds (both .NET `HostOptions.ShutdownTimeout` default and Windows SCM `ServicesPipeTimeout`). SD-1 alone cannot protect deployments longer than 30 seconds. S-003 provides the token decoupling that gives deployments the best chance of completing within the window; S-004 provides the recovery path for the residual case.

Key current code locations:
- `DeploymentRequestStateProcessor.ExecuteRequests`: linked token creation (lines ~483–491)
- `DeploymentEngine.ProcessDeploymentRequestsAsync`: inner processing loop and graceful-shutdown wait block (lines ~45–128)
- `Program.cs`: no `HostOptions.ShutdownTimeout` configuration

---

## 2. Requirements

### R1 — Token decoupling

The per-request `CancellationTokenSource` created inside `ExecuteRequests` must no longer be linked to `monitorCancellationToken`. The revised linking rules are:

- **HA enabled** (`envLock != null`): link only to `envLock.LockLostToken`. Lock loss (genuine or via re-acquisition failure) cancels the deployment; service shutdown does not.
- **HA disabled** (`envLock == null`): create an independent `CancellationTokenSource` not linked to any shared token.

The `monitorCancellationToken.ThrowIfCancellationRequested()` check at the beginning of the `Task.Run` body **MUST be retained**. It prevents a task from starting if the service is already stopping at the point it is scheduled. Without it, a task that starts after shutdown is signalled would have a fully decoupled token and could never be stopped by the service lifecycle. The processing loop should not start new work during shutdown.

User-initiated cancellation continues to work via `TerminateRequestExecution`, which calls `Cancel()` on the stored `CancellationTokenSource`. This path is unaffected by the token decoupling.

### R2 — Explicit shutdown timeout

`HostOptions.ShutdownTimeout` must be set in `Program.cs` to 30 seconds, making the host's graceful window explicit and consistent with the Windows SCM stop timeout (per U-1 resolution). This replaces the implicit .NET 8 default.

The `DeploymentEngine`'s internal 30-minute wait in the graceful-shutdown block (the `WaitAsync(TimeSpan.FromMinutes(30))` call) must be reviewed and aligned: since the host forces exit after 30 seconds, the 30-minute wait is effectively dead code. It must be replaced with an unbounded `Task.WhenAll` call (no internal timeout), allowing the host's `ShutdownTimeout` to be the single controlling timeout. The try/catch structure around this call may be simplified accordingly.

### R3 — Graceful shutdown log accuracy

The `DeploymentEngine` graceful-shutdown completion message must accurately distinguish between:
- Deployments that completed naturally within the shutdown window
- Deployments still running when the host forces exit

The current message "All in-progress deployments completed successfully during graceful shutdown" fires regardless of whether the timeout expired or tasks were still running. This must be replaced with a message that correctly states the actual outcome (e.g., how many completed vs. still running at shutdown).

### R4 — HA-disabled path unaffected

When `HighAvailabilityEnabled = false` and `envLock == null`, the decoupling must produce an independent `CancellationTokenSource` that can still be explicitly cancelled via `TerminateRequestExecution`. User-initiated cancellation and the `CancelRequests` polling loop must continue to function identically.

---

## 3. Acceptance Criteria

| ID   | Criterion |
|------|-----------|
| AC-1 | When the Monitor service receives a stop signal, in-flight deployment tasks are NOT immediately cancelled. The tasks continue executing. |
| AC-2 | A deployment that completes within the 30-second shutdown window finishes with its normal completion state (not Cancelled). The lock queue is cleanly released. |
| AC-3 | A deployment still running when the host forces exit leaves its request in `Running` state in the database (not `Cancelled`). S-004 recovers from this state. |
| AC-4 | User-initiated cancellation (via the explicit cancel signal / `CancelRequests` path) still terminates the affected deployment and transitions it to `Cancelled`. |
| AC-5 | The `HighAvailabilityEnabled = false` path passes all existing tests and works correctly end-to-end: user cancellation functions, deployments can be terminated. |
| AC-6 | The graceful shutdown log message correctly reports the number of deployments that completed vs. those still running at host exit. |
| AC-7 | `HostOptions.ShutdownTimeout` is set to 30 seconds in `Program.cs` and is the only controlling timeout for the host's graceful window. |
| AC-8 | All existing unit tests for `DeploymentRequestStateProcessor` and `DeploymentEngine` pass without modification (or with only mechanical updates to align with the decoupled token construction). |

---

## 4. Test Approach

### Unit tests

- A test for `ExecuteRequests` verifies that firing `monitorCancellationToken` after a task starts does NOT cancel the `requestCancellationTokenSource` for that task.
- A test verifies that `TerminateRequestExecution` still cancels the correct `requestCancellationTokenSource` after decoupling.
- A test verifies the HA-disabled path: an independent `CancellationTokenSource` is created and is cancellable via `TerminateRequestExecution`.
- A test verifies the graceful shutdown log emits an accurate completion/still-running count (can mock the task list via `_runningTasks`).

### Integration / end-to-end

Full integration verification (service stop with in-flight deployment) requires the live environment and is out of scope for automated tests. The unit tests above cover the critical branching logic.

---

## 5. Accepted Risks

- **Orphaned child processes on forced host exit**: When the host's `ShutdownTimeout` expires and the process is force-terminated, any deployment tasks still awaiting `Task.WhenAll` are abandoned mid-execution. Managed resources are cleaned up by the runtime, but external child processes spawned by deployments (e.g., PowerShell scripts, runner processes) may be orphaned. This is acceptable: S-004 provides the state recovery path, and runner process cleanup on next startup is the existing mechanism for this scenario. No code change is required to address this risk.

## 6. Out of Scope

- Changes to `CancelStaleRequests` startup logic — this is S-004.
- Changes to the lock re-acquisition path — completed in S-002.
- Changes to the Windows SCM timeout — outside this codebase.
- Processing loop cancellation behaviour — the loop correctly stops on `monitorCancellationToken` cancellation, unchanged.
