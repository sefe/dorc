# SPEC-S-002: Harden Lock Re-Acquisition with a Retry Window

| Field              | Value                                                        |
|--------------------|--------------------------------------------------------------|
| **Status**         | APPROVED — Pending user approval                             |
| **Step ID**        | S-002                                                        |
| **Author**         | Agent                                                        |
| **Date**           | 2026-03-23                                                   |
| **Governing Docs** | HLPS-monitor-robustness.md (APPROVED), IS-monitor-robustness.md (APPROVED) |
| **Branch**         | `fix/s-002-lock-reacquisition-retry`                         |

---

## 1. Context

### Problem Being Solved
FM-3 from the HLPS: when the RabbitMQ broker emits `INTERNAL_ERROR`, the existing lock re-acquisition path makes a single attempt to consume the requeued lock message with a short delivery timeout (~5 seconds via `LockAcquisitionTimeoutSeconds`). During the observed broker recovery window (~2–3 minutes), the message may not be immediately deliverable even though the broker is recovering and no other monitor has taken the lock. The single-attempt timeout fires before the broker has finished recovery, `TryReacquireLockChannelAsync` returns null, and the deployment is cancelled unnecessarily.

### Goal
Replace the single-attempt re-acquisition with a configurable retry loop. If the first delivery attempt times out, the code discards the failed channel, waits a short interval, and tries again, looping until either the message is successfully delivered or the total elapsed time exceeds the configured retry window. Failure after the full window still triggers deployment cancellation, preserving the safety constraint (SC-03).

### Accepted Risk from HLPS
During the retry window, the in-flight deployment continues executing. This is accepted per HLPS C-02. Cancellation remains the correct terminal outcome if the window is exhausted.

---

## 2. Scope of Change

### Files Affected
- `src/Dorc.Monitor/IMonitorConfiguration.cs` — add new property
- `src/Dorc.Monitor/MonitorConfiguration.cs` — implement new property with config read and default
- `src/Dorc.Monitor/appsettings.json` — add default value entry under `HighAvailability`
- `src/Dorc.Monitor/HighAvailability/RabbitMqDistributedLockService.cs` — replace single-attempt logic with retry loop

### Out of Scope
- `TryReacquireOrCancelAsync` (the caller in the inner `RabbitMqDistributedLock` class) requires no changes. It already handles null results from `TryReacquireLockChannelAsync` correctly.
- S-003/S-004 token decoupling is independent and not addressed here.
- No changes to queue declaration, lock acquisition, or lock release paths.

---

## 3. Configuration

### New Property: `LockReacquisitionRetryWindowSeconds`

A new integer property is added to `IMonitorConfiguration` and implemented in `MonitorConfiguration` following the existing pattern for HA configuration (see `LockAcquisitionTimeoutSeconds` as the reference implementation):

- **Config path**: `AppSettings:HighAvailability:LockReacquisitionRetryWindowSeconds`
- **Default value**: `150` (2 minutes 30 seconds — a midpoint estimate based on the single observed FM-3 event, which showed a ~2–3 minute broker recovery window. This value is a starting point, not a proven production minimum. Operators in environments where broker recovery takes longer should increase this value.)
- **appsettings.json**: Add `"LockReacquisitionRetryWindowSeconds": 150` to the `HighAvailability` section, alongside the existing `LockAcquisitionTimeoutSeconds`
- **Semantics**: Total elapsed time budget for all re-acquisition attempts combined. When elapsed time exceeds this value at the start of a new attempt, the retry loop terminates and returns failure.
- **Edge case**: If this value is configured at or below `LockAcquisitionTimeoutSeconds`, only one delivery attempt will run (the window is already exhausted after the first timeout). This is a valid operator misconfiguration; no startup validation is required, though the delivery phase may log a warning if detected.

The existing `LockAcquisitionTimeoutSeconds` continues to govern the per-attempt delivery wait (unchanged).

The static `LockReacquisitionTimeout` field (currently 30 seconds) in `RabbitMqDistributedLockService` is removed. Its former role as an outer bound is superseded by `LockReacquisitionRetryWindowSeconds`.

---

## 4. Behavioural Change in `TryReacquireLockChannelAsync`

### Current Behaviour
1. Start a 30-second outer CancellationToken.
2. Ensure connection; create one channel.
3. Register consumer on the lock queue.
4. Wait up to `LockAcquisitionTimeoutSeconds` for message delivery.
5. If delivered: return success. If not: log warning, clean up channel, return null.

### New Behaviour
1. Record the retry window deadline as `now + LockReacquisitionRetryWindowSeconds`.
2. Maintain an attempt counter (for logging).
3. Loop:
   a. **Check deadline first**: if total elapsed time has already exceeded the retry window deadline, exit the loop and return null (window exhausted before starting a new attempt).
   b. **Check disposed flag**: if the lock is disposed, return null immediately (see Thread Safety section).
   c. Ensure connection (handles the case where the connection itself is also recovering).
   d. Create a fresh channel. Per-iteration channel recreation is the correct pattern here because the triggering event for re-acquisition is always channel closure — there is no path where a prior channel can be safely reused across retries.
   e. Register a consumer on the lock queue.
   f. Wait up to `LockAcquisitionTimeoutSeconds` for message delivery.
   g. **If delivered**: return success (same as before).
   h. **If timed out**:
      - Clean up the channel (cancel consumer, close, dispose) — best-effort, same as the existing code (cleanup exceptions are swallowed and do not abort the retry loop).
      - Log a warning with the attempt number and elapsed time.
      - Wait a short, fixed inter-attempt delay before the next iteration to avoid hammering the broker during recovery. This delay must be bounded: it must either be capped at the remaining window time, or the loop must re-check the deadline immediately after the delay completes. The delay should be short relative to `LockAcquisitionTimeoutSeconds` (the delivery phase should choose a value in the range of a few seconds; the delay must not be so long that it prevents timely delivery detection on fast broker recoveries).
4. Any exception from channel creation or consumer registration is caught, logged at `Warning` level (broker recovery is the expected context), and treated the same as a delivery timeout (proceed to cleanup and next-iteration-or-exit logic). This mirrors the existing `catch` block behaviour.
5. The outer `LockReacquisitionTimeout` static field is removed; its timeout role is replaced by the window deadline computed in step 1.

### Logging
- Each failed attempt should log at `Warning` level, including: `resourceKey`, attempt number, elapsed seconds, and remaining window seconds.
- Exceptions during retry iterations should be logged at `Warning` level (not `Error`), since broker recovery exceptions are expected during this window.
- Success after retry (attempt > 1) should log at `Information` level, noting which attempt succeeded and total elapsed time.
- Window exhaustion should log at `Warning` level, noting total attempts made and total elapsed time. The message must not imply exclusive broker fault — window exhaustion can also mean the lock was legitimately claimed by a peer monitor during the retry window. A neutral phrasing (e.g., "lock message not delivered within retry window — another monitor may have claimed the lock") is appropriate.

### Thread Safety and Disposal
- The retry loop must check `disposedFlag` at the top of each iteration (step 3b above). If disposed, return null.
- **Cancellation invariant**: when the loop returns null due to the disposed flag, the caller (`TryReacquireOrCancelAsync`) will proceed to attempt `_lockLostCts.Cancel()`. This is safe: if the lock has been fully disposed by that point, `_lockLostCts` will be disposed and the cancel is caught as `ObjectDisposedException` (already handled in the caller). No special return sentinel is required — null is the correct return value in all failure cases.
- **Inter-attempt sleep during disposal**: the inter-attempt delay must use a cancellation-aware mechanism (e.g., `Task.Delay` linked to the lock's lifetime or a relevant CancellationToken) so that disposal during the sleep terminates the loop promptly rather than waiting for the full delay to expire before the next `disposedFlag` check.
- No changes to the existing `Interlocked`-based re-acquisition guard in `TryReacquireOrCancelAsync`.

---

## 5. Tests

### Branch Strategy
All tests are written on branch `fix/s-002-lock-reacquisition-retry`, committed before the corresponding production changes (TDD ordering).

### Unit Tests (`Dorc.Monitor.Tests`)

**T1 — Default config value is 150 seconds**
- Construct `MonitorConfiguration` with an `IConfigurationRoot` that has no `LockReacquisitionRetryWindowSeconds` entry.
- Assert `LockReacquisitionRetryWindowSeconds` returns `150`.

**T2 — Config value is read from appsettings**
- Construct `MonitorConfiguration` with an `IConfigurationRoot` containing `AppSettings:HighAvailability:LockReacquisitionRetryWindowSeconds = 300`.
- Assert `LockReacquisitionRetryWindowSeconds` returns `300`.

### Integration Tests (`Dorc.Monitor.IntegrationTests`)

Integration tests require a live RabbitMQ broker (same approach as existing `RabbitMqLockIntegrationTests`). These tests confirm the retry loop's observable behaviour end-to-end; mocked channel simulations are insufficient per SC-04.

**IT1 — Retry window exhaustion triggers cancellation**

*Scenario*: Re-acquisition is attempted but the lock message is never deliverable within the window.

*Approach*:
- Configure a service instance with `LockReacquisitionRetryWindowSeconds` set to a short value (e.g., 8 seconds) and `LockAcquisitionTimeoutSeconds` set to a short value (e.g., 2 seconds), to keep test duration reasonable.
- Acquire the distributed lock for a test resource.
- **Before triggering channel loss**, register a test-controlled standby consumer on the same lock queue. With Single-Active-Consumer semantics, this consumer will be in standby behind the service's active consumer.
- Trigger channel loss (close the lock service's channel directly). When the service's channel drops, the lock message is requeued. SAC promotes the test's standby consumer as the new active consumer, which receives and holds the message unacknowledged. The lock service's subsequent retry-loop consumers remain in standby and receive no delivery.
- Assert that `LockLostToken` fires after the configured window elapses, confirming the deployment-cancellation path is triggered.
- Assert that multiple re-acquisition attempts were logged (observable via test log capture), confirming the loop ran more than once.
- *Cleanup*: cancel and close the test-controlled consumer after the assertion phase.

**IT2 — Successful re-acquisition after initial delivery delay**

*Scenario*: The lock message is not immediately deliverable (broker recovering), but becomes consumable partway through the retry window.

*Approach*:
- Configure `LockReacquisitionRetryWindowSeconds = 20`, `LockAcquisitionTimeoutSeconds = 2`.
- Acquire the distributed lock.
- **Before triggering channel loss**, register a test-controlled standby consumer (as in IT1). This consumer will receive the message when the service's channel drops.
- Trigger channel loss.
- Hold the message in the test consumer for a controlled duration that exceeds at least two complete attempt cycles: the blocking duration must be greater than `2 × LockAcquisitionTimeoutSeconds + inter-attempt delay` (e.g., for `LockAcquisitionTimeoutSeconds = 2` and a 3s inter-attempt delay, the block must last more than 7 seconds). This guarantees at least two retry attempts by the service before the message becomes available.
- After the blocking duration, cancel the test consumer (nack-requeue or cancel the consumer subscription). The message is requeued and becomes available to the service's next retry attempt.
- Assert that `LockLostToken` is **not** fired.
- Assert that the lock service logs a successful re-acquisition.
- Assert that at least two complete delivery attempts (each consisting of: channel created, consumer registered, delivery waited) were made before success. "Two attempts" means two full attempt cycles as described in §4, not merely that an attempt counter incremented — confirm via log entries or the attempt counter.
- *Note to delivery*: the test must register the blocking consumer before triggering channel loss to ensure it is promoted (not the retry loop's first consumer). The ordering — register blocker, then trigger channel close — is required to achieve the intended test scenario.

---

## 6. Commit Strategy

1. **Commit 1**: Configuration interface, implementation, and appsettings change (`IMonitorConfiguration`, `MonitorConfiguration`, `appsettings.json`).
2. **Commit 2**: Unit tests (T1, T2) for the new config property — tests should pass after commit 1.
3. **Commit 3**: Integration test skeletons for IT1 and IT2 — committed before production code, asserting the new retry behaviour (tests will initially fail).
4. **Commit 4**: Production change to `TryReacquireLockChannelAsync` — replace single-attempt with retry loop, remove `LockReacquisitionTimeout` static field.
5. **Commit 5**: Verify all tests pass; clean up any test helper code introduced.

*Note*: The delivery phase determines the final commit count and exact messages. The above is a guide to TDD ordering, not a prescription.

---

## 7. Acceptance Criteria

- [ ] `IMonitorConfiguration` has a `LockReacquisitionRetryWindowSeconds` property.
- [ ] `MonitorConfiguration` reads the config key with a default of `150`; test T1 passes.
- [ ] `appsettings.json` includes `LockReacquisitionRetryWindowSeconds: 150` under `HighAvailability`.
- [ ] The static `LockReacquisitionTimeout` field is removed from `RabbitMqDistributedLockService`; no equivalent hard-coded timeout cap is re-introduced.
- [ ] `TryReacquireLockChannelAsync` checks the deadline at the top of each iteration (before creating a channel) and exits when it is exceeded.
- [ ] Each failed attempt is logged at Warning level with attempt number and elapsed/remaining time. The window-exhaustion message is phrased neutrally (does not imply exclusive broker fault).
- [ ] Exceptions during retry iterations are logged at `Warning` level, not `Error`.
- [ ] Window exhaustion results in null return (unchanged caller behaviour → cancellation triggered).
- [ ] The inter-attempt delay is cancellation-aware and is capped so it does not extend the total elapsed time materially beyond the configured window.
- [ ] Integration test IT1 passes: deployment is correctly cancelled after a short window with no deliverable message, and multiple retry attempts are logged.
- [ ] Integration test IT2 passes: deployment continues after delayed-but-within-window message delivery, with at least two complete attempt cycles logged before success.
- [ ] Existing lock acquisition, release, and fast-re-acquisition paths are unaffected (existing tests pass).
- [ ] The build compiles and all tests (unit + integration) pass on the feature branch.

---

## 8. Review History

| Round | Reviewer           | Outcome          | Date       |
|-------|--------------------|------------------|------------|
| R1    | Claude Opus 4.6    | REQUEST CHANGES  | 2026-03-23 |
| R1    | Claude Sonnet 4.6  | REQUEST CHANGES  | 2026-03-23 |
| R1    | GPT 5.2-codex      | REQUEST CHANGES  | 2026-03-23 |
| R2    | Claude Opus 4.6    | APPROVE          | 2026-03-23 |
| R2    | Claude Sonnet 4.6  | APPROVE          | 2026-03-23 |
| R2    | GPT 5.2-codex      | APPROVE          | 2026-03-23 |

### Code Review — R1 (2026-03-23)

| Finding | Reviewer | Severity | Disposition | Resolution |
|---------|----------|----------|-------------|------------|
| `serviceCts?.Token ?? CancellationToken.None` null-conditional is dead code | Sonnet 4.6 (×2) | MEDIUM | Accept | Removed null-conditional: `await Task.Delay(actualDelay, serviceCts.Token)` — `serviceCts` is a non-nullable readonly field |
| Elapsed calculation in exhaustion log wrong | All 3 | MEDIUM | Reject | Math is correct: `retryWindowSeconds - remaining.TotalSeconds` when `remaining ≤ 0` = `currentTime - startTime` = actual elapsed |
| IT1 "multiple attempts" not via log capture | Sonnet 4.6 | HIGH → LOW | Downgrade | Spec wording is guidance; elapsed-time assertion is equivalent proof; spec R2 approved language |
| IT2 timing math fragile on slow CI | Sonnet 4.6 | MEDIUM → LOW | Downgrade | Blocking duration formula explicitly described and approved in spec §5 IT2 |
| `disposedFlag` not at loop top | All 3 | MEDIUM → LOW | Downgrade | Spec Thread Safety allows `serviceCts.Token` as equivalent mechanism |
| `connectionCts` first-iteration full-window risk | Sonnet 4.6 | MEDIUM → LOW | Downgrade | Not a spec requirement; bounded by OS TCP timeout |
| appsettings string vs integer | Sonnet 4.6 | LOW | Reject | Consistent with existing `LockAcquisitionTimeoutSeconds` string pattern |
| Semaphore `CancellationToken.None` | Sonnet 4.6 | LOW | Reject | Correct pattern for near-instantaneous read |

### Code Review — R2 (2026-03-23) — UNANIMOUS APPROVAL

**Panel:** Claude Opus 4.6, Claude Sonnet 4.6, GPT-5.2-codex

R1 fix verified correct: `serviceCts.Token` is valid after `Cancel()` (token is in cancelled state, `Task.Delay` throws `OperationCanceledException` which is caught). `serviceCts` is non-nullable; the null-conditional was dead code. One LOW non-blocking observation: `continue`→`return` path in inter-attempt delay section skips exhaustion log in a narrow timing edge case; per-attempt Warning logs provide sufficient operator visibility.

### R1 Findings Addressed (spec review)

| Finding | Reviewer | Severity | Disposition | Resolution |
|---------|----------|----------|-------------|------------|
| Window deadline semantics ambiguous (start vs. complete attempt) | Opus 4.6 | HIGH | Accept | Added §4 step 3a: deadline checked at top of iteration before creating channel |
| SAC log message misleads operators on exhaustion cause; channel-recreation rationale missing | Sonnet 4.6 | HIGH | Accept | Added neutral phrasing requirement to §4 Logging; added channel-recreation rationale to §4 step 3d |
| IT1 "exclusive competing consumer" mechanically unsound for SAC | Sonnet 4.6, GPT 5.2 | HIGH | Accept | Replaced with SAC-compatible standby consumer approach (register before channel loss, SAC promotes blocker) |
| 150s default framing inconsistent ("minimum floor" claim) | GPT 5.2 | HIGH | Accept | Replaced with "midpoint estimate based on single observation"; added operator guidance |
| Cleanup exception handling during retry not explicit | GPT 5.2 | HIGH | Downgrade to LOW | Spec already defers to "same cleanup as existing code" which uses swallowed exceptions; added explicit "best-effort, exceptions swallowed" note to §4 step 3h |
| disposedFlag invariant underspecified — null-from-disposal safety | Opus 4.6, Sonnet 4.6, GPT 5.2 | MEDIUM | Accept | Rewrote Thread Safety section with explicit invariant and ObjectDisposedException safety explanation |
| IT2 delay calibration floor not stated | Opus 4.6 | MEDIUM | Accept | Added formula: blocking duration > 2 × LockAcquisitionTimeoutSeconds + inter-attempt delay |
| Inter-attempt delay unbounded; sleep overshoot possible | Opus 4.6, GPT 5.2 | MEDIUM | Accept | Added requirement to cap delay at remaining window or re-check after delay; added AC |
| Disposal during inter-attempt sleep not interruptible | GPT 5.2 | MEDIUM | Accept | Added cancellation-aware sleep requirement to Thread Safety section |
| Window ≤ per-attempt timeout edge case silent | Sonnet 4.6 | MEDIUM | Accept (LOW) | Added edge case note to §3 |
| IT2 timing fragility (ordering with blocking consumer) | Sonnet 4.6 | LOW | Accept | Added note to IT2: register blocker before triggering channel close |
| Exception log level during retry unspecified | Opus 4.6 | LOW | Accept | Added Warning-level guidance for retry exceptions to §4 Logging |
| IT2 "two attempts" ambiguous (complete cycles vs. counter) | GPT 5.2 | LOW | Accept | Clarified as "two full attempt cycles" in IT2 |
| Replacement behavior not verified by test | GPT 5.2 | LOW | Defer to Delivery | Code review is the mechanism; AC wording updated to "no equivalent cap re-introduced" |
