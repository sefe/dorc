# ADR-S-005 — Leader Election Mechanism

| Field | Value |
|---|---|
| **Status** | APPROVED (user-approved 2026-04-14; R2 unanimous adversarial panel) |
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

1. **Lock mechanism:** each `Dorc.Monitor` instance joins a single consumer group on the lock topic (working name: `dorc.locks`). Partition ownership = lock ownership for any resource-key that hashes to that partition. The topic has 12 partitions and no messages are actually consumed for their payload — the topic exists to give Kafka partitions to hand out. **Group liveness is maintained by the Kafka consumer-group `Heartbeat` protocol frames the client library sends to the group coordinator every `HeartbeatIntervalMs` (3 s by default) — no user records are produced or consumed to keep the group alive.** An empty lock-topic is fine.
2. **Resource-key → partition mapping:** Kafka's built-in `MurmurHash2` partitioner on the resource-key string. No custom partitioner; no reshape of the `IDistributedLockService` surface. Partition count is **immutable** post-cutover without a coordinated full-Monitor-fleet restart — changing the count remaps every key and creates a transient two-leaders-for-same-key window across the fleet. S-010 runbook must document this; operational partition-count changes are not a supported mid-flight operation.
3. **Handler idempotency — must be *monotonic*, not just UPSERT:** all state-transition writes performed inside `Dorc.Monitor`'s deployment-engine must be either (a) pure functions of the input message (idempotent-by-equality), or (b) **monotonic against current DB state** via a guarded write (e.g. `WHERE CurrentState IN (allowed-predecessors)` or a monotonically-increasing version column). A plain UPSERT is **not** sufficient on its own because it does not protect against a stale-leader's in-flight write landing after a newer leader has already advanced state. S-005b must audit every `IDeploymentEventsPublisher` / `DeploymentEngine` handler path and verify one of those two shapes holds; handlers that are neither require a guard-column addition before S-005b can land. The POC (§5) demonstrates (a) on a `(RequestId, version)` append-only sink; production handlers need (a) or (b).
4. **Lock-loss signal preservation:** the existing `IDistributedLock.LockLostToken` / `IsValid` contract must be preserved. The `LockLostToken` must fire on **both** lock-loss shapes:
   - Cooperative-rebalance revoke → `SetPartitionsRevokedHandler`.
   - Session-timeout partition loss (consumer lost contact with group coordinator long enough that ownership was revoked on the broker side) → `SetPartitionsLostHandler`.
   `SetPartitionsAssignedHandler` conversely resets the `LockLostToken` scope for newly-assigned partitions. The POC candidate code wires all three correctly; S-005b preserves that shape.

**Caller-survey required before S-005b lands:** today's `TryAcquireLockAsync` returns `null` on contention (fail-fast). Under option (1) the method blocks until partition ownership is held (or the CancellationToken fires). S-005b must enumerate every caller of `IDistributedLockService.TryAcquireLockAsync` in `Dorc.Monitor` and confirm none depend on the fail-fast-null semantic — any caller that does is adjusted or gets a bounded-wait overload.
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

**Interpretation — Safety Property passes (mechanism-proof; see caveats).**

- Every `(RequestId, version)` was applied **exactly once** to the POC state file (6 accepted, 0 duplicate rows for any `(RequestId, version)` pair).
- cand-B re-invoked the handler for at least one offset that cand-A had already processed (the 1 idempotent-noop), and idempotency absorbed it cleanly at the POC's append-only sink.
- The three-outcome contract from SPEC §4 #4 (i) is satisfied: handler invoked twice for at least one message, second invocation was a no-op, terminal state unchanged vs a single invocation.

**Scope caveat:** this POC evidence is a **mechanism proof** — it shows the idempotency path *works* once, not a *distribution* across duplicate shapes. Specifically:

- Only **1** forced-duplicate was observed (cand-A committed offset 0 before the kill, so only offset 1 was replayed). Meets the spec's "at least one" bar literally, not richly.
- The POC's "DB" is a JSONL append with in-candidate dedup logic. Production handlers write to SQL Server with state-machine semantics; the proof here is of the Kafka mechanics + idempotent-sink contract, **not** of every production handler's monotonicity.
- IS SC-2c sets **≥20 rebalances** with zero duplicate state-transitions as the production-acceptance bar. The POC tops out at ~5 rebalances. The ≥20 bar is carried forward to **S-005b's HA test suite** as the long-lived regression guard.

These are accepted-risk items covered in §6 below.

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

- **Two-leader window** possible for sub-second intervals during cooperative rebalance. POC-demonstrated idempotency absorbs it; if production reveals a case idempotency doesn't cover, revisit option (2). Handler monotonicity audit (§4 Consequence #3) is the first line of defence.
- **Stale-leader in-flight writes** landing after a newer leader advanced state. Fencing tokens (option 2) are the canonical solution; option (1) relies on monotonic-handler guards per §4 Consequence #3. If S-005b finds a handler that cannot be made monotonic without a schema change, pause and re-visit option (2).
- **POC evidence is n=1 mechanism-proof**, not a distribution test. S-005b's HA suite carries the ≥20 rebalance × zero-duplicate bar (IS SC-2c).
- **`MurmurHash2` false sharing:** different resource-keys can hash to the same partition, creating head-of-line blocking between unrelated resources when a slow handler pins a partition. This is not a safety violation (partition ordering still holds) but can affect SC-2a/SC-2b latency budgets. 12 partitions bounds the worst-case fan-in; can be raised in S-005b if observability shows contention.
- **Partition-count immutability post-cutover:** changing the lock-topic partition count remaps every resource-key and creates a two-leaders-same-key window until the full fleet restarts. S-010 runbook must document that partition-count changes are a fleet-coordinated operation, not a rolling change.
- **POC runs against the local compose stack and Aiven non-prod** with `MurmurHash2` partitioning; we haven't proven behaviour under `librdkafka` version changes. Chr.Avro / Confluent.Kafka pins under S-002 AT-6 audit constrain this.
- **`IDistributedLockService.TryAcquireLockAsync` becomes synchronous-waiting** (blocks until partition ownership is held) rather than fail-fast-on-contention. Callers in `Dorc.Monitor` already tolerate short waits for the Rabbit impl, but S-005b must **enumerate every caller and confirm none depend on the fail-fast-null semantic** (§4 Caller-survey-required). Any caller that does is adjusted or given a bounded-wait overload.

## 7. Review History

### R1 (2026-04-14) — UNANIMOUS APPROVE WITH MINOR

Panel: Sonnet-4.6, Gemini-Pro-3.1, GPT-5.3-codex. Verdicts: APPROVE WITH MINOR × 3. One HIGH + several MEDIUM findings accepted and resolved inline:

| ID | Reviewer(s) | Severity | Finding | Disposition |
|---|---|---|---|---|
| Gemini-F1 | Gemini | **HIGH** | "Heartbeats (tiny metadata records)" factually wrong — Kafka consumer-group liveness is `Heartbeat` protocol-frame, not user messages | **Accepted** — §4 Consequence #1 rewritten to cite the `Heartbeat` RPC every `HeartbeatIntervalMs`; notes empty lock-topic is fine. |
| GPT-F1 | GPT | MEDIUM | §3 "Why not (2)" conflates duplicate-invocation with stale-leader-write; UPSERT alone doesn't protect against the latter | **Accepted** — §4 Consequence #3 now binds S-005b to verify every handler is pure-idempotent OR monotonic (WHERE-clause-guarded or version-column), and lifts a mirror risk into §6. |
| Gemini-F2 | Gemini | MEDIUM | §4 Consequence #4 only named `SetPartitionsRevokedHandler`; missed `SetPartitionsLostHandler` for session-timeout loss | **Accepted** — §4 #4 rewritten to name both handlers + `SetPartitionsAssignedHandler` as the token-reset point. Aligns with what the POC candidate code actually does. |
| Sonnet-F1 / GPT-F2 | Sonnet, GPT | MEDIUM | POC evidence scope: proves mechanism at a JSONL sink with n=1 forced duplicate, not a distribution; ADR §5.2 read as if DB-level proof | **Accepted** — §5.2 now carries an explicit "Scope caveat" paragraph delineating mechanism-proof from distribution-proof; carries the ≥20 rebalance bar forward to S-005b HA suite. |
| Sonnet-F2 | Sonnet | MEDIUM | `TryAcquireLockAsync` blocking-vs-fail-fast caller survey unbound | **Accepted** — §4 Caller-survey-required block added; mirrored in §6. |
| GPT-F3 | GPT | MEDIUM | Partition-count-change two-leaders-same-key window not enumerated | **Accepted** — §4 Consequence #2 now names partition-count immutability as binding + cross-references §6 and S-010 runbook. |
| Gemini-F3 | Gemini | MEDIUM | `MurmurHash2` false-sharing head-of-line blocking between distinct resource-keys on the same partition | **Accepted** — §6 risk row added; notes 12 partitions bounds worst-case fan-in, raisable in S-005b. |
| Sonnet-F3 | Sonnet | LOW | `poc-r1` 0 idempotent-noops row adds no evidentiary value | Defer — narrative text is explicit; row kept for completeness. |
| Sonnet-F4 | Sonnet | LOW | Score-grid doesn't tie-break (2) wins on safety | Defer — §3.1 narrative already covers. |
| Sonnet-F5 | Sonnet | LOW | 14-day rollback window uncited | Defer — IS / HLPS citation is a polish item. |
| GPT-F4 / GPT-F5 | GPT | LOW | `TryAcquireLockAsync` already covered; option (3) fallback path complete | Non-findings — acknowledged. |
| Gemini-F4 / F5 / F6 | Gemini | LOW / non-finding | POC n=1, `poc-r1` no-op reading, three-outcome reconcile | Non-findings — acknowledged. |

Status after R1 fixes: `PROPOSED` → `APPROVED — Pending user approval` once verified in R2.

### R2 (2026-04-14) — UNANIMOUS APPROVE

Same panel. Verdicts: APPROVE / APPROVE / APPROVE. No CRITICAL/HIGH/MEDIUM/LOW findings. No regressions on unchanged text.

| Reviewer | Verdict | R1 fixes verified | Notes |
|---|---|---|---|
| Sonnet-4.6 | **APPROVE** | Sonnet-F1, Sonnet-F2 both addressed via §5.2 Scope caveat + §4 caller-survey block + §6 mirrors | Surgical fixes; Delivery-level detail correctly deferred to S-005b. |
| Gemini-Pro-3.1 | **APPROVE** | Gemini-F1 (heartbeat factual), F2 (both lock-loss handlers), F3 (false-sharing risk row) all addressed | Rewritten §4 #1 is technically accurate (KIP-62 heartbeat thread); §4 #4 matches Confluent.Kafka three-handler API; §6 row correctly scopes false-sharing as latency not safety. |
| GPT-5.3-codex | **APPROVE** | GPT-F1 (monotonic handler binding), F2 (scope caveat), F3 (partition-count immutability) all addressed | §4 #3 converts the stale-leader-write concern into an enforceable S-005b audit obligation at the right abstraction. |

No regressions. No unchanged text mined. Status transitions: `PROPOSED` → `APPROVED — Pending user approval`.

## 8. References

- SPEC-S-005a (this ADR's governing spec)
- HLPS Kafka Migration §5.1 Safety Property; R-1 (cooperative-not-fenced leadership); R-2 (rebalance thrash); U-11
- IS S-005 §4a slip-trigger (2026-07-15 → option 3 fallback)
- `pocs/s-005a-leader-election/run-scenario.sh` + `run-forced-duplicate.sh` — reproducible POC drivers
- `pocs/s-005a-leader-election/evidence/poc-r1/` + `evidence/poc-forced-r1/` — captured evidence
