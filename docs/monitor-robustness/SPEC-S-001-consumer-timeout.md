---
name: SPEC-S-001 — Disable per-queue consumer acknowledgement timeout
description: JIT Specification for S-001: add x-consumer-timeout=0 to RabbitMQ lock queue declaration to prevent broker-forced channel closure on long-running deployments
type: spec
status: APPROVED
---

# SPEC-S-001 — Disable per-queue consumer acknowledgement timeout

| Field       | Value                                                    |
|-------------|----------------------------------------------------------|
| **Status**  | APPROVED — Pending user approval                         |
| **Step**    | S-001                                                    |
| **Author**  | Agent                                                    |
| **Date**    | 2026-03-23                                               |
| **IS**      | IS-monitor-robustness.md (APPROVED)                      |
| **HLPS**    | HLPS-monitor-robustness.md (APPROVED)                    |
| **Folder**  | docs/monitor-robustness/                                 |

---

## 1. Context

### Failure mode being addressed
**FM-2 / SC-02**: The RabbitMQ distributed lock holds a queue message unacknowledged for the full duration of a deployment. The broker enforces a consumer acknowledgement timeout (default: 1,800,000 ms = 30 minutes). When a deployment exceeds 30 minutes, the broker forcibly closes the channel with a `PRECONDITION_FAILED (406)` shutdown, triggering lock loss and deployment cancellation. This is deterministic for any long-running deployment.

### Scope
The **production code change** is confined to the lock queue declaration in `RabbitMqDistributedLockService.TryAcquireLockAsync`. No changes are required to `RabbitMqDistributedLock`, `DeploymentEngine`, `MonitorService`, or any startup/shutdown paths.

Test verification necessarily exercises the full lock path including the queue declaration and the broker's response to the declared arguments. This is expected: SC-04 requires integration-level tests, which by definition exercise multiple components end-to-end.

### Governing constraints
- **C-05**: The `HighAvailabilityEnabled = false` path must continue to function — this change is within the HA-enabled code path only.
- **SC-02**: Fix must not require broker-level configuration changes. The `x-consumer-timeout` per-queue argument is a documented RabbitMQ client-side override (supported natively from RabbitMQ 3.12.0, which is the deployed version, as confirmed by U-6 resolution). U-6 also confirmed zero user and operator policies on the broker, so the argument will not be silently overridden.
- **SC-04**: Recovery behaviour must be covered by an automated integration-level test; unit tests with mocked channels are not sufficient for FM-2.

---

## 2. Production Code Change

### Target
The queue arguments dictionary in the `TryAcquireLockAsync` method of `RabbitMqDistributedLockService`, at the point where the lock queue is declared.

### Change
Add a third entry to the existing queue arguments dictionary: the `x-consumer-timeout` argument set to a `long` value of `0`. A value of `0` is the RabbitMQ-documented mechanism for disabling the per-queue consumer acknowledgement timeout. The existing `x-queue-type` and `x-single-active-consumer` arguments must remain unchanged.

The argument value must be typed as `long` (not `int`). RabbitMQ's AMQP argument type for time-based values is 64-bit integer; passing a 32-bit `int` with value `0` may be silently mis-typed by the client library, producing unexpected behaviour.

No other changes to `TryAcquireLockAsync`, the connection/channel lifecycle, or the lock acquisition flow are required.

### Queue lifecycle note
Lock queues are ephemeral by design: each deployment declares a fresh queue and `DisposeAsync()` explicitly deletes it. The `x-consumer-timeout = 0` argument therefore takes effect naturally on the next lock acquisition after this change is deployed. In the edge case where a prior deployment left an orphaned queue without this argument (unclean crash before deletion), the existing orphaned-queue cleanup logic handles deletion before re-declaration; no additional handling is needed.

### Declaration conflict note
If a queue already exists with different arguments, RabbitMQ returns a `PRECONDITION_FAILED` error on re-declaration. Because the existing arguments include only `x-queue-type` and `x-single-active-consumer`, adding `x-consumer-timeout` would cause a declaration conflict with any live queue from before this change.

Two cases arise:

1. **Orphaned queue (no active consumer)**: The orphaned-queue cleanup path (`TryDeleteQueueAsync`) deletes stale queues before re-declaration. This handles the crash-left-behind scenario.

2. **Live queue held by an older Monitor instance (rolling upgrade)**: The new Monitor version cannot delete a queue with an active consumer without breaking the old Monitor's lock, which would violate C-01. The correct behaviour in this case is that the new Monitor receives `PRECONDITION_FAILED` on re-declaration and fails to acquire the lock — returning `null` for that environment — until the old Monitor releases the lock naturally. This is already the correct outcome: C-01 requires that two monitors cannot hold the same environment lock concurrently. The new Monitor will retry lock acquisition on its next polling cycle, at which point the old queue will have been deleted by the old Monitor's `DisposeAsync()` and re-declaration will succeed.

---

## 3. Branch

`feature/S-001-disable-consumer-ack-timeout`

---

## 4. Test Approach

### Rationale
S-001's fix is **preventive**: by declaring the queue with `x-consumer-timeout = 0`, the broker will never close the channel due to consumer acknowledgement timeout, so the PRECONDITION_FAILED failure (FM-2) cannot occur. The primary test must therefore verify the **effect** of the queue argument — that a lock queue acquired through `TryAcquireLockAsync` carries `x-consumer-timeout = 0` and that this argument prevents the broker from closing the channel under the timeout condition.

This is distinct from a re-acquisition survival test (which tests what happens *after* a channel closes — that is S-002's scope). A simulated client-side channel abort does not validate S-001 because it would bypass the queue argument entirely.

Per SC-04, the FM-2 recovery path requires integration-level testing against a broker (not a mocked channel). The existing `RabbitMqLockIntegrationTests` class is the correct location.

### Test 1 — Integration: lock queue carries x-consumer-timeout=0 argument (primary)

**What it verifies**: After acquiring a lock via `TryAcquireLockAsync`, the queue declared for that resource carries the `x-consumer-timeout` argument with value `0`, confirming the broker accepted the argument.

**Approach**: Acquire a lock for a test resource key. Query the declared queue's arguments using the RabbitMQ management HTTP API or passive queue declaration. Assert that `x-consumer-timeout` is present and equals `0`. Release the lock.

**Pass condition**: `x-consumer-timeout` is present in queue arguments and equals `0`.

### Test 2 — Integration: x-consumer-timeout=0 prevents broker channel closure (effect validation)

**What it verifies**: The `x-consumer-timeout = 0` argument causes the broker to not close the channel for an unacknowledged consumer, even when the global consumer timeout would otherwise apply.

**Approach**: Using the RabbitMQ .NET client directly (not through `TryAcquireLockAsync`):

1. Declare a control queue with a short, non-zero `x-consumer-timeout` (e.g., 1,000 ms). Start a consumer on it with `autoAck: false`. Hold the message unacknowledged for longer than the timeout. Assert: the channel is closed by the broker (PRECONDITION_FAILED or connection loss) within a reasonable wait period.
2. Declare a treatment queue with `x-consumer-timeout = 0`. Start a consumer with `autoAck: false`. Hold the message unacknowledged for the same duration as step 1. Assert: the channel remains open and the consumer is still active.

**Pass condition**: Control queue channel closes; treatment queue channel remains open.

**Note**: This test directly demonstrates the broker behaviour that S-001 relies on and does not require the global broker consumer timeout to be configured at any specific value — the short per-queue timeout in step 1 is sufficient to trigger the failure within the test.

### Test 3 — Unit: queue declaration includes x-consumer-timeout argument (supplementary)

**What it verifies**: The arguments dictionary passed to `QueueDeclareAsync` contains the `x-consumer-timeout` key with a `long` value of `0`. This is a compile-time safety net ensuring the argument is not accidentally removed by a future refactor.

**Approach**: Using the existing mocked channel infrastructure in the unit test suite, capture the `arguments` parameter passed to `QueueDeclareAsync` and assert the key-value pair is present with the correct type (`long` / `Int64`) and value (`0`). The type assertion is important: the spec requires `long`, not `int`, and both have the same `0` value at runtime, so the type must be verified explicitly.

**Pass condition**: `args["x-consumer-timeout"]` equals `0L` and is of type `long` (Int64).

### Execution environment
Tests 1 and 2 require a live RabbitMQ instance, consistent with the existing `RabbitMqLockIntegrationTests` class (which is `[Ignore]` by default and requires explicit activation). These tests are expected to be run against the integration environment during development and before release; they are not expected to be in the default CI gate.

Test 3 (unit) runs in the standard CI pipeline with no external dependencies.

### Existing tests
All existing tests in `RabbitMqLockIntegrationTests` and `DistributedLockServiceTests` must continue to pass without modification.

---

## 5. Commit Strategy

Commits are at the Delivery phase's discretion, following the test-first approach required by the governing process. The production change and tests are delivered on the feature branch. The minimum is one commit for tests and one for the production change; ordering and grouping is determined by the implementer.

---

## 6. Acceptance Criteria

| ID   | Criterion |
|------|-----------|
| AC-1 | The lock queue is declared with `x-consumer-timeout = 0L` in the arguments dictionary, alongside the existing `x-queue-type` and `x-single-active-consumer` arguments. |
| AC-2 | The integration test (Test 1) passes: a lock acquired via `TryAcquireLockAsync` has `x-consumer-timeout = 0` in the queue's declared arguments as verified against the broker. |
| AC-3 | The integration test (Test 2) passes: a queue declared with `x-consumer-timeout = 0` is not closed by the broker when holding an unacknowledged message beyond the timeout threshold of a control queue declared with a short non-zero timeout. |
| AC-4 | The unit test (Test 3) passes: `args["x-consumer-timeout"]` in the `QueueDeclareAsync` call equals `0` and is of type `long` (Int64). |
| AC-5 | All pre-existing tests in `RabbitMqLockIntegrationTests` and `DistributedLockServiceTests` continue to pass. |
| AC-6 | The `HighAvailabilityEnabled = false` path is unaffected (this change is within the HA-enabled code path only). |
| AC-7 | No new public interfaces, configuration keys, or database schema changes are introduced. |
