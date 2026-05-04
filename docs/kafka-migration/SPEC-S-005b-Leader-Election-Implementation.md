# JIT Spec — S-005b: Leader-Election Implementation

| Field | Value |
|---|---|
| **Status** | APPROVED — Executed 2026-04-14 (user-approved; code-review R1 unanimous APPROVE WITH MINOR) |
| **Author** | Claude (Opus 4.6) |
| **Created** | 2026-04-14 |
| **Step ID** | S-005b |
| **Governing ADR** | `ADR-S-005-Leader-Election.md` (APPROVED user-approved 2026-04-14) |
| **Governing IS** | `IS-Kafka-Migration.md` §3 S-005 |
| **Governing HLPS** | `HLPS-Kafka-Migration.md` §5.1, R-1, R-2, U-11 |

---

## 1. Purpose & Scope

Replace `Dorc.Monitor`'s `RabbitMqDistributedLockService` with a Kafka-consumer-group-based implementation per the ADR-S-005 decision (option (i)): **partition ownership on a dedicated lock topic = lock ownership** for any resource-key that hashes to that partition. Idempotent or monotonic handlers absorb the cooperative-rebalance two-leader window per HLPS R-1.

### In scope

- A new `Dorc.Kafka.Lock` project housing:
  - `KafkaLocksOptions` + validator (bootstrap-reuse from S-002; lock-topic name, partition count, session/heartbeat timings).
  - `KafkaLockCoordinator` — a singleton per-Monitor-process service that owns the single Kafka consumer, joins the group, and tracks currently-owned partitions thread-safely.
  - `KafkaDistributedLockService : IDistributedLockService` — exposes the unchanged `TryAcquireLockAsync` surface; delegates partition-ownership queries to the coordinator.
  - `KafkaDistributedLock : IDistributedLock` — handle returned from `TryAcquireLockAsync`; its `LockLostToken` is wired to the coordinator's per-partition revoke/lost signal; `Dispose`/`DisposeAsync` release the per-resource lease.
  - `AddDorcKafkaDistributedLock(IServiceCollection, IConfiguration)` — DI extension.
- Lock-topic provisioning hook (one more topic on top of the S-007 provisioner), 12 partitions, RF = `KafkaLocksOptions.ReplicationFactor` (default 3, 1 for dev compose).
- Substrate-selector flag `Kafka:Substrate:DistributedLock = Rabbit | Kafka` (default `Rabbit`). Same pattern as S-006/S-007 substrate flags; removed in S-009.
- `Dorc.Monitor` DI wiring: register both the existing `RabbitMqDistributedLockService` and the new `KafkaDistributedLockService` as concrete services; a factory method picks the active one based on the flag.
- **Sub-deliverable 1: Handler Monotonicity Audit** — separate doc `docs/kafka-migration/S-005b-Handler-Monotonicity-Audit.md` enumerating every state-transition write path that runs under a distributed lock today; verifies each is (a) pure-idempotent or (b) monotonic-guarded (per ADR §4 Consequence #3). Any handler that is neither gets a guard-column / predicate added in the same step.
- **Sub-deliverable 2: `TryAcquireLockAsync` Caller Survey** — separate doc `docs/kafka-migration/S-005b-Caller-Survey.md` enumerating every `IDistributedLockService.TryAcquireLockAsync` call site in `Dorc.Monitor`; confirms no caller depends on the fail-fast-null semantic that the Kafka impl no longer provides (blocks-until-ownership instead).
- **Sub-deliverable 3: HA test suite** — productionised version of the S-005a POC drivers under `src/Dorc.Kafka.Lock.HATests/` (or similar); exercises the IS SC-2a/2b/2c bars (≤60 s partition reassignment after leader-kill, ≤30 s new-deployment acceptance post-failover, ≥20 rebalances with zero duplicate Request state-transitions).

### Out of scope

- Removal of `RabbitMqDistributedLockService` — **S-009** cuts the pre-kafka-cutover tag first, then removes it.
- The Kafka-based request-lifecycle pub/sub — **S-006**.
- Changes to the `IDistributedLockService` / `IDistributedLock` interface shapes — they stay unchanged so both Rabbit and Kafka impls share the surface until S-009.
- Operational runbook for lock-failover diagnosis — **S-010** cutover runbook.

---

## 2. Requirements

### R-1 — `KafkaLockCoordinator` (the singleton consumer wrapper)

One instance per `Dorc.Monitor` process. Holds the single Kafka consumer joined to the lock-topic consumer group. Responsible for:

- Maintaining a thread-safe `IReadOnlyDictionary<Partition, CancellationTokenSource>` whose keys are currently-owned partitions and whose values are the CTS fired on revoke/lost. The dictionary swap is **atomic per partition**: on a revoke→reassign sequence for partition P, the old CTS is cancelled and a fresh CTS is published in the same critical section so a concurrent `TryAcquireLockAsync(K)` that resolves to P never observes the stale (cancelled) token as "still live". On a **no-op rebalance assignment** (partition P already owned, received again) the existing CTS is reused unchanged — recycling is scoped to the revoke→assign cycle for that specific partition, not fired on every assignment event.
- Running a background consume loop so the Kafka client sends protocol-level heartbeats on schedule (ADR §4 #1). No user records are produced or consumed for lock semantics — the loop exists to keep the group coordinator happy. Any records that do arrive on the topic (future use) are silently discarded without affecting offsets.
- `EnableAutoCommit` can be **true** for the lock consumer — commit semantics are not used for lock correctness (ADR §2). Setting it false is acceptable too; choice is Delivery-phase.
- Exposing the per-partition CTS so the lock service can wire `LockLostToken` to it.
- Exposing a `WaitForPartitionOwnershipAsync(Partition, CancellationToken)` API that the lock service uses to block until ownership. **Signalling intent:** implemented via a `TaskCompletionSource`-per-partition fired from the `SetPartitionsAssignedHandler` callback — **not** via polling the ownership dictionary. Bounded-wait semantics (timeouts) are the **lock service's** responsibility via a linked CancellationToken; the coordinator's API surface takes only an unbounded CancellationToken and expects the caller to cap it.
- `IAsyncDisposable` — on shutdown closes the consumer and signals every per-partition CTS.

Uses S-002's `KafkaConnectionProvider` for SASL / bootstrap / timeouts. `PartitionAssignmentStrategy` = `CooperativeSticky` (S-002 default). Reuses S-002's `KafkaRebalanceHandlers` class for §4.3 log-shape parity so operators see the same "partitions incrementally assigned / revoked / lost" structured logs as the S-007 consumer.

### R-2 — `KafkaDistributedLockService : IDistributedLockService`

Preserves the existing interface verbatim. Implementation:

- `IsEnabled` — reads `KafkaLocksOptions.Enabled` (default `true` when this impl is registered; the substrate flag gates whether this type is registered at all).
- `TryAcquireLockAsync(string resourceKey, int leaseTimeMs, CancellationToken ct)`:
  1. Compute the target partition via Kafka's `MurmurHash2` partitioner against `resourceKey` bytes and the configured partition count.
  2. Call `_coordinator.WaitForPartitionOwnershipAsync(partition, linkedCt)` where `linkedCt` is a linked token combining the caller's `ct` and a bounded timeout derived from `leaseTimeMs` (the Rabbit impl used `leaseTimeMs` as a message TTL; here we reinterpret it as "max time we're willing to wait for partition ownership before giving up").
  3. On ownership acquisition, construct a `KafkaDistributedLock` handle whose `LockLostToken` is the coordinator's per-partition CTS.Token.
  4. On timeout or caller cancellation, return `null` (preserves the interface's contention-semantic as far as the caller cares about it — *this* semantic preservation is the key change from the naive "block forever" model). This is the bounded-wait overload the caller survey (sub-deliverable 2) may require.

**ADR-deviation note:** ADR §4 API-parity-gap described `leaseTimeMs` as "advisory only" and the method as "blocks on `TryAcquireLockAsync` until the calling Monitor instance owns the target partition (or the CancellationToken fires)". This spec reinterprets `leaseTimeMs` as a **wait-cap with `null`-return on timeout** to better preserve the legacy `IDistributedLockService` contract's "null on contention" signalling. The reinterpretation is defensible engineering (unbounded blocking under a broker outage is a worse caller contract than bounded-wait-with-null), but **R-7's caller survey is explicitly bound to confirm every existing caller tolerates both the blocks-or-cancels and the new null-on-wait-timeout paths**.

Thread-safe for concurrent callers (different resource keys may hash to different partitions).

### R-3 — `KafkaDistributedLock : IDistributedLock`

- `ResourceKey` — as passed to `TryAcquireLockAsync`.
- `IsValid` — `!LockLostToken.IsCancellationRequested`.
- `LockLostToken` — the per-partition CTS from the coordinator; fires on:
  - Cooperative-rebalance revoke (`SetPartitionsRevokedHandler`).
  - Session-timeout partition loss (`SetPartitionsLostHandler`).
  - Coordinator shutdown.
  It does **not** fire on `assignment → assignment` of the same partition (no-op rebalance). CTS recycling is scoped to a **revoke/lost → reassign** cycle for that specific partition (see R-1 atomic-swap rule); a fresh lock acquire after such a cycle gets a fresh token scope, while acquires across no-op rebalances continue to observe the same live token.
- `Dispose` / `DisposeAsync` — best-effort; the lock is "released" automatically when the handle is disposed by virtue of the calling deployment completing; no explicit release message is sent to the broker (there is none to send in the partition-ownership model).

### R-4 — Options + validator

`KafkaLocksOptions` (bind section: `Kafka:Locks`):

- `Enabled` (bool, default `true`).
- `Topic` (string, default `dorc.locks`).
- `PartitionCount` (int, default `12`). Validated `>= 1`. Immutable post-cutover per ADR §4 Consequence #2 — the validator does not enforce this (it's an operational constraint), but the value is logged at startup and the S-010 runbook flags it.
- `ReplicationFactor` (short, default `3`, dev override `1`). Validated `>= 1`.
- `ConsumerGroupId` (string, default `dorc.monitor.locks`). All Monitor replicas share this group id — partitions split across the fleet. Distinct from S-007's per-replica group id pattern (S-007 needs every replica to see every event; S-005b needs exactly-one-leader-per-partition across the fleet, which is the shared-group-id model).
- `LockWaitDefaultTimeoutMs` (int, default `30_000`) — used when the caller passes `leaseTimeMs <= 0`.

Validator fails fast at host build on invalid values with key-qualified message (S-002 pattern).

### R-5 — DI extension + Monitor wiring

`AddDorcKafkaDistributedLock(IServiceCollection, IConfiguration)`:

- Binds `KafkaLocksOptions` + validator.
- Idempotent via marker-singleton.
- Reads `Kafka:Substrate:DistributedLock` (enum `Rabbit | Kafka`, default `Rabbit`). The `Rabbit` path is a no-op (keeps whatever `IDistributedLockService` was registered upstream, i.e. `RabbitMqDistributedLockService`). The `Kafka` path:
  - Registers `KafkaLockCoordinator` as singleton + hosted-service.
  - Registers `KafkaDistributedLockService` as concrete and ensures it is the active `IDistributedLockService` registration (Delivery-phase DI mechanism; the requirement is behavioural — Kafka impl wins resolution).
  - Registers an additional topic provisioner (could be the S-007 provisioner with `dorc.locks` added to its list, or a separate one — Delivery choice).

`KafkaSubstrateOptions` from S-007 gets one more property (`DistributedLock`, enum `Rabbit|Kafka`, default `Rabbit`). S-007 reads it forward-compat-only; S-005b binds it to behaviour. Mirror of the pattern S-006 inherits from S-007.

### R-6 — Handler monotonicity audit (sub-deliverable)

A new doc `docs/kafka-migration/S-005b-Handler-Monotonicity-Audit.md` enumerating every `Dorc.Monitor` code path that writes DB state under a distributed lock today. For each path:

- Identify the write (table + columns + shape of SQL).
- Classify as **pure-idempotent** (same input → same DB state regardless of invocation count) or **monotonic-guarded** (WHERE-clause predecessor check or monotonically-increasing version/timestamp column that guards the write).
- Any path that is neither gets a remediation in this step — typically add a `WHERE CurrentStatus IN (allowed-predecessors)` predicate or a version-column guard.

The audit is reviewer-scrutable: each row cites the file + line of the write.

### R-7 — `TryAcquireLockAsync` caller survey (sub-deliverable)

A new doc `docs/kafka-migration/S-005b-Caller-Survey.md` enumerating every `IDistributedLockService.TryAcquireLockAsync` call site in `Dorc.Monitor` (and any other project that resolves it via DI). For each:

- Identify the caller + resource-key shape.
- Classify as **fail-fast-null-tolerant** (caller handles a `null` return by doing nothing / trying later) or **blocking-OK** (caller already awaits and proceeds).
- Per R-2, the Kafka impl preserves `null`-return on timeout/ct-fire, so most existing callers stay compatible. Any caller that relies on `null`-return for a specific non-wait-then-retry pattern (e.g. "skip this request if someone else is holding the lock") gets explicit tolerance verification.

**Survey scope explicitly covers the R-2 null-on-wait-timeout shim** (not just blocks-OK vs fail-fast-null) — the audit must classify each caller against both the legacy Rabbit-impl contract (null on contention) and the new Kafka-impl contract (null on wait-cap timeout or caller cancellation). Most callers are expected to tolerate both symmetrically, but the audit enumerates.

**Exit branch:** if the survey finds a caller that depends on the legacy semantic in a way the Kafka impl cannot preserve (e.g. relies on *immediate* null on any contention without waiting), remediation is **in-scope for S-005b** — either refactor the caller or add a bounded-wait overload with an explicit `TryAcquireLockImmediateAsync` shape. Non-trivial remediations trigger user escalation; the step does not close with an unresolved incompatible caller.

### R-8 — HA test suite (sub-deliverable)

New test project `src/Dorc.Kafka.Lock.HATests/` (or similar name of Delivery's choosing). Exercises the IS SC-2a / SC-2b / SC-2c bars:

- **SC-2a** — leader-kill test: spin up N ≥ 2 Monitor-shaped candidates; kill the one owning a specific partition; assert ownership reassigned within 60 s.
- **SC-2b** — new-deployment acceptance post-failover: submit a deployment whose resource-key hashes to the freshly-reassigned partition; assert acceptance by the new leader within a further 30 s.
- **SC-2c** — ≥20 induced rebalances with zero duplicate Request state-transitions. Uses the same monotonicity-guarded handler shape the production Monitor will use (or a close simulation). The audit query that exercises the check is part of this sub-deliverable per IS S-005.

Tests require a live compose stack (or Aiven non-prod with credentials); they are long-running and opt-in via env var `DORC_KAFKA_HA_TESTS=1` (same pattern as other S-00x integration tests).

**Definition of Done — HA evidence capture:** S-005b closes only after **at least one observed HA-suite green run** with the env var set; the evidence transcript (SC-2a/2b/2c pass verification) is captured under `docs/kafka-migration/S-005b-HA-evidence/<timestamp>/` and cited in the step's completion record. Tests existing + compiling is insufficient — IS S-005 SC-2a/2b/2c demand an observed pass.

**AT-5 test-substrate note:** the regression bar permits a SQLite-backed state table as the "Monitor-shaped handler" sink for test-run cost reasons (in-process; no external infra). This is acceptable because the Safety Property the test exercises (`no (RequestId, Version) duplicate rows`) is a property of the handler's monotonic-guard predicate, not of the SQL dialect — SQLite's isolation-level behaviour is not a material difference here. If production reveals a SQL-Server-specific isolation-level edge case under concurrent writes from a two-leader window, escalate post-cutover and revisit with a containerised SQL Server test environment.

### R-9 — Observability

Reuse S-002's `KafkaRebalanceHandlers` for the §4.3-shape partition events. Lock-acquisition / lock-loss events emit structured log entries with at minimum: `ResourceKey`, `Partition`, `Outcome` (`acquired` / `wait-timeout` / `caller-cancelled` / `lock-lost`). No new observability primitive — same `Microsoft.Extensions.Logging` pipeline.

---

## 3. Out of Scope (explicit)

- Removal of `RabbitMqDistributedLockService` — **S-009**.
- Changes to `IDistributedLockService` or `IDistributedLock` interfaces — neutral preservation is required for the Rabbit/Kafka substrate flag to work.
- Request-lifecycle pub/sub — **S-006**.
- Operator runbook for lock-failover, cluster-wide partition-count change, etc. — **S-010**.
- Fencing tokens — deferred per ADR §3 "Why not (2)". May be reopened post-cutover if production reveals a case idempotency doesn't cover.

---

## 4. Acceptance Criteria

### AT-1 — Lock acquisition + release for ≥2 distinct resource keys

Unit test (or lightweight integration against compose):

- Acquire a lock for `resourceKey-A`. Verify `IDistributedLock.IsValid == true` and `LockLostToken.IsCancellationRequested == false`.
- Acquire a lock for `resourceKey-B` concurrently. Verify both succeed (keys hash to different partitions, or if they collide the second waits and ultimately acquires after the first releases).
- Dispose both. Subsequent re-acquire succeeds.

### AT-2 — Single-leader-at-a-time

Integration test: two `KafkaLockCoordinator` instances in the same consumer group. Both attempt `TryAcquireLockAsync(K, ...)` concurrently for the same `K`. Exactly one succeeds; the other blocks. On first-instance dispose + rebalance, second acquires.

### AT-3 — SC-2a leader-kill failover ≤ 60 s

As R-8 sub-deliverable: kill the owning candidate process; assert the `LockLostToken` fires on the dead one's handle before process exit (observed in final log), and a fresh acquire on another candidate succeeds within 60 s.

### AT-4 — SC-2b new-deployment acceptance post-failover ≤ 30 s

Continue from AT-3: after partition reassignment, submit a fresh acquire for the same resource-key via the new leader; acquire succeeds within 30 s of partition-assignment event.

### AT-5 — SC-2c ≥20 rebalances with zero duplicate Request state-transitions

Using a realistic Monitor-shaped handler (or a close simulation that performs a monotonic-guarded UPSERT against a SQLite DB), drive ≥20 induced rebalances while producing a burst of synthetic state-transition requests. Assert post-run that no `(RequestId, Version)` pair appears twice in the state table.

### AT-6 — Handler monotonicity audit deliverable exists

`docs/kafka-migration/S-005b-Handler-Monotonicity-Audit.md` exists, enumerates every state-transition write path, classifies each as pure-idempotent or monotonic-guarded, and records remediations for any that were not. Reviewer can re-run the audit by following the cited code paths.

### AT-7 — Caller survey deliverable exists

`docs/kafka-migration/S-005b-Caller-Survey.md` exists, enumerates every `IDistributedLockService.TryAcquireLockAsync` call site, and confirms Kafka-impl compatibility for each.

### AT-8 — DI substrate flag switches behaviour

Unit test: with `Kafka:Substrate:DistributedLock = Rabbit` (or unset), DI resolves `RabbitMqDistributedLockService` (no change from today). With `= Kafka`, DI resolves `KafkaDistributedLockService` and `KafkaLockCoordinator` is registered as a hosted service.

### AT-9 — Options validator fails fast on invalid values

Unit test: invalid `PartitionCount` / `ReplicationFactor` / enum strings throw `OptionsValidationException` at host build with the failing key in the message.

### AT-10 — `LockLostToken` fires on both lock-loss shapes

Unit test with a fake coordinator (or integration): acquire a lock, force a `PartitionsRevokedHandler` callback — `LockLostToken` fires. Separately: acquire a lock, force a `PartitionsLostHandler` callback — `LockLostToken` fires.

---

## 5. Accepted Risks

| Risk | Source | Disposition |
|---|---|---|
| Cooperative-rebalance two-leader window; idempotency/monotonicity absorbs it | ADR §6 | Accepted — R-6 audit is the guard rail. |
| POC proved mechanism (n=1); SC-2c ≥20 bar carried to AT-5 here | ADR §6 | Accepted — AT-5 is the production-grade proof. |
| `MurmurHash2` false-sharing head-of-line blocking | ADR §6 | Accepted — 12 partitions default; raise in a follow-up if contention observed. |
| Partition-count immutability post-cutover | ADR §4 #2 | Accepted — S-010 runbook concern. |
| `TryAcquireLockAsync` becomes waiting-with-timeout rather than pure fail-fast; timeout-on-null is the compatibility shim | R-2 + R-7 | Accepted — R-7 audit confirms no caller depends on the old semantic. |
| `KafkaLockCoordinator` background consume loop consumes nothing meaningful — the Kafka client's heartbeat thread does the real work; an empty lock-topic is fine | ADR §4 #1 | Accepted — operators who grep for "lock activity" will see only rebalance events, which is the intended surface. |
| Shared `ConsumerGroupId` across Monitor replicas (vs S-007's per-replica group) — this is the mechanism that makes partition ownership mutually exclusive | R-4 | Accepted — different use case to S-007; both patterns documented. |
| HA test suite (R-8) is long-running + opt-in; CI pipeline runs only the fast AT-1/AT-2/AT-8/AT-9/AT-10 unit tests by default | R-8 | Accepted — CI runs on every push; HA suite runs on a slower cadence. |

---

## 6. Delivery Notes

- **Branch:** continue on `feat/kafka-migration` per IS §1.
- **Project layout:** `src/Dorc.Kafka.Lock/` for production code; `src/Dorc.Kafka.Lock.Tests/` for unit tests; HA suite project naming is Delivery's choice.
- **Monitor DI:** `Dorc.Monitor` gains a ProjectReference to `Dorc.Kafka.Lock`; `Program.cs` (or equivalent bootstrap) calls `AddDorcKafkaDistributedLock(builder.Configuration)` after the existing Rabbit-lock registration, same pattern as S-007 in `Dorc.Api`.
- **Sub-deliverables (audits) land as markdown docs before AT-6/AT-7 can close.**
- **HA suite (R-8) is opt-in:** env var `DORC_KAFKA_HA_TESTS=1` gates execution so CI default stays fast.

---

## 7. Review Scope Notes for Adversarial Review

Reviewers should evaluate:

- Whether R-1..R-9 + AT-1..AT-10 collectively implement the ADR bindings (§4 Consequences #1–#6 of the ADR).
- Whether the monotonicity / caller-survey sub-deliverables are sized appropriately for an ADR-phase audit (not under-scoped, not over-scoped).
- HA test suite design vs IS SC-2a/2b/2c — does the three-number-bar actually get exercised?
- Risk coverage in §5.

Reviewers should **NOT**:

- Re-litigate the ADR decision (option i chosen, option ii deferred, option iii fallback).
- Demand exact method signatures / field names / test-project names.
- Demand the audit be co-authored in this spec — it's a sub-deliverable.

---

## 8. Review History

### R1 (2026-04-14) — UNANIMOUS APPROVE WITH MINOR

Panel: Sonnet-4.6, Gemini-Pro-3.1, GPT-5.3-codex. Verdicts: APPROVE WITH MINOR × 3. No HIGH/CRITICAL findings.

| ID | Reviewer(s) | Severity | Finding | Disposition |
|---|---|---|---|---|
| Gemini-G1 / Sonnet-F4 | Gemini, Sonnet | MEDIUM | `LockLostToken` CTS recycling race on revoke→reassign + no-op-rebalance wording contradiction | **Accepted** — R-1 now requires atomic per-partition dictionary swap on revoke→reassign + explicitly scopes recycling to that cycle only. R-3 reworded to remove contradiction. |
| Gemini-G2 | Gemini | MEDIUM | `WaitForPartitionOwnershipAsync` signalling primitive not specified (polling vs TCS-per-partition) | **Accepted** — R-1 now names `TaskCompletionSource`-per-partition signalled from `SetPartitionsAssignedHandler` as the intent; also clarifies that bounded-wait is the lock-service's concern, not the coordinator's API. |
| Sonnet-F1 | Sonnet | MEDIUM | R-1 vs R-2 wait-API contract inconsistency | **Accepted** — subsumed by Gemini-G2 fix; R-1 now explicit that the coordinator's API is unbounded, timeouts are the caller's responsibility. |
| Sonnet-F2 / GPT-F1 | Sonnet, GPT | MEDIUM | R-7 survey scope missing the null-on-wait-timeout shim; no exit branch for incompatible callers | **Accepted** — R-7 now explicitly covers both the legacy and new null-return semantics; exit branch states remediation is in-scope for S-005b (refactor caller or add `TryAcquireLockImmediateAsync`-style bounded-wait overload). R-2 carries an ADR-deviation note. |
| Sonnet-F3 | Sonnet | MEDIUM | AT-5 SQLite vs production SQL Server isolation-level gap | **Accepted** — R-8 / AT-5 now carries a test-substrate note justifying SQLite for the Safety Property at stake (monotonic-guard predicate is the property, not the SQL dialect), with an escalation branch if production reveals a SQL-Server-specific edge case. |
| GPT-F2 | GPT | MEDIUM | HA-suite env-var gate vs IS SC-2a/2b/2c closure evidence | **Accepted** — R-8 now carries an explicit Definition of Done: S-005b closes only after at least one observed HA-suite green run with evidence captured under `docs/kafka-migration/S-005b-HA-evidence/<timestamp>/`. |
| Sonnet-F5 / GPT-F3 | Sonnet, GPT | LOW | §7 typo "R-1..R-10" should be "R-1..R-9" | **Accepted** — fixed. |
| Sonnet-F6 | Sonnet | LOW | R-5 `services.Replace` crosses abstraction level | **Accepted** — rephrased as behavioural requirement (Kafka impl wins resolution). |
| Gemini-G3 | Gemini | LOW | AT-5 rebalance-induction mechanism hint (join/leave vs SIGKILL) | Defer to Delivery — join/leave keeps wall-clock bounded; Delivery choice. |
| Gemini-G4 | Gemini | LOW | Substrate-flag coexistence AT with S-007 | Defer to Delivery — the R-5 design mirrors S-007's pattern; runtime coexistence will be exercised by Monitor startup. |
| GPT-F4 | GPT | LOW | R-3 explicit naming of `SetPartitionsAssignedHandler` reset | Addressed by Gemini-G1 / Sonnet-F4 fix (R-1 now references it by name). |

All 6 MEDIUMs accepted and resolved via surgical edits; LOWs accepted or deferred. No re-litigation of HLPS / IS / ADR decisions. Status transitions: `IN REVIEW (R1)` → `APPROVED — Pending user approval`.

### Code Review R1 (2026-04-14) — UNANIMOUS APPROVE WITH MINOR

Panel: Sonnet-4.6, Gemini-Pro-3.1, GPT-5.3-codex. Verdicts: APPROVE WITH MINOR × 3. No HIGH/CRITICAL findings.

| ID | Reviewer(s) | Severity | Finding | Disposition |
|---|---|---|---|---|
| Convergent: Sonnet-F02 / Gemini-G1 / GPT-F1 | all three | LOW/MEDIUM | `OnRevokedOrLost` cancels old slot CTS but does not cancel the old `AcquiredTcs` — an orphan-waiter edge (caller captured incomplete TCS, partition revoked-without-assign) relies solely on caller-side cancellation for liveness. | **Accepted** — `OnRevokedOrLost` now also calls `slot.AcquiredTcs.TrySetCanceled()` before replacing the slot. |
| Gemini-G4 | Gemini | LOW | After `WaitForPartitionOwnershipAsync` returns, the token could already be cancelled (revoke raced with TCS completion); service would still log "acquired" and hand the caller a dead handle. | **Accepted** — service now checks `lockLostToken.IsCancellationRequested` post-wait; returns null with distinct "revoked-before-observe" outcome log on the race. |
| Sonnet-F01 | Sonnet | MEDIUM | No-op-reassign guard uses `AcquiredTcs.Task.IsCompleted` as the "already owned" signal — intent-couples to TCS state. | Defer to follow-up — current implementation is correct; an explicit `bool _owned` is stylistic polish. |
| Gemini-G7 | Gemini | LOW | `ConcurrentDictionary<int, PartitionSlot>` is accessed only under `_slotLock` — the concurrent container is redundant. | Defer to follow-up — cosmetic; current code is correct. |
| Gemini-G2 / G3 / G5, GPT-F2..F5, Sonnet-F03..F12 | various | LOW/INFO | Polish nits (Consume cancellation token, Dispose double-teardown, Replace vs RemoveAll, options bind in Direct branch, admin timeout, log symmetry, test-assertion brittleness). | Defer to follow-up — none are correctness concerns; accepted risks per §5. |

Fixes applied in-place; tests re-run green (26/26). No new files or public-surface changes beyond the two edits above. Status transitions: `APPROVED — Pending user approval` → `APPROVED — Executed 2026-04-14`.
