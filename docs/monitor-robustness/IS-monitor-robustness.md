# IS: Monitor Service Robustness — Implementation Sequence

| Field       | Value                                     |
|-------------|-------------------------------------------|
| **Status**  | APPROVED — Pending user approval          |
| **Author**  | Agent                                     |
| **Date**    | 2026-03-23                                |
| **HLPS**    | HLPS-monitor-robustness.md (APPROVED)     |
| **Folder**  | docs/monitor-robustness/                  |

### Amendment — 2026-03-23 (post S-003/S-004 delivery)
S-003 and S-004 have been implemented and passed the Adversarial Quality Gate (R2 unanimous approval). The S-004 section above has been updated to reflect the actual implementation: the clean-shutdown marker mechanism described in the original IS was eliminated during JIT Spec authoring, replaced by unconditional `Running` → `Pending` resume justified by U-5 (deployments are idempotent). No database schema changes were required. The S-003 section is unchanged — it accurately describes what was implemented. S-001 and S-002 remain pending.

---

## Step Index

| ID    | Title                                                   | Addresses       | Depends On        |
|-------|---------------------------------------------------------|-----------------|-------------------|
| S-001 | Disable per-queue consumer acknowledgement timeout      | SD-2, SC-02, SC-04 | —              |
| S-002 | Harden lock re-acquisition with a retry window          | SD-4, SC-03, SC-04 | —              |
| S-003 | Decouple service shutdown from in-flight deployments    | SD-1, SC-01     | —                 |
| S-004 | Auto-resume deployments interrupted by service restart  | SD-3, SC-01     | S-003 (**must be released together with S-003**) |

---

## S-001 — Disable per-queue consumer acknowledgement timeout

### What changes
The lock queue declaration in `RabbitMqDistributedLockService.TryAcquireLockAsync` is extended to include the `x-consumer-timeout` queue argument set to a value that disables RabbitMQ's broker-level consumer acknowledgement timeout for this queue.

### Why it changes
**Addresses FM-2 / SC-02 / SC-04.** The distributed lock consumer holds a RabbitMQ message unacknowledged for the full duration of the deployment. The broker's default consumer timeout (1,800,000 ms) forcibly closes the channel after 30 minutes, cancelling the deployment. This is deterministic for any long-running deployment. Disabling the timeout per-queue — without touching broker configuration — is the correct and supported fix for this pattern (RabbitMQ 3.12.0, which is deployed, supports this argument natively).

### Dependencies
None. This change is entirely self-contained within the lock service.

### Verification intent
- An automated integration-level test simulates the broker's PRECONDITION_FAILED channel shutdown event (the failure that FM-2 produces) and confirms the deployment continues without cancellation after the fix. Unit tests with mocked channels are not sufficient for this case per SC-04.
- A newly acquired lock queue carries the `x-consumer-timeout` argument when inspected via the RabbitMQ management API (supplementary check).
- Existing lock acquisition, release, and re-acquisition behaviour is unaffected for short-running deployments.

---

## S-002 — Harden lock re-acquisition with a retry window

### What changes
The lock re-acquisition path in `RabbitMqDistributedLock` is changed from a single attempt with a short message-delivery timeout to a configurable retry loop that retries re-acquisition multiple times with a delay between attempts, before triggering cancellation. A new configuration value controls the total retry window. The default is calibrated to be at least 2–3 minutes based on the observed broker disturbance duration in FM-3; this is a minimum floor derived from a single observed event and should be treated as a starting point pending further production observation post-delivery. During the retry window, the in-flight deployment continues executing (accepted risk — documented in HLPS C-02).

### Why it changes
**Addresses FM-3 / SC-03 / SC-04.** When the broker emits INTERNAL_ERROR, the re-acquisition message delivery may be delayed beyond the current short timeout, causing the lock to be treated as lost even though the broker is recovering and no other monitor has taken the lock. A retry window gives the broker time to requeue the lock message and the re-acquisition to succeed. Failure to re-acquire after the full window still triggers cancellation, preserving the safety constraint.

### Dependencies
None. This change is self-contained within `RabbitMqDistributedLock` and its supporting service method. It does not interact with the token decoupling in S-003.

### Verification intent
- An automated integration-level test using an embedded or real broker (not a mocked channel) simulates a channel shutdown during re-acquisition with a controlled delay before the message is deliverable, and confirms successful re-acquisition and deployment continuation. Per SC-04, mocked/unit-level simulation is not sufficient for this failure mode.
- After exhausting the retry window with no message delivered, the deployment is correctly cancelled.
- The retry window is configurable via `appsettings.json` and defaults to the calibrated minimum.
- Existing re-acquisition behaviour for simple connection drops (fast re-queue) is unaffected.

---

## S-003 — Decouple service shutdown from in-flight deployments

### What changes
Three related changes delivered together as a single release **in conjunction with S-004** (see deployment note below):

1. **Token decoupling**: The per-request `CancellationTokenSource` in `DeploymentRequestStateProcessor` is no longer linked to `monitorCancellationToken`. It is linked only to the lock's `LockLostToken` and the explicit per-request cancel signal. A separate **processing-loop token** — derived from `monitorCancellationToken` — is used exclusively to stop the `DeploymentEngine` from picking up new requests.

2. **Shutdown timeout**: `HostOptions.ShutdownTimeout` is configured in `Program.cs` to a value that makes the .NET host's graceful window explicit and consistent with the Windows SCM `ServicesPipeTimeout` (~30 seconds). The existing extended wait duration in `MonitorService.StopAsync()` will be reviewed and removed or aligned with this value to avoid leaving dead code in place.

3. **Graceful shutdown log accuracy**: The `DeploymentEngine` graceful shutdown message is updated to correctly distinguish between deployments that completed naturally within the window and those that were still running when it expired.

### Why it changes
**Addresses FM-1 / SC-01 (partial).** Currently, service shutdown immediately cancels all in-flight deployments via the shared `monitorCancellationToken`. Decoupling the tokens means a service stop no longer acts as a kill signal for running deployments. Deployments that complete within the graceful window finish normally; those that outlast it remain in `Running` state for S-004 to recover.

### Dependencies
None — S-003 has no technical dependency on S-001 or S-002. However, **S-003 must be released in the same deployment as S-004**. Deploying S-003 alone, without S-004, creates an intermediate regression: service restart leaves requests in `Running` state indefinitely (neither immediately `Cancelled` as before, nor automatically resumed). This is operationally worse than the current behaviour. S-003 is only correct in production when S-004 is also present.

### Verification intent
- When the Monitor service receives a stop signal, in-flight deployments are not immediately cancelled.
- A deployment that completes within the graceful shutdown window is marked `Completed` (not `Cancelled`) and the lock queue is cleanly deleted.
- A deployment still running when the shutdown window expires leaves its request in `Running` state in the database.
- User-initiated cancellation (via the explicit cancel signal) continues to work correctly and is not affected by this change.
- The `HighAvailabilityEnabled = false` path is unaffected.
- The graceful shutdown log correctly reports "X deployments completed, Y still running at shutdown" (or equivalent), not the previous misleading "all completed successfully."
- The existing extended wait duration in `MonitorService.StopAsync()` is confirmed removed or aligned — no dead code remains.

---

## S-004 — Auto-resume deployments interrupted by service restart

### What changes
One change delivered **in the same release as S-003**:

**Startup recovery (unconditional resume)**: The existing `CancelStaleRequests()` startup logic is updated so that requests found in `Running` state are transitioned to `Pending` for automatic retry, rather than `Cancelled`. This applies unconditionally — there is no distinction between crash recovery and graceful-shutdown recovery. `Requesting`-state requests continue to transition to `Cancelled`, unchanged. A `RequestStatusChanged` event is published for each `Running` → `Pending` transition. The transition uses optimistic concurrency (matching on `Running` status) so that in a concurrent two-instance startup scenario, each request is resumed exactly once.

The original IS described a clean-shutdown marker mechanism to distinguish graceful stop from crash. This was eliminated during the JIT Spec phase. The justification: U-5 confirmed deployments are idempotent — re-running from `Pending` is safe regardless of how the previous run ended. Because resume is safe in all cases, no persistent marker is needed and the crash-vs-clean distinction provides no value. The marker design (database-flag vs. file, multi-instance scoping, crash-after-write edge case) was therefore superseded entirely by this simpler unconditional approach.

### Why it changes
**Addresses FM-1 / SC-01 (completion).** S-003 gives deployments the best chance of completing within the shutdown window. S-004 handles the residual case — when the service is killed before the deployment finishes. Because U-1 confirmed the effective stop timeout is ~30 seconds and most deployments run longer than this, S-004 is the primary recovery path for FM-1. Because U-5 confirmed deployments are idempotent, unconditional resume from `Pending` is safe.

### Dependencies
**Depends on S-003. Must be released in the same deployment as S-003.** Without S-003, requests are cancelled immediately on shutdown (via `monitorCancellationToken`) before the graceful window elapses, so nothing would ever be left in `Running` state for S-004 to resume. S-003 is what makes `Running` at startup a meaningful signal.

### Verification intent
- On startup, requests found in `Running` state are transitioned to `Pending`, not `Cancelled`.
- `Requesting`-state requests are still transitioned to `Cancelled` on startup, unchanged.
- A `RequestStatusChanged` event is published for each `Running` → `Pending` transition.
- The resumed request is picked up and deployed normally on the next processing cycle.
- A request that completed during the shutdown window (no longer in `Running` state at startup) is not re-queued.
- After a crash (monitor killed, no clean shutdown), requests in `Running` state are still transitioned to `Pending` — the resume path is unconditional.
- In a concurrent two-instance startup scenario, each `Running` request is resumed exactly once; a second concurrent transition attempt for an already-resumed request transitions zero rows and publishes no event.
- `TerminateRunnerProcesses` is not called for resumed requests — the previous instance has exited, so all runner processes it started are already gone.
