# SPEC-S-004: Auto-Resume Deployments Interrupted by Service Restart

| Field       | Value                                                  |
|-------------|--------------------------------------------------------|
| **Status**  | APPROVED — Pending user approval                       |
| **Step**    | S-004                                                  |
| **Author**  | Agent                                                  |
| **Date**    | 2026-03-23                                             |
| **IS**      | IS-monitor-robustness.md (APPROVED)                    |
| **HLPS**    | HLPS-monitor-robustness.md (APPROVED)                  |
| **Branch**  | feat/monitor-robustness-s001-s002                      |
| **Must release with** | S-003 (see IS deployment note)               |
| **Depends on** | S-003 (token decoupling makes Running state meaningful on shutdown) |

---

## 1. Context

`CancelStaleRequests()` currently transitions all `Running` and `Requesting` requests to `Cancelled` on startup,
regardless of how the previous instance stopped. After S-003 is deployed, graceful shutdowns leave requests in
`Running` state. Without S-004, these would be cancelled by `CancelStaleRequests()` on next startup — the same
bad outcome as before.

The original design intent in the IS called for a clean-shutdown marker to distinguish graceful stop from crash.
However, HLPS U-5 was resolved as: **deployments are idempotent** (re-running from `Pending` is safe regardless
of how the previous run ended). This eliminates the technical justification for the crash-vs-clean distinction.
Because resuming from `Pending` is safe in all cases — whether the previous instance stopped gracefully, was
killed hard, or crashed — no persistent marker is needed. The startup recovery logic is simply: always resume
`Running` requests as `Pending`.

---

## 2. Requirements

### R1 — `Running` → `Pending` on startup

The `CancelStaleRequests` startup method must be updated so that requests found in `Running` state are
transitioned to `Pending` (resume) rather than `Cancelled`. This applies unconditionally — no distinction
between crash and clean-shutdown recovery paths is made or needed.

The transition must use optimistic concurrency (matching on `Running` status) so that in a concurrent
two-instance startup scenario, only one instance transitions each request. The second instance's transition
attempt for a request already moved to `Pending` is a no-op.

A `RequestStatusChanged` event must be published for each `Running` → `Pending` transition, consistent with
the event publishing done for cancelled requests.

No runner process cleanup (`TerminateRunnerProcesses`) is required for resumed requests. The previous Monitor
instance must have exited before a new instance can start on the same machine, meaning all runner processes
started by the previous instance are already gone.

No deployment result cleanup is required for the `Running` → `Pending` transition. Deployment results created
during the interrupted run are left in place. When the request is re-processed from `Pending`,
`PendingRequestProcessor.Execute` fetches the existing results and passes them to `DeployComponent`, which
re-executes all components and overwrites result status. This is safe and correct under the U-5 idempotency
guarantee. It differs from the `RestartRequests` path (which calls `ClearAllDeploymentResults` for an
explicit user-initiated restart) because here the re-execution is automatic recovery, not a user-driven
clean-slate operation. The `SwitchDeploymentResultsStatuses` call that cancels `Pending` results in the
current `Cancelled` path is therefore omitted from the resume path.

### R2 — `Requesting` → `Cancelled` unchanged

Requests in `Requesting` state are still transitioned to `Cancelled` on startup, unchanged from the current
behaviour. The `Requesting` state represents a request that was mid-pickup by `ExecuteRequest` — the status was
set to `Requesting` but the runner had not yet begun component execution. Deployment results are not yet created
at this state, so no deployment-results cleanup is needed. Runner processes should be terminated as per existing
behaviour.

### R3 — Production requests resume correctly

The production guard in `SwitchRequestsStatus` (which prevents cancellation of requests on production
environments) does not apply to the `Running` → `Pending` resume transition. Resuming a production request is
safer than cancelling it and is explicitly correct. The resume transition calls the persistence layer directly
(as the current `CancelStaleRequests` does for its transitions), bypassing the production guard that exists
only in `SwitchRequestsStatus`.

### R4 — HA-disabled path unaffected

The `Running` → `Pending` resume applies regardless of whether `HighAvailabilityEnabled` is true or false.
The resume logic is entirely within `CancelStaleRequests` and is independent of the lock service.

---

## 3. Acceptance Criteria

| ID   | Criterion |
|------|-----------|
| AC-1 | On startup, requests found in `Running` state are transitioned to `Pending`, not `Cancelled`. |
| AC-2 | The resumed `Pending` request is picked up and deployed normally on the next processing cycle. |
| AC-3 | A request that completed during the shutdown window (no longer in `Running` state at startup) is not re-queued. |
| AC-4 | After a crash (monitor killed, no clean shutdown), requests in `Running` state are still transitioned to `Pending` — the resume path is unconditional. |
| AC-5 | `Requesting`-state requests are transitioned to `Cancelled` on startup, unchanged. |
| AC-6 | In a concurrent two-instance startup scenario, each `Running` request is resumed exactly once. No duplicate or missed transitions. |
| AC-7 | A `RequestStatusChanged` event is published for each `Running` → `Pending` transition. |
| AC-8 | The `HighAvailabilityEnabled = false` path behaves identically — `Running` requests resume as `Pending`. |

---

## 4. Test Approach

### Unit tests

- Verify that `CancelStaleRequests` transitions `Running` → `Pending` (not `Cancelled`).
- Verify that `CancelStaleRequests` still transitions `Requesting` → `Cancelled` (unchanged).
- Verify that a `RequestStatusChanged` event is published for each `Running` → `Pending` transition.
- Verify that `TerminateRunnerProcesses` is NOT called for resumed (`Running` → `Pending`) requests.
- Verify that the `Running` → `Pending` transition uses optimistic concurrency (match on `Running` status);
  a second concurrent call for a request already moved to `Pending` transitions zero rows.

---

## 5. Out of Scope

- No database migration required — this change requires no schema additions.
- No persistent clean-shutdown marker — the IS originally called for this; it is replaced by unconditional
  resume, justified by U-5 (deployments are idempotent).
- Changes to `AbandonRequests` (24-hour stale abandonment) — unchanged.
- Changes to `CancelRequests` (user-initiated cancellation) — unchanged.
- Multi-instance HA lock contention during resume — handled by existing distributed lock acquisition logic.

---

## 6. Review History

### R1 — 2026-03-23

**Panel:** Claude Opus 4.6, Claude Sonnet 4.6, GPT-5.2-codex

| Finding | Severity | Disposition | Resolution |
|---------|----------|-------------|------------|
| F-1: Deployment results cleanup for resumed requests not addressed | HIGH | Accept | Added explicit statement to R1: no cleanup required; `PendingRequestProcessor.Execute` fetches and reuses existing results; `DeployComponent` overwrites status on re-execution; contrast with `RestartRequests` explained |
| F-2: Log message update not specified | LOW | Defer to Delivery | Implementer will update log messages naturally |
| F-3: IS divergence — marker description still in IS | MEDIUM | Defer to Impact Assessment (3.H) | IS update is correctly sequenced after S-004 passes quality gate |
| F-4: `Requesting` → `Cancelled` justification | LOW | No action | Justification adequate |
| F-5: AC-6 testability at unit level | LOW | No action | Persistence-layer optimistic concurrency is sufficient |
| F-6: Duplicate events in concurrent scenario | MEDIUM → LOW | Downgrade | Pre-existing behaviour outside diff scope |
| F-7: U-5 justification sufficiency | LOW | No action | Design rationale sound; 24-hour abandon covers crash-loop case |

### R2 — 2026-03-23

**Panel:** Claude Opus 4.6, Claude Sonnet 4.6, GPT-5.2-codex — **UNANIMOUS APPROVAL**

F-1 fix verified adequate by all three reviewers. No regressions or new blocking findings.
