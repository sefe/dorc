# HLPS: Monitor Service Robustness — Cancellation Cascade Elimination

| Field       | Value                          |
|-------------|--------------------------------|
| **Status**  | APPROVED — Pending user approval |
| **Author**  | Agent                          |
| **Date**    | 2026-03-23                     |
| **Folder**  | docs/monitor-robustness/       |

---

## 1. Problem Statement

The DOrc Monitor service is producing avoidable deployment cancellations across both the PR and QA non-production environments. Three distinct failure modes were observed on 2026-03-19, all resulting in in-flight deployment requests being terminated and marked **Cancelled** rather than completing or recovering automatically.

On 2026-03-19 alone, at least 7 deployment requests were cancelled across two environments due to these failure modes (6 in PR from service restart cascades, 1 in QA from RabbitMQ consumer timeout). The consequence is wasted deployment time, manual operator re-submission, and reduced confidence in scheduled deployment pipelines.

---

## 2. Observed Failure Modes

### FM-1 — Service Restart Cascade (PR environment: 17:52 and 18:39)
When the Monitor service is restarted (whether by a planned rolling update, health-check bounce, or Windows service manager action), the `monitorCancellationToken` is signalled. Because every per-request `CancellationTokenSource` is linked to this token, **all in-flight deployments are immediately aborted**. The `DeploymentEngine` graceful shutdown then logs "All in-progress deployments completed successfully" — a misleading message, since completion was via cancellation, not success.

On 2026-03-19 this occurred twice in the PR environment (17:52 and 18:39), cancelling 6 deployment requests across two restart events.

**Signatures in logs:**
```
[INFOR Microsoft.Hosting.Lifetime:] - Application is shutting down...
[INFOR Dorc.Monitor.ScriptDispatcher:] - Cancellation requested — terminating Runner process...
[INFOR Microsoft.Hosting.Lifetime:] - Application started.     ← within same second
```

### FM-2 — RabbitMQ Consumer Acknowledgement Timeout / PRECONDITION_FAILED (QA: 19:18)
The distributed lock mechanism holds a RabbitMQ message **unacknowledged** for the duration of the deployment. RabbitMQ enforces a broker-level consumer timeout (configured at 1,800,000 ms = 30 minutes) and forcibly closes the channel when the limit is exceeded. The resulting `PRECONDITION_FAILED` (AMQP code 406) causes:

1. The lock channel to close
2. The lock re-acquisition attempt to fail (the new channel open throws `TaskCanceledException`)
3. `_lockLostCts` to be cancelled
4. The in-flight deployment to abort and the request to be marked Cancelled

Long-running deployments (database restores, large MSI installs) routinely exceed 30 minutes on these environments, making this failure **deterministic for long deployments**.

**Signatures in logs:**
```
[WARNI RabbitMqDistributedLockService:] - Channel for lock 'env:TEVO DV 11' shut down:
  PRECONDITION_FAILED - delivery acknowledgement on channel 1 timed out. Timeout value: 1800000 ms
[WARNI RabbitMqDistributedLockService:] - Failed to re-acquire lock for 'env:TEVO DV 11'. Triggering cancellation.
```

### FM-3 — RabbitMQ Broker INTERNAL_ERROR Causing Lock Channel Loss (PR: 18:24)
The RabbitMQ broker emitted `INTERNAL_ERROR (code 541)` for the `env:TEVO DV 11` channel. The existing re-acquisition logic attempted to recover but the re-acquisition message delivery timed out ("Lock re-acquisition timed out waiting for message delivery — lock may have been taken by another monitor"). The deployment for request 1849043 was cancelled.

A secondary cascade affected `env:Endur DV 10` and `env:Endur DV 13` approximately 2 minutes later — the same broker disturbance propagated to connections holding those environment locks. `Endur DV 10` re-acquired successfully; `Endur DV 13` did not. This secondary cascade is treated as the same failure mode (broker disturbance propagating across multiple connections) rather than a distinct fourth failure mode.

**Signatures in logs:**
```
[WARNI RabbitMqDistributedLockService:] - Channel for lock 'env:TEVO DV 11' shut down: INTERNAL_ERROR
[WARNI RabbitMqDistributedLockService:] - Lock re-acquisition timed out waiting for message delivery
[WARNI RabbitMqDistributedLockService:] - Failed to re-acquire lock for 'env:TEVO DV 11'. Triggering cancellation.
```

---

## 3. Scope

**In scope:**
- DOrc Monitor service (`src/Dorc.Monitor/`)
- `RabbitMqDistributedLockService` and `RabbitMqDistributedLock`
- `DeploymentEngine`, `MonitorService`, `DeploymentRequestStateProcessor`, `PendingRequestProcessor`
- Deployment request state machine (new state transitions if required)

**Out of scope:**
- The component script failure (QA 11:01, request 1816175, `010 - Stop Endur & Spread` exit code 0xFFFFFFFF) — this is a script-level bug, not a monitor robustness issue
- User-initiated explicit cancellations (QA 16:40) — by design
- Clean service restarts with zero in-flight deployments (QA 13:12, 16:20) — no impact
- RabbitMQ broker infrastructure changes (consumer timeout policy, etc.) — solution must be achievable at the client code level without broker config changes
- Changes to the DOrc API or runner processes

---

## 4. Goals and Success Criteria

| ID    | Success Criterion |
|-------|------------------|
| SC-01 | A Monitor service restart (planned or unplanned) does not cause in-flight deployment requests to be immediately marked Cancelled. Requests in flight at restart time complete normally within the graceful shutdown window where the service manager timeout permits, or are placed in a recoverable state from which the new instance can automatically resume them (conditional on U-1 and U-5 resolution — see Unknowns Register). |
| SC-02 | Deployments running longer than 30 minutes do not fail due to RabbitMQ consumer acknowledgement timeout. The fix must not require changes to broker-level RabbitMQ configuration, and must remain effective even if a per-queue argument override is subject to operator policy precedence (see U-6). |
| SC-03 | Transient RabbitMQ broker errors (INTERNAL_ERROR, connection drops) and re-acquisition message-delivery timeouts result in automatic retry with sufficient patience, cancelling the deployment only if lock ownership genuinely cannot be confirmed after exhausting a meaningful configurable retry window calibrated against observed broker recovery times. |
| SC-04 | All changes are covered by automated tests demonstrating the recovery behaviour at the level where the failure occurs. FM-2 and FM-3 recovery paths require integration-level tests using embedded broker behaviour or equivalent faithful simulation; unit tests alone are not sufficient for these failure modes. |
| SC-05 | No change to the external API contract, database schema (beyond additive changes), or runner protocol. |

---

## 5. Constraints

- C-01: The solution must not allow two monitor instances to deploy to the same environment concurrently — the distributed lock invariant must be preserved.
- C-02: If a lock is genuinely lost and cannot be re-acquired (another monitor has taken it), the current instance must gracefully yield. It must NOT continue executing a deployment it no longer holds the lock for. During a re-acquisition retry window (SD-4), the deployment continues executing; this is an accepted window of unconfirmed lock ownership, bounded by the retry timeout. The re-acquisition mechanism relies on the single-active-consumer guarantee of the lock queue to ensure at most one monitor receives the lock message.
- C-03: A Monitor process that is killed hard (OS SIGKILL, service manager kill) cannot be fully protected against. The existing `CancelStaleRequests()` on next-startup mechanism remains the recovery path for hard-kill scenarios.
- C-04: The Windows service manager stop timeout is beyond our control. Solutions that rely on extended graceful shutdown periods must account for the real-world service manager timeout in the deployment environment. If deployments are still running when the graceful shutdown window expires, requests remain in **Running** state and are resolved by the startup recovery mechanism on the next instance launch.
- C-05: Changes must be backward-compatible with environments where HA (RabbitMQ locking) is disabled — the `HighAvailabilityEnabled = false` path must continue to function.

---

## 6. Proposed Solution Directions

This section describes the intended approach at a conceptual level. Detailed design is in the Implementation Sequence.

### SD-1: Decouple Service Shutdown from Per-Request Cancellation (addresses FM-1)

Currently the per-request `CancellationTokenSource` is created by linking `monitorCancellationToken` with `envLock.LockLostToken`. When the service shuts down, `monitorCancellationToken` fires and all per-request tokens fire simultaneously.

The fix: introduce a dedicated **processing-loop token** (used only to stop the DeploymentEngine from picking up new requests) separate from the per-request tokens. Per-request tokens are linked only to `envLock.LockLostToken` (lock loss) and an explicit per-request cancel signal. On graceful shutdown, the processing loop stops but in-flight deployments run to natural completion within the service's graceful shutdown window.

If the graceful shutdown window expires before in-flight deployments complete, requests remain in **Running** state. The existing `CancelStaleRequests()` on next-startup mechanism provides recovery. If SD-3 is implemented (conditional on U-1 and U-5), recovery transitions to automatic resume rather than cancel.

### SD-2: Eliminate RabbitMQ Consumer Acknowledgement Timeout (addresses FM-2)

The lock queue is declared with the `x-consumer-timeout` per-queue argument set to `0` (disabled). This overrides the broker-level consumer timeout for that specific queue without requiring broker configuration changes, and is a documented RabbitMQ pattern for intentionally long-lived consumers.

Lock queues are ephemeral by design: each deployment creates a fresh lock queue (`lock.env:{environment}`) and `RabbitMqDistributedLock.DisposeAsync()` explicitly deletes it on completion. The `x-consumer-timeout = 0` argument therefore takes effect naturally on the next lock acquisition. In the edge case where a prior deployment failed to delete its lock queue (e.g., due to an unclean crash), the existing fallback cleanup mechanism (`TryDeleteQueueAsync` / orphaned queue deletion) handles removal before re-declaration.

This fix is conditional on U-6: if the RabbitMQ broker is configured with an operator policy that takes precedence over per-queue arguments and explicitly sets `consumer-timeout`, the per-queue argument will be silently ignored. If U-6 confirms this is the case, an alternative strategy (e.g., periodic lock renewal) will be required.

### SD-3: Automatic Request Resume After Service Restart (addresses FM-1, enhancement)

When the new Monitor instance starts up, `CancelStaleRequests()` currently marks all **Running** requests as Cancelled. Instead, for requests that were in-flight during a clean shutdown (i.e., the service stopped itself rather than crashed), those requests should be transitioned to **Pending** for automatic retry, rather than Cancelled.

Distinction between clean shutdown and crash:
- A clean shutdown writes a "draining" marker before stopping.
- A crash leaves no marker.
- On startup: if marker present → transition Running requests to Pending for resume; if absent → cancel (existing behaviour).

**Edge case — crash after marker write**: If the process is killed after writing the marker but before completing the drain, the new instance would incorrectly treat in-flight requests as eligible for resume. This risk is accepted: the distributed lock's TTL and the next instance's lock acquisition will ensure only one monitor deploys to any environment. In the worst case, a request is retried from the beginning, which requires deployments to be sufficiently idempotent (see U-5).

This improvement is conditional on U-1 and U-5 being resolved.

### SD-4: Longer Re-acquisition Window for Broker Errors (addresses FM-3)

The current re-acquisition wait for message delivery after channel/connection loss is short. During broker disturbance (as observed in FM-3, where the cascade lasted at least 2 minutes), the lock message re-queuing may be delayed. The re-acquisition logic should implement a configurable retry loop, cancelling the deployment only after exhausting the retry window. The default retry window must be calibrated against observed broker recovery times — at minimum 2–3 minutes based on the FM-3 evidence.

During the retry window, the deployment continues executing. This is an accepted window of unconfirmed lock ownership: if another monitor has taken the lock, it cannot start deploying to the same environment until the current deployment's Runner process exits (which will happen when this instance's cancellation eventually fires, or when the deployment completes). The single-active-consumer guarantee of the lock queue ensures the lock message is delivered to exactly one consumer, preventing split-brain.

---

## 7. Unknowns Register

| ID  | Description | Owner | Blocking | Resolution |
|-----|-------------|-------|---------|------------|
| U-1 | What is the Windows service manager stop timeout configured for the Monitor service? | User | **Blocking** | **RESOLVED.** No explicit `HostOptions.ShutdownTimeout` is configured in `Program.cs`; .NET 8 default is **30 seconds**. Windows SCM `ServicesPipeTimeout` is also ~30 seconds (Wix installer sets no override). The existing 30-minute graceful wait in `MonitorService.StopAsync()` never runs — the .NET host timeout fires first. **SD-3 (auto-resume) is therefore required, not optional.** SD-1 alone cannot protect in-flight deployments. |
| U-2 | Are the Monitor service restarts caused by a planned self-deployment, health-check, or another mechanism? | User | Non-blocking unless hard-kill confirmed | **Unresolved** — non-blocking, does not gate any IS step. |
| U-3 | Is there a documented maximum deployment duration for any environment? | User | Non-blocking | **Unresolved** — non-blocking. Defaulting to `x-consumer-timeout = 0` (unlimited). |
| U-4 | Is the `HighAvailabilityEnabled = false` path exercised in any active environment? | User | Non-blocking | **Unresolved** — non-blocking. |
| U-5 | Are deployment executions idempotent (safe to re-run from the beginning)? | User | **Blocking** for SD-3 | **RESOLVED.** Confirmed idempotent by user. SD-3 is safe to implement. |
| U-6 | Does the RabbitMQ broker have an operator policy overriding per-queue `consumer-timeout` arguments? | User | **Blocking** for SD-2 | **RESOLVED.** RabbitMQ Management UI confirms zero user policies and zero operator policies on the broker (RabbitMQ 3.12.0, which natively supports per-queue `x-consumer-timeout`). The `x-consumer-timeout = 0` queue argument will take effect as intended. |

---

## 8. Out-of-Scope Risks

- **Script bugs**: The `010 - Stop Endur & Spread` component failing repeatedly (QA, 11:01) is a script-level issue. No robustness improvement in the monitor will prevent a consistently-failing script from causing a cancellation.
- **Hard-kill race**: If the OS kills the Monitor process mid-deployment (after all our protections), the request will land in Running state. This is unavoidable without external state coordination, and is addressed by the startup recovery mechanism.
- **Dual-monitor concurrency window during re-acquisition**: During the SD-4 retry window, neither monitor has confirmed lock ownership. This is acceptable: the lock queue uses a single-active-consumer pattern, guaranteeing that the lock message is delivered to exactly one consumer. If the current instance cannot re-acquire, the message will be delivered to the next consumer (another monitor), not to both simultaneously. The worst outcome is that the current instance's in-flight deployment continues briefly while another monitor acquires the lock — this is bounded by the retry timeout and resolved when the current instance's cancellation fires.
