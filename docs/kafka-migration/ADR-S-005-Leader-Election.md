# ADR-S-005 — Leader Election Mechanism

| Field | Value |
|---|---|
| **Status** | PROPOSED (pending user approval) |
| **Author** | Claude (Opus 4.6) |
| **Date** | 2026-04-14 |
| **Governing Spec** | `SPEC-S-005a-Leader-Election-Decision-Spike.md` (APPROVED R1, user-approved 2026-04-14) |
| **Governing HLPS** | `HLPS-Kafka-Migration.md` §5.1 (Safety Property), R-1, R-2, U-11 |
| **Supersedes** | `RabbitMqDistributedLockService` — removed in S-009 |

## 1. Context

The current `Dorc.Monitor` uses `RabbitMqDistributedLockService` for per-resource distributed locking against arbitrary string keys (e.g. `request:123`, `env:Production`). Each deployment acquires a lock for its target resource before the Monitor applies state transitions. The RabbitMQ dependency must be removed per the Kafka migration (SC-1). S-005a is the decision spike choosing the replacement mechanism.

Three options were on the table (SPEC §1):

1. **Kafka consumer-group single-partition leader election + Request-level idempotency.** Partition ownership is the lock signal; per-resource ordering relies on keyed messages going to the same partition; idempotent handlers tolerate the cooperative-rebalance two-leader window.
2. **(1) + fencing tokens.** As (1), but every DB write carries a monotonically-increasing token so stale-leader writes are rejected at the DB.
3. **SQL Server advisory-lock fallback.** Keep per-resource locking on `sp_getapplock`; Kafka carries event flow only.

## 2. Decision

**Adopt option (1) — Kafka consumer-group + Request-level idempotency.**

Rationale:

- **Removes the RabbitMQ runtime dependency** (SC-1), which is the primary HLPS objective.
- **Natural fit with the rest of the S-00x design** — S-002 already ships cooperative-sticky partition assignment; S-003 ships Avro schemas; S-007 already uses consumer-group partition assignment for the results-status consumer. Option (1) reuses the same machinery.
- **User directive** (2026-04-14): *"with kafka and auto commit offsets we should have what we need"* + *"we want to avoid the fallback / sp_getapplock option"*. The SQL fallback (3) stays in scope as the IS §4a slip-trigger safety net only, never as the positive choice.
- **POC evidence** (§5 below) confirms the Safety Property holds under a deliberately-forced duplicate-handler-invocation: the idempotent DB write path absorbs the duplicate as a no-op, leaving the terminal state correct. Option (2)'s fencing tokens would be additive hardening and are deferred — re-open only if production experience shows a case idempotency doesn't cover.

**Resource-key → partition mapping (binding for S-005b):** Kafka's built-in partitioner = `MurmurHash2` of the key bytes, modulo partition count. This gives deterministic routing per key. The lock-topic partition count is fixed at **12** (matching HLPS §5.3 default). The same resource-key string that's passed to today's `IDistributedLockService.TryAcquireLockAsync` becomes the Kafka message key at produce time; partition ownership confers the lock.

## 3. Options Considered (Score Grid)

Each option scored against the SPEC §3 decision criteria.

| Criterion | Weight | (1) Kafka + idempotency | (2) Kafka + fencing tokens | (3) SQL `sp_getapplock` |
|---|---|---|---|---|
| §5.1 Safety Property | MUST | ✅ PASS (POC demonstrated) | ✅ PASS (stricter; token check at DB) | ✅ PASS (session-scoped lock has no two-leader window) |
| API parity with `IDistributedLockService` | HIGH | ⚠️ Needs per-partition lock-loss signal mapping from cooperative-revoke events; preserves arbitrary-string-key surface | ⚠️ As (1) | ✅ One-for-one: `sp_getapplock` is already arbitrary-string-key |
| Operational complexity | HIGH | ⚠️ One new moving part (Kafka consumer-group coordinator) but we own it already post-S-007 | ❌ Two new moving parts: consumer group + fencing-token bookkeeping | ✅ Zero new moving parts (SQL Server already in the stack) |
| Failover latency (SC-2a: ≤60 s) | MEDIUM | ✅ Session timeout 10 s + cooperative rebalance = typically <15 s | ✅ As (1) | ✅ `sp_getapplock` timeout-based; comparable |
| HLPS R-1 / R-2 risk surface | HIGH | ⚠️ Cooperative two-leader window is real but idempotency covers it (POC-proven) | ✅ Fencing eliminates the window | ✅ Not applicable (no Kafka for election) |
| Implementation effort for S-005b | MEDIUM | ⚠️ Moderate: partition-ownership→lock adapter, handler idempotency refactor where needed | ❌ High: (1) + DB schema change for tokens + write-path token plumbing | ✅ Low: replace Rabbit-backed `IDistributedLockService` impl with a SQL one |
| Removal-from-Kafka-stack cost | LOW | ✅ Low: contained in one `IDistributedLockService` impl | ✅ As (1) plus drop token column | ✅ Low: SQL impl is its own class |
| **Aligns with "remove RabbitMQ" HLPS objective** | — | ✅ | ✅ | ⚠️ Kafka still used for events; Rabbit-lock code removed; but we add a new SQL dependency path |
| **User preference (2026-04-14)** | — | ✅ explicit | neutral | ❌ explicit "avoid" |

### Why not (2) — Kafka + fencing tokens

Option (2) is strictly stronger than (1) on safety — the two-leader rebalance window is eliminated, not absorbed. But it doubles the implementation surface for S-005b (fencing-token bookkeeping in addition to consumer-group plumbing) and introduces a DB schema change (a `FencingToken` column on `DeploymentRequest`, or a separate table) that cascades into every state-transition write-path. The POC (§5) shows idempotency already covers the window acceptably — production has no concurrent same-request writes outside this rebalance-window, and the idempotent handlers already coalesce duplicates into no-ops.

If production experience under (1) shows a specific failure mode that fencing would close, revisit — option (2) is an additive upgrade over (1), not a rewrite.

### Why not (3) — SQL Server `sp_getapplock`

Option (3) is the IS §4a slip-trigger default if (1) fails to converge by 2026-07-15. It is the easiest to implement and genuinely removes the RabbitMQ dependency, but:

- User directive explicitly avoids it.
- It re-introduces DB coupling for a concern the Kafka stack can already mediate — reduces architectural coherence.
- Leader-election over a SQL lock adds load to the DB that's independent of the event-flow DB usage; a DB outage becomes a leader-election outage even when events themselves are healthy.

Stays in scope as the fallback only.

## 4. Consequences

### Binding decisions carried into S-005b

1. **Lock mechanism:** each `Dorc.Monitor` instance joins a single consumer group on the lock topic (working name: `dorc.locks`). Partition ownership = lock ownership for any resource-key that hashes to that partition. The topic has 12 partitions and no messages are actually consumed for their payload — the topic exists to give Kafka partitions to hand out. Candidates periodically send heartbeats (tiny metadata records) to keep the group alive.
2. **Resource-key → partition mapping:** Kafka's built-in `MurmurHash2` partitioner on the resource-key string. No custom partitioner; no reshape of the `IDistributedLockService` surface.
3. **Handler idempotency:** all state-transition writes performed inside `Dorc.Monitor`'s deployment-engine must be idempotent — already largely true today (UPSERT-style writes against DeploymentRequest and DeploymentResult tables); S-005b audits and tightens as needed.
4. **Lock-loss signal preservation:** the existing `IDistributedLock.LockLostToken` / `IsValid` contract must be preserved. Partition-revocation callbacks from Confluent.Kafka's `SetPartitionsRevokedHandler` fire the `LockLostToken`, giving `DeploymentEngine` the same "stop what you're doing" signal it has today.
5. **Cooperative-sticky assignment strategy** (already S-002 default) applies. This means a rebalance moves only the partitions that need to move, not all partitions — fewer lock-loss signals and less deployment-engine churn than eager rebalance.
6. **Session timeout 10 s, heartbeat 3 s, max-poll-interval 60 s** (already S-002 defaults). These are the sizing knobs for the SC-2a ≤60 s failover budget; tune in S-005b if the HA suite flags a regression.

### API-parity gap (nonzero)

Today's `IDistributedLockService.TryAcquireLockAsync(string resourceKey, int leaseTimeMs, CancellationToken ct)` returns `IDistributedLock?` (or `null` on contention). Under (1), the consumer owning the partition for `resourceKey` has the lock unconditionally — there is no "try" in the same sense; the candidate's position in the consumer group **is** the lock. S-005b's implementation of `IDistributedLockService`:

- Blocks on `TryAcquireLockAsync` until the calling Monitor instance owns the target partition (or the CancellationToken fires). In practice the wait is zero when this Monitor already owns the partition (the common case) and ≤ rebalance-window otherwise.
- Returns an `IDistributedLock` whose `LockLostToken` fires on the next `PartitionsRevokedHandler` call for that partition.
- The `leaseTimeMs` parameter becomes advisory only (Kafka session-timeout is the real lease).

### Observability / diagnostics

`KafkaRebalanceHandlers` (shipped in S-002) already emits structured `partitions assigned / revoked / lost` logs matching §4.3 of S-002 spec. S-005b's lock-loss events are those logs projected onto the resource-key namespace via the partition-mapping — same emit surface, additional interpretation.

### Rollback

If production under (1) proves unstable, the S-009 `release/pre-kafka-cutover` tag lets us redeploy the Rabbit-lock build cleanly. Within the initiative's 14-day rollback window.

## 5. POC Evidence

Two scenarios executed against the local compose stack (Kafka 3.7.0 + Karapace 4.1.1).

### 5.1 `poc-r1` — rolling rebalance with 3 candidates

- 3 candidates (cand-1, cand-2, cand-3); 5 rebalances driven by start/kill/restart sequence.
- 80 events produced across 4 RequestIds × 20 versions each.
- Pre-commit delay 2000 ms per candidate to widen the commit window.

| Metric | Value | Expected |
|---|---|---|
| state.jsonl rows (accepted transitions) | 80 | 80 |
| handler invocations (total) | 80 | ≥ 80 |
| invocations — accepted | 80 | 80 |
| invocations — idempotent no-ops | 0 | ≥ 0 |
| duplicate (RequestId, version) rows | 0 | 0 |

**Interpretation:** Kafka's normal commit semantics + cooperative-sticky rebalance did not produce natural duplicates in this run. Kafka's offset-commit-before-release machinery is doing its job. But this alone doesn't prove idempotency works under duplicate invocation — that's the `poc-forced-r1` scenario's job.

Evidence: `pocs/s-005a-leader-election/evidence/poc-r1/`.

### 5.2 `poc-forced-r1` — deliberately-forced duplicate handler invocation

- One candidate (cand-A) started with an 8-second pre-commit delay.
- 6 events produced across 3 RequestIds × 2 versions.
- cand-A consumed all 6 and wrote state but was `SIGKILL`'d mid-delay, before any offset commit.
- cand-B took over after session-timeout rebalance, re-read all 6 uncommitted offsets.

| Metric | Value | Expected |
|---|---|---|
| state.jsonl rows (accepted transitions) | 6 | 6 |
| handler invocations (total) | 7 | ≥ 7 |
| invocations — accepted | 6 | 6 |
| invocations — idempotent no-ops | 1 | ≥ 1 |
| duplicate (RequestId, version) rows | 0 | 0 |

**Interpretation — Safety Property passes.**

- Every `(RequestId, version)` was applied **exactly once** to the state file (6 accepted, 0 duplicates).
- cand-B re-invoked the handler for at least one offset that cand-A had already processed (the 1 idempotent-noop), and idempotency absorbed it cleanly.
- The three-outcome contract from SPEC §4 #4 (i) is satisfied: handler invoked twice for at least one message, second invocation was a no-op, terminal state is identical to "handler invoked exactly once" at the DB level.

Evidence: `pocs/s-005a-leader-election/evidence/poc-forced-r1/`.

### 5.3 Failure-mode walkthrough

Operator view per mechanism class (for the S-010 runbook to expand):

| Failure | What the operator sees | Recovery |
|---|---|---|
| Dead Monitor (SIGKILL) | `partitions-lost` log on any other Monitor; partition reassigned after 10 s session timeout | Automatic |
| Broker unavailable | `Consumer error: BrokerNotAvailable` logs on every Monitor; partitions stay but no new commits | Automatic on broker recovery |
| Handler exception on the active leader | Handler logs the exception; DB write was partial; offset NOT committed; next poll replays the message | Idempotency absorbs the replay; operator investigates the exception post-hoc |
| DB outage | Handler fails; offset not committed; consumer retries indefinitely; deployment stalls (expected) | DB recovery unblocks the Monitor |

## 6. Accepted Risks

Carried forward into S-005b:

- Two-leader window possible for sub-second intervals during cooperative rebalance. POC-demonstrated idempotency absorbs it; if production reveals a case idempotency doesn't cover, revisit option (2).
- POC runs against the local compose stack and Aiven non-prod with `MurmurHash2` partitioning; we haven't proven behaviour under `librdkafka` version changes. Chr.Avro / Confluent.Kafka pins under S-002 AT-6 audit constrain this.
- `IDistributedLockService.TryAcquireLockAsync` becomes synchronous-waiting (blocks until partition ownership is held) rather than fail-fast-on-contention. Callers in `Dorc.Monitor` already tolerate short waits; verify in S-005b.

## 7. References

- SPEC-S-005a (this ADR's governing spec)
- HLPS Kafka Migration §5.1 Safety Property; R-1 (cooperative-not-fenced leadership); R-2 (rebalance thrash); U-11
- IS S-005 §4a slip-trigger (2026-07-15 → option 3 fallback)
- `pocs/s-005a-leader-election/run-scenario.sh` + `run-forced-duplicate.sh` — reproducible POC drivers
- `pocs/s-005a-leader-election/evidence/poc-r1/` + `evidence/poc-forced-r1/` — captured evidence
