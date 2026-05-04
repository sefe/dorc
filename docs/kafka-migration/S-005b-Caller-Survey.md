# S-005b `TryAcquireLockAsync` Caller Survey

| Field | Value |
|---|---|
| **Status** | APPROVED — Pending user approval |
| **Created** | 2026-04-14 |
| **Related** | SPEC-S-005b R-7, R-2 (ADR-deviation shim) |

---

## 1. Purpose

Enumerate every call site of `IDistributedLockService.TryAcquireLockAsync`
and classify each against both:

1. **Legacy Rabbit semantic** — `null` returned immediately on contention
   (another monitor holds the queue's single-active consumer slot).
2. **New Kafka semantic** — `null` returned on **wait-cap timeout** (partition
   not acquired within `leaseTimeMs`) or on **caller cancellation**.

Per SPEC-S-005b §R-7 "Exit branch", remediation is in-scope for this step
if any caller is incompatible.

---

## 2. Call-site enumeration

### 2.1 Production callers

| # | Caller (file:line) | Resource-key shape | Handles null? | Blocks? | Legacy-compatible? | Kafka-compatible? |
|---|---|---|---|---|---|---|
| 1 | `DeploymentRequestStateProcessor.cs:501` | `"env:{environmentName}"` | **Yes** — on null, sets `environmentLockBackoff[env] = now + LockBackoffDuration` and returns from the per-env task (skips this pass). | No — passes `EnvironmentLockLeaseTimeMs` as the budget; expects prompt return. | ✅ Tolerates immediate null on contention. | ✅ Tolerates null on wait-timeout or caller cancellation; backoff path is the same. |

**Only one production caller.** The null-return path is already the
graceful "another monitor is working this env; try again later" path — the
Kafka impl preserves the shape (null → skip + backoff) even though the null
arrives after a bounded wait rather than instantly.

### 2.2 Test-code callers

All test callers substitute a mock or test double for `IDistributedLockService`
and are unaffected by the substrate choice.

- `DistributedLockServiceTests.cs:25, 54, 77, 142, 240, 276, 317, 366, 1044, 1118` — exercise the Rabbit impl directly; remain bound to the Rabbit substrate.
- `DeploymentRequestStateProcessorTests.cs:401, 441, 462, 1064, 1406, 1544` — mock setup verifying lock acquire/dispose; agnostic to substrate.

---

## 3. Behavioural delta for the single production caller

**Rabbit substrate (today):** `TryAcquireLockAsync` returns null quickly
(~message-TTL arrival window) when another monitor is the single-active
consumer. The caller records backoff and moves on.

**Kafka substrate (post-cutover):** `TryAcquireLockAsync` blocks up to
`min(leaseTimeMs, KafkaLocksOptions.LockWaitDefaultTimeoutMs)` waiting for
partition ownership, then returns null on timeout. The caller records the
same backoff and moves on. Outcome is behaviourally equivalent; the only
observable difference is wall-clock latency on the null path.

### Operational note

`EnvironmentLockLeaseTimeMs` today is tuned for Rabbit's TTL semantic
("longer than typical request duration so crash-recovery is the dominant
use"). Post-cutover this value is reinterpreted as a wait-cap. If it is
significantly longer than the per-environment processing cadence, the null
path becomes slower than useful. Operators should review the value before
flipping `Kafka:Substrate:DistributedLock = Kafka` — a cap of ~5–30 seconds
is a reasonable upper bound. This is a configuration concern, not a code
change: the caller's null-handling path does not need to change.

---

## 4. Remediations

**None required.** The single production caller's null-handling semantic
maps cleanly onto the Kafka-impl wait-cap-then-null behaviour. The
operational tuning note above is captured as-is for the S-010 cutover
runbook.

No `TryAcquireLockImmediateAsync` bounded-wait-overload is needed — the
caller does not depend on immediate-null-on-contention for correctness, only
for liveness, and the Kafka impl's bounded wait maintains equivalent
liveness provided `leaseTimeMs` is reasonably scoped.

---

## 5. Reviewer checklist

- [x] Every call site of `IDistributedLockService.TryAcquireLockAsync` enumerated.
- [x] Each caller classified against legacy + new semantics.
- [x] Any incompatible caller either remediated or escalated (N/A — none incompatible).
- [x] Operational-tuning advice captured for the S-010 runbook.
