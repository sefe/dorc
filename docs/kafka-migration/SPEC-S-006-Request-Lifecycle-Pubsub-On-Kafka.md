# JIT Spec — S-006: Request-Lifecycle Pub/Sub on Kafka

| Field | Value |
|---|---|
| **Status** | APPROVED — Pending user approval |
| **Author** | Claude (Opus 4.6) |
| **Created** | 2026-04-14 |
| **Step ID** | S-006 |
| **Governing HLPS** | `HLPS-Kafka-Migration.md` §5.2, SC-3, C-7, U-12 |
| **Governing IS** | `IS-Kafka-Migration.md` §3 S-006 |
| **Related ADR** | `ADR-S-005-Leader-Election.md` (mutual-exclusion via Monitor's S-005b Kafka lock — applies to request **execution**, not event consumption) |

---

## 1. Purpose & Scope

Make Kafka the **authoritative** substrate for `DeploymentRequestEventData`
events (`PublishNewRequestAsync`, `PublishRequestStatusChangedAsync`) while
keeping SignalR as a UI fan-out transport. Introduce a Monitor-side Kafka
consumer that **accelerates** the existing DB-poll pickup loop — it does
**not** replace the poll. Per IS §3 S-006 the DB-poll path remains a
fallback through this step and is only removed in S-009.

The S-007 pattern is reused end-to-end: substrate selector flag,
per-replica consumer-group id (fan-out semantics), Avro-serialised events,
Karapace schema gate.

### In scope

- **Producer side (API).** Replace the current delegation bodies in
  `KafkaDeploymentEventPublisher.PublishNewRequestAsync` /
  `PublishRequestStatusChangedAsync` with real Kafka `ProduceAsync` calls
  against `dorc.requests.new` and `dorc.requests.status` respectively.
  When substrate = Kafka, the SignalR publisher becomes the fallback (wired
  via `IFallbackDeploymentEventPublisher`), so every publish is
  **dual-transport** (Kafka + SignalR fan-out for UI) but **single-authoritative**
  (Kafka).

- **Consumer side (Monitor).** New hosted service
  `DeploymentRequestsKafkaConsumer` that subscribes to both topics and
  fans work into the existing Monitor state machine:
  - `dorc.requests.new` event → request a poll-loop wake-up so
    `DeploymentRequestStateProcessor.ExecuteRequests` picks up the new
    Pending row on the next iteration without waiting for the full
    `iterationDelayMs` interval.
  - `dorc.requests.status` event → same wake-up so `CancelRequests`,
    `RestartRequests`, `AbandonRequests` paths observe the DB-persisted
    status change quickly. The consumer **does not** mutate DB state
    directly; the API still owns the authoritative write.

- **Substrate flag.** `Kafka:Substrate:RequestLifecycle = Direct | Kafka`
  (default `Direct`). Mirrors the S-007 flag pattern and the `DistributedLock`
  slot added by S-005b. Removed in S-009.

- **Topic provisioning.** Both `dorc.requests.new` and `dorc.requests.status`
  are already listed in `KafkaResultsStatusTopicProvisioner` (S-007) — no
  new provisioner needed; verify RF/partition-count correctness in an AT.

- **DI extension.** `AddDorcKafkaRequestLifecycleSubstrate` in
  `Dorc.Kafka.Events` mirroring S-007's `AddDorcKafkaResultsStatusSubstrate`
  — idempotent, reads substrate flag at registration time, no-op on
  `Direct`.

- **Schema gate.** PR-gate for `DeploymentRequestEventData` Avro schema
  BACKWARD compatibility, reusing the S-003 `AvroSchemaGate` check against
  Karapace. Subject: `dorc.requests.new-value` and `dorc.requests.status-value`.

- **Observability.** Structured log shape mirrors S-007's
  `DeploymentResultsKafkaConsumer` (`broadcast-ok` / `error-logged` /
  `error-fallback-structured-log`). Producer-side `publish-ok` /
  `publish-failed` mirrors S-007's publisher.

- **Failure-path routing.** Reuse S-004's `IKafkaErrorLog` DAL for poison
  messages and handler exceptions on the Monitor consumer. Same
  three-tier failure model S-007 uses: DAL → structured log → swallow.

### Out of scope

- **Removal of the DB-poll path** — explicit S-009 concern per IS.
- **Removal of SignalR** — the UI keeps subscribing to the SignalR hub
  throughout S-006; full SignalR removal is post-cutover follow-up.
- **API-side Monitor write-path changes** — the API still writes to DB and
  still emits on SignalR; S-006 only adds the Kafka emit and a read-side
  consumer. `RequestController` callers are unchanged.
- **Execution mutual-exclusion** — already solved by S-005b's Kafka
  distributed lock on env keys. S-006's consumer does not execute requests.
- **New AT for the S-007 consumer** — S-006 does not touch it.

---

## 2. Requirements

### R-1 — Kafka producer for Request-lifecycle events

Replace the current `_fallback`-delegating bodies of
`KafkaDeploymentEventPublisher.PublishNewRequestAsync` /
`PublishRequestStatusChangedAsync` with Avro-serialised Kafka
`ProduceAsync` calls mirroring the shape of
`PublishResultStatusChangedAsync` (key = `RequestId.ToString()`, value =
`DeploymentRequestEventData`, structured log on success / failure).

Two new injected `IProducer<string, DeploymentRequestEventData>` instances
(one per topic is fine; a single producer writing to both topics is also
fine — Delivery choice). Idempotence + `Acks=All` inherited from
S-002's `KafkaConnectionProvider`.

On producer failure the method must throw so the caller (API controller)
can log at ERROR and still complete the HTTP response — the DB write has
already happened; Kafka failure does not rollback the user-visible outcome.
The failure-as-thrown-exception path matches the S-007 contract.

**Write-ordering invariant (Sonnet-F1 / Gemini-G1 / GPT-F6):** The canonical
order per publish is (1) DB commit by the API controller [pre-existing,
upstream of this spec], (2) SignalR fan-out attempt, (3) Kafka emit
attempt. A crash after (1) but before (2) or (3) is recovered by the
retained DB-poll path (IS §3 S-006) — the DB row is authoritative; Kafka +
SignalR are projections. SignalR emit MUST be attempted regardless of
Kafka outcome, and Kafka failure MUST propagate as an exception **after**
the SignalR attempt completes (or throws). This is the single source of
the acceleration-layer framing.

### R-2 — `IFallbackDeploymentEventPublisher` wiring

When substrate = Kafka, `IDeploymentEventsPublisher` resolves to
`KafkaDeploymentEventPublisher` (already the case post-S-007) and its
injected `IFallbackDeploymentEventPublisher` is the existing
`FallbackDeploymentEventPublisher` (S-007) that wraps
`DirectDeploymentEventPublisher` (SignalR). This means **every** method on
the publisher interface is dual-transport:

- `PublishResultStatusChangedAsync`: Kafka-authoritative (S-007), with
  SignalR fan-out via the consumer projection.
- `PublishNewRequestAsync` / `PublishRequestStatusChangedAsync`: Kafka-authoritative
  (S-006), with the same publisher emitting to SignalR inline for UI
  continuity. Because the API controllers are the producers and they
  already run alongside SignalR clients, inline dual-publish is the
  simplest correctness-preserving shape.

The exact DI mechanism — whether the publisher holds a reference to
`IFallbackDeploymentEventPublisher` and awaits both in sequence, or whether
the fallback emits via a decorator — is a Delivery choice. The behavioural
requirement: every Request-lifecycle publish must complete both substrates
in the Kafka mode, Kafka failure must not suppress the SignalR emit, and
the ordering invariant from R-1 (SignalR attempted first, Kafka second)
applies to whichever mechanism Delivery picks.

**DI-coordination guard (GPT-F7):** The new S-006 DI extension MUST NOT
re-register `IDeploymentEventsPublisher` or `IFallbackDeploymentEventPublisher`
— both are owned by S-007's `AddDorcKafkaResultsStatusSubstrate` extension.
S-006's extension registers only the Request-lifecycle producers, the
`DeploymentRequestsKafkaConsumer`, and its topic-provisioner contribution
(if any; see §1 "Topic provisioning"). Delivery verifies this by running
S-007's AT-2 regression after wiring S-006.

### R-3 — Monitor-side `DeploymentRequestsKafkaConsumer`

New hosted service in `Dorc.Kafka.Events` subscribing to both
`dorc.requests.new` and `dorc.requests.status`. Consumer properties:

- **Consumer group id:** per-replica, `dorc.monitor.requests.{HostInstanceId}`
  — same pattern as S-007's per-replica group (fan-out). Every Monitor
  replica sees every event. Mutual-exclusion of execution is S-005b's
  job, not the consumer's.
- **AutoOffsetReset:** `Earliest`. New requests must not be missed across
  a consumer restart window; the DB-poll fallback would catch up on
  restart, but using Earliest narrows the visibility gap.
- **AutoCommit:** true. Correctness does not depend on commit-after-handle
  because the handler is idempotent (wake-up signal, not a state mutation)
  — duplicate deliveries are harmless.
- **Rebalance handlers:** reuse S-002 `KafkaRebalanceHandlers`.
- **Avro deserialiser:** S-003 `AvroKafkaSerializerFactory`.
- **Handler shape:** for each record, invoke a small `IRequestEventHandler`
  service that:
  1. For new-request events, marks the Monitor state processor "poll-now"
     signal (see R-4).
  2. For status-changed events, same signal — the DB-poll loop will
     observe the persisted status transition and dispatch to existing
     `CancelRequests` / `RestartRequests` / `AbandonRequests`.
  3. Logs a structured `event-consumed` line with `topic`, `partition`,
     `offset`, `group`, `requestId`, `status`.
- **Failure routing:** poison messages (deserialise failure) and handler
  exceptions write a `KafkaErrorLogEntry` via S-004's `IKafkaErrorLog`;
  if the DAL itself throws, fall back to structured `LogError`;
  super-degraded case swallowed so the consumer loop never crashes —
  identical three-tier model to S-007's `DeploymentResultsKafkaConsumer`.

**S-005b env-lock invariant (Gemini-G4):** Wake-up signals never bypass
S-005b's env-lock. The consumer's sole effect is to shorten the poll
loop's sleep; the post-wake iteration contends for the env-lock
identically to a timer-triggered iteration. Two replicas receiving
wake-ups for the same RequestId both proceed only through the
S-005b-guarded path, so the mutual-exclusion property holds.

**Rebalance-replay invariant (GPT-F5):** Under `AutoCommit=true` +
`AutoOffsetReset=Earliest`, a rebalance may replay already-handled
records. This is harmless because (i) R-4's wake-up primitive collapses
duplicate signals, and (ii) the existing Monitor state-machine paths
(`CancelRequests` / `RestartRequests` / `AbandonRequests`) use
WHERE-on-source-status optimistic concurrency (see S-005b Handler
Monotonicity Audit) and are re-entrant on the same DB status, per
HLPS C-7.

**Topic-replay invariant (Gemini-G3):** A per-replica consumer group id
derived from `HostInstanceId` means every Monitor restart replays the
retained topic window. This is absorbed by the same two properties above
plus (iii) the poll loop inspects authoritative DB state and skips
non-Pending rows — terminal-state replays produce no work. Request-lifecycle
topics are configured with a bounded retention (Delivery picks the
value; recommended ≤7 days) so the replay cost is bounded.

**Schema-registry unavailability at startup (Gemini-G10):** Same
degraded behaviour as S-007's `DeploymentResultsKafkaConsumer` —
consumer startup fails-loud; the retained DB-poll fallback continues to
pick up requests. This matches the S-007 accepted-risk posture.

### R-4 — Poll-wake-up signal

Introduce a lightweight synchronisation primitive that the consumer raises
and `DeploymentEngine`'s poll loop observes, so the loop's `Task.Delay(iterationDelayMs)`
sleep short-circuits on signal. Implementation approach (Delivery choice
between equivalent options):

- A shared `SemaphoreSlim` released by the handler and `WaitAsync`-ed by
  the poll loop with a timeout equal to the iteration delay, **or**
- A shared `ManualResetEventSlim` / `AsyncManualResetEvent` reset per loop
  iteration.

Either way, the primitive must be:

- Singleton-scoped (one per Monitor process).
- Safe for many-signals-one-waiter semantics (duplicate signals collapse
  to a single wake).
- Invariant-preserving under broker outage: if the consumer is not
  running (substrate=Direct, or broker down), the signal is never raised
  and the poll loop falls back to the full `iterationDelayMs` — i.e.
  current behaviour is preserved exactly.

The poll loop's maximum wait must remain `iterationDelayMs` (no
long-poll). The signal only shortens the wait, never lengthens it.

**Signal-semantic pinning (GPT-F2):** The primitive MUST be latchable
across a "no waiter yet" window — i.e. a signal raised before the poll
loop begins its first `WaitAsync` must persist until observed, not be
lost. `SemaphoreSlim(initialCount: 0, maxCount: 1)` satisfies this;
`AsyncManualResetEvent` reset-per-iteration does not (unless the reset
happens only after the wait returns). Startup-race events therefore
never reach zero visibility — worst case is one baseline `iterationDelayMs`
wait after which the DB-poll catches up.

**Cancellation + disposal safety (Sonnet-F4 / Gemini-G2):** The
`WaitAsync` MUST observe the Monitor host's cancellation token so
shutdown is not delayed by up to `iterationDelayMs`. Signalling a
disposed or cancelled primitive MUST be a no-op — no throw, no
`LogError` — so the consumer loop never crashes when the Monitor host
is stopping but the Kafka consumer is still draining in-flight records.

### R-5 — Substrate selector + DI extension

New property `RequestLifecycle` already exists on `KafkaSubstrateOptions`
(from S-007); S-006 binds it to behaviour:

- `Direct` (default): no registration changes; SignalR-only path identical
  to today.
- `Kafka`:
  - Registers the two `IProducer<string, DeploymentRequestEventData>` singletons.
  - Replaces the `PublishNewRequestAsync` / `PublishRequestStatusChangedAsync`
    delegation in `KafkaDeploymentEventPublisher` with real Kafka emits
    (Delivery-phase: this may already be the registered concrete type —
    R-1 changes the method bodies, not the DI graph).
  - Registers `DeploymentRequestsKafkaConsumer` as a hosted service in
    Monitor's DI container (the Monitor `Program.cs` calls the extension
    in addition to S-007's `AddDorcKafkaResultsStatusSubstrate`).
  - Wires the `IRequestEventHandler` concrete implementation that raises
    the R-4 signal.

Idempotent via a marker-type guard (S-007 pattern).

The substrate flag must fail-fast at host build on an invalid enum string
with a key-qualified message — same contract S-007's
`ReadResultsStatusMode` enforces.

### R-6 — Schema gate

Extend the S-003 `AvroSchemaGate` check so the CI schema-compatibility
stage verifies `DeploymentRequestEventData`'s schema is BACKWARD-compatible
against the subjects `dorc.requests.new-value` and
`dorc.requests.status-value` on the non-prod Karapace, if the schema has
drifted vs the last-registered version. The S-003 gate already supports
multi-subject.

**Gate-state clarification (Gemini-G5):** As of this spec's drafting the
S-007 gate configuration does not include the Request-lifecycle subjects
— the Request-lifecycle schemas are produced by S-003 but have no active
consumer until S-006 lands. S-006's Delivery phase adds the two subjects
to the gate check-set (additive; no CI conflict). If Delivery discovers
they are already present (because a later change added them), the R-6
work becomes a no-op verification — Delivery records either outcome in
the commit.

**Schema-resolution observability (GPT-F4):** On consumer startup and on
first deserialise per subject, log an INFO line with the resolved
subject name, schema ID, and schema version so a "stuck consumer due to
schema mismatch" incident is triageable from logs alone. Matches the
S-007 / S-003 `AvroKafkaSerializerFactory` precedent.

### R-7 — Observability

- **Producer:** `publish-ok topic={Topic} partition={Partition} offset={Offset} requestId={RequestId} status={Status}` on success;
  `publish-failed topic={Topic} requestId={RequestId} error={Error}` on
  exception (ERROR level; caller still throws).
- **Consumer:** `request-event-consumed topic={Topic} partition={Partition} offset={Offset} group={GroupId} requestId={RequestId} status={Status}` on success;
  S-004 error-log entry on failure; same `error-fallback-structured-log`
  shape for DAL-unavailable as S-007.
- **Wake-up primitive:** `monitor-poll-signalled requestId={RequestId} kind={new|status}`
  at DEBUG. The poll-loop waker logs at TRACE so it doesn't dominate steady-state.

### R-8 — Failure-path + error-log routing

- Deserialise failure on either topic: S-004 `IKafkaErrorLog.InsertAsync`
  with raw payload bytes.
- Handler exception (e.g. wake-up primitive disposed race): same.
- DAL failure inside the handler path: structured `LogError`; loop continues.

**Commit-semantic clarification (Sonnet-F2):** Under `AutoCommit=true`
(R-3) the consumer advances past a poison record because the handler
**does not throw** after the error-log write — the exception is caught,
logged, and swallowed. The next `Consume()` call progresses the offset
and the librdkafka auto-commit timer persists it. Handler-driven commit
is not used. The invariant "poison records do not re-consume forever" is
realised by the no-throw-after-log path, not by an explicit `Commit()`
call.

### R-9 — Rollback posture (GPT-F3)

Kafka→Direct rollback is supported but is **not** a runtime flag flip:
the substrate mode is read at DI-registration (R-5), so changing the
flag requires a Monitor + API restart. Rollback SLA is therefore "restart
all Monitor + API replicas" — the same SLA S-007 inherits. During the
restart window, the retained DB-poll path continues to function
(substrate=Direct restores the SignalR-only shape; substrate=Kafka with
consumers-down still has DB-poll). No data loss in either direction:
the DB is the authoritative store (see R-1 ordering invariant).

**Asymmetric-flip posture (Gemini-G6):** Rolling the flag to Monitor
before API (or vice versa) yields either "Kafka produces, no consumer" or
"consumer subscribed, no producer." The former is absorbed by topic
retention + `AutoOffsetReset=Earliest`; the latter by the retained DB-poll.
Operator runbook for the rolling-deploy order is an S-010 concern.

---

## 3. Accepted Risks

| Risk | Disposition |
|---|---|
| Kafka failure during producer publish causes API HTTP to log ERROR but the user still sees a 200 (DB write succeeded, SignalR emit may have succeeded). | Accepted — matches S-007 contract; C-9 rollback tag covers recovery. |
| Duplicate wake-ups on the poll signal are wasted CPU. | Accepted — `iterationDelayMs` is already milliseconds of sleep; wake-collapsing via ManualResetEvent or Semaphore-cap-1 keeps the cost O(1) per redundant signal. |
| Consumer-group id per replica means every Monitor replica consumes every event, N× broker traffic. | Accepted — event volume is request-lifecycle, not per-step; N ≤ 3 in practice. Mirrors S-007. |
| DB-poll fallback is retained through S-006 → duplicated work during transition. | Accepted per IS §3 S-006; removal is S-009 concern. |
| `AutoCommit=true` on an idempotent handler risks lost visibility on a crash mid-handle — a rebalanced replica might wake up later than ideal. | Accepted — DB-poll fallback compensates; the Kafka consumer is an acceleration layer only (R-3). |
| Wake-up primitive is novel code inside Monitor's host process; thread-safety must be right first time. | Accepted — Delivery-phase tests explicitly exercise signal-collapse, cancellation, and no-consumer fallback (see AT-5). |
| Producer idempotence across substrate-flag flip (Direct→Kafka): an in-flight request at flip-time is not re-emitted to Kafka. | Accepted — the DB row still exists; next DB-poll cycle picks it up. |
| Cross-step schema drift: S-006 lands alongside S-007 which already registered Request-subject schemas via the gate. | Accepted — R-6 extends the gate set rather than redefining. |

---

## 4. Acceptance Criteria

### AT-1 — Producer emits to the correct topic with RequestId key

Unit/integration test: calling `KafkaDeploymentEventPublisher.PublishNewRequestAsync`
in Kafka mode produces a record on `dorc.requests.new` whose Key is
`RequestId.ToString()` and Value round-trips (Avro) to the same
`DeploymentRequestEventData`. Same for `PublishRequestStatusChangedAsync` →
`dorc.requests.status`.

### AT-2 — Publisher dual-publishes (Kafka + SignalR fallback)

Unit test with a fake `IFallbackDeploymentEventPublisher`: both methods
are invoked on the fallback when the publisher is in Kafka mode. Kafka
failure (producer throws) does not suppress the fallback call; the
caller still sees the Kafka exception propagate.

### AT-3 — Consumer subscribes to both request topics

Integration test against compose: `DeploymentRequestsKafkaConsumer`
subscribes to both topics and a produced record on either topic triggers
exactly one handler invocation per record.

### AT-4 — Handler raises the poll-wake-up signal

Unit test with a fake `IRequestEventHandler`: on receipt of a
`DeploymentRequestEventData` record, the handler's wake-up primitive
signal fires exactly once. Duplicate records collapse to at most one
pending signal.

### AT-5 — Poll-wake-up primitive short-circuits the loop delay

Unit test on the Monitor engine's sleep path: raising the signal causes
a pending `Task.Delay(iterationDelayMs)` to complete in ≤100 ms. When
the signal is never raised, the delay elapses to its full duration
(baseline `iterationDelayMs`).

### AT-6 — Substrate flag switches behaviour

Unit test on the DI extension: with
`Kafka:Substrate:RequestLifecycle = Direct` (or unset), the Kafka
producers and consumer are **not** registered and the existing
SignalR-only publisher is untouched. With `= Kafka`, the producers are
registered and `DeploymentRequestsKafkaConsumer` appears as a hosted
service.

### AT-7 — Invalid enum fails fast at host build

Unit test: `Kafka:Substrate:RequestLifecycle = Bogus` throws
`InvalidOperationException` with the key name in the message at the DI
extension call site — identical contract to S-007's `ReadResultsStatusMode`.

### AT-8 — Poison-message routes to S-004 error log

Integration test: produce a non-Avro or schema-incompatible bytes record
on `dorc.requests.new`; assert a `KafkaErrorLogEntry` row appears with
the topic, partition, offset, and `error` populated; assert the
consumer continues processing subsequent records.

### AT-9 — Schema gate catches an incompatible Request-event schema change

CI-level test: modify `DeploymentRequestEventData` to drop a field and
run the S-003 `AvroSchemaGate` check; assert it rejects the PR. Revert
and the gate passes.

### AT-10 — End-to-end: API publish → Monitor consumer wake-up

Integration test using a real Monitor shaped harness: API-side
`PublishNewRequestAsync` fires, the consumer receives the record, and
the poll loop's next iteration starts within a configurable
acceleration-window (default ≤1 s). Also asserts that with
substrate=Direct, the same test observes only the baseline
`iterationDelayMs` latency (no acceleration).

### AT-11 — Per-RequestId ordering preserved (Sonnet-F3 / GPT-F1)

Integration test: produce N (≥5) status-changed events for the same
RequestId in order S1→S2→…→Sn on `dorc.requests.status`. Assert the
consumer's handler observes them in the same order (key-based
partitioning collapses them onto one partition; per-partition FIFO
guarantees the order). This proves the IS §3 S-006 verification intent.

### AT-12 — Wake-up primitive survives startup race (GPT-F2)

Unit test: signal the primitive BEFORE any waiter has called `WaitAsync`.
Assert that the next `WaitAsync` completes immediately (signal was
latched across the no-waiter window). Also assert that a second
consecutive `WaitAsync` with no further signal blocks until timeout.

### AT-13 — Wake-up primitive safe under disposal / cancellation (Gemini-G2)

Unit test: dispose the primitive (or cancel its host token); assert that
a subsequent signal call is a no-op (no throw, no `LogError`). Assert
that a pending `WaitAsync` returns promptly on host-token cancellation.

### AT-14 — Handler-exception branch writes to error log (GPT R-7 gap)

Unit/integration test with a handler stub that throws a synthetic
exception; assert a `KafkaErrorLogEntry` is written via
`IKafkaErrorLog`, the log shape matches R-7
(`request-event-consumed` / `error-logged` variants), and the consumer
loop continues to the next record. Complements AT-8 (deserialise
failure) with the handler-throw path.

### AT-15 — Log shape assertions for R-7 (GPT-F10)

Extend AT-1 (producer) to assert a `publish-ok` log line with the named
fields on success and `publish-failed` on throw. Extend AT-3 (consumer)
to assert a `request-event-consumed` line per delivered record. Extend
AT-5 (wake-up) to assert `monitor-poll-signalled` is emitted per signal
reception.

### AT-16 — DB-poll fallback is untouched

Regression test: with substrate=Direct (the baseline, no consumer
registered), the existing DB-poll pickup path still processes new
Pending rows within `iterationDelayMs` — i.e. the Kafka consumer is
additive, not substitutive, as IS §3 S-006 demands. (The spec does not
define a "consumer-disabled" sub-toggle for substrate=Kafka; if
Delivery needs that signal for its test harness it injects a consumer
stub rather than adding a config flag.)

---

## 5. Out of Scope (explicit)

- Removal of `PendingRequestProcessor` / `DeploymentRequestStateProcessor`
  DB-poll paths — S-009.
- Removal of SignalR hub subscription by the web UI — post-cutover follow-up.
- Topic provisioning — already handled by S-007's provisioner (existing
  topics list includes both request topics).
- Partition-count change — 12 partitions per S-007 default; immutable
  per ADR-S-005 §4 Consequence #2 style rule.
- Changes to request execution ordering — S-005b's env-key partition
  ownership continues to serialise execution.
- Status-event substrate (`ResultsStatus`) — S-007 already owns it.

---

## 6. Delivery Notes

- **Branch:** continue on `feat/kafka-migration` per IS §1.
- **Files changed (indicative, not prescriptive):**
  - `src/Dorc.Kafka.Events/Publisher/KafkaDeploymentEventPublisher.cs` —
    real emits replace delegation bodies for the two Request-lifecycle
    methods.
  - `src/Dorc.Kafka.Events/Publisher/DeploymentRequestsKafkaConsumer.cs` — new.
  - `src/Dorc.Kafka.Events/Publisher/IRequestEventHandler.cs` + concrete — new.
  - `src/Dorc.Kafka.Events/DependencyInjection/KafkaRequestLifecycleSubstrateServiceCollectionExtensions.cs` — new.
  - `src/Dorc.Monitor/` — small addition: a `RequestPollSignal` primitive
    + a tiny hook in the poll loop to `WaitAsync` on it with the
    existing iteration delay as timeout. Monitor's `Program.cs` calls
    the new DI extension.
  - `src/Dorc.Kafka.Events.Tests/`, `src/Dorc.Kafka.Events.IntegrationTests/`
    — AT-1..AT-16 coverage.
- **Sub-deliverables:** none (no audit-doc style artefact required;
  AT-11 covers the DB-poll-preserved invariant).
- **Substrate-flag flip:** post-merge, staging runs with `Kafka` mode
  while production stays `Direct` until the S-011 cutover window.

---

## 7. Review Scope Notes for Adversarial Review

Reviewers should evaluate:

- Whether R-1..R-8 + AT-1..AT-11 collectively implement the IS S-006
  scope.
- Whether the acceleration-layer framing (consumer-does-not-execute) is
  correctly preserved through the requirements — in particular that R-3
  and R-4 never introduce a second execution path.
- Failure-mode coverage: producer fail, consumer fail, broker down,
  schema drift, wake-up primitive misuse.
- Substrate-flag flip safety (fresh traffic vs in-flight requests at
  flip time).

Reviewers should **NOT**:

- Re-open IS §3 S-006's decision to retain the DB-poll fallback.
- Demand exact method signatures / field names / test-project names —
  this is a requirements doc.
- Demand removal of SignalR — deferred to S-009 and post-cutover.

---

## 8. Review History

### R1 (2026-04-14) — SPLIT: 1 APPROVE WITH MINOR + 2 REVISION REQUIRED

Panel: Sonnet-4.6 (APPROVE WITH MINOR), Gemini-Pro-3.1 (REVISION REQUIRED), GPT-5.3-codex (REVISION REQUIRED). Aggregate findings table:

| ID | Reviewer | Severity | Finding | Disposition (R2) |
|---|---|---|---|---|
| Sonnet-F1 / Gemini-G1 / GPT-F6 | all three | HIGH | R-1/R-2 dual-publish ordering invariant unstated; Kafka throw could suppress SignalR emit. | **Accepted** — R-1 now states: (1) DB commit, (2) SignalR attempted, (3) Kafka attempted; Kafka exception propagates AFTER SignalR attempt. R-2 reaffirms. |
| Sonnet-F3 / GPT-F1 | Sonnet, GPT | HIGH | No AT for IS-explicit per-RequestId ordering verification. | **Accepted** — AT-11 added (≥5 events for one RequestId, FIFO assertion). |
| Gemini-G2 / GPT-F2 | Gemini, GPT | HIGH | Wake-up primitive startup-race + disposal safety unspecified. | **Accepted** — R-4 pins signal-latch semantic (Semaphore-style, persists across no-waiter window) + cancellation/disposal no-op; AT-12, AT-13 added. |
| Gemini-G3 | Gemini | HIGH | Per-replica `HostInstanceId` group + `Earliest` reset replays full topic on every restart. | **Accepted** — R-3 topic-replay invariant: wake-collapse + DB-status guard + bounded retention recommendation absorb the cost. |
| GPT-F3 | GPT | HIGH | Rollback procedure silent. | **Accepted** — new R-9: substrate flag is DI-time bound; rollback SLA = restart Monitor+API replicas; DB-poll + topic retention cover both directions. Asymmetric-flip posture (Gemini-G6) folded in. |
| Sonnet-F2 | Sonnet | MEDIUM | R-3 AutoCommit=true vs R-8 "offset committed after log path" wording inconsistent. | **Accepted** — R-8 commit-semantic clarification: handler does not throw post-log; auto-commit timer advances; explicit "no handler-driven commit". |
| Gemini-G4 | Gemini | MEDIUM | S-005b env-lock invariant not stated under R-3. | **Accepted** — R-3 paragraph: wake-up never bypasses S-005b lock; consumer's only effect is to shorten the poll-loop sleep. |
| Gemini-G5 | Gemini | MEDIUM | R-6 schema-gate ambiguous: additive vs already-covered by S-007. | **Accepted** — R-6 gate-state clarification: subjects not currently in gate set; S-006 adds them; no-op-fallback acknowledged. |
| GPT-F4 | GPT | MEDIUM | Schema-resolution observability missing. | **Accepted** — R-6: INFO log on consumer startup + first deserialise per subject (resolved subject/id/version). |
| GPT-F5 | GPT | MEDIUM | Rebalance-replay idempotence asserted but not proved. | **Accepted** — R-3 invariant cites HLPS C-7 + S-005b Handler Monotonicity Audit (existing optimistic-concurrency guards). AT-14 covers the handler-exception → error-log → loop-continues path. |
| GPT-F10 | GPT | MEDIUM | R-7 log shapes have no AT. | **Accepted** — AT-15 extends AT-1 / AT-3 / AT-5 with named-field log-shape assertions. |
| GPT-F7 | GPT | LOW | DI collision risk vs S-007 owning `IDeploymentEventsPublisher` / `IFallbackDeploymentEventPublisher`. | **Accepted** — R-2 explicit "MUST NOT re-register"; S-007 AT-2 regression cited. |
| Gemini-G6 | Gemini | LOW | Asymmetric flag-flip (Monitor-before-API or vice versa) unstated. | **Accepted** — R-9 asymmetric-flip posture paragraph. |
| Gemini-G10 | Gemini | LOW | Schema-registry-down at startup unaddressed. | **Accepted** — R-3 fails-loud + DB-poll-fallback paragraph (matches S-007 posture). |
| Sonnet-F4 | Sonnet | LOW | R-4 cancellation observation not stated. | **Accepted** — R-4 paragraph mandates host-token observation. |
| Sonnet-F5 / Gemini-G7 | Sonnet, Gemini | LOW | AT-3 "exactly one handler invocation" wording brittle under rebalance. | Defer to Delivery — AT-3 phrasing tightened in spirit by AT-14 (handler-exception path) and the rebalance-replay invariant in R-3; Delivery may further loosen the count assertion if needed. |
| Sonnet-F6 | Sonnet | LOW | R-5 dangling reference to S-007-already-added `RequestLifecycle` member. | Defer — fact is verifiable; if Delivery finds it absent it adds it (additive, no behavioural change). |
| Sonnet-F7 | Sonnet | LOW | Reverse flip (Kafka→Direct) accepted-risk row missing. | **Accepted** — folded into R-9. |
| Gemini-G8 / GPT-F9 | Gemini, GPT | LOW | AT-10 "≤1s" magic threshold. | Defer to Delivery — informative threshold; Delivery may rephrase as "materially less than `iterationDelayMs`" without re-review. |
| Gemini-G9 | Gemini | LOW | R-7 wake-collapse log diagnostics. | Defer to Delivery — observability nuance; not load-bearing. |
| GPT-F8 | GPT | LOW | AT-11 "consumer-disabled" sub-toggle not defined. | **Accepted** — AT-16 reworded to drop the off-spec toggle; substrate=Direct alone is the baseline. |

### R2 (2026-04-15) — UNANIMOUS APPROVE-LEVEL (2× APPROVE, 1× APPROVE WITH MINOR / LOW only)

Panel: Sonnet-4.6 (APPROVE), Gemini-Pro-3.1 (APPROVE), GPT-5.3-codex (APPROVE WITH MINOR — LOW editorial only).

Verified: every R1 HIGH/MEDIUM finding has an adequate, proportionate fix. No regressions on unchanged text. Two LOW editorial nits raised on R2-added text:

| ID | Severity | Finding | Disposition |
|---|---|---|---|
| GPT-R2-L1 | LOW | §6 Delivery Notes still said "AT-1..AT-11 coverage" after R2 added AT-12..AT-16. | **Accepted** — fixed in-place to "AT-1..AT-16". |
| GPT-R2-L2 | LOW | AT-14 covers handler-exception (R-7 gap) rather than the GPT-F5 rebalance-replay path the disposition table mapped it to. | Defer — rebalance-replay is covered by R-3 invariant + existing Monitor optimistic-concurrency tests; AT-14 stands as the handler-exception coverage. Mapping note clarified in this disposition row. |

Status transitions: `IN REVIEW (R1)` → `IN REVIEW (R2)` → `APPROVED — Pending user approval`.
