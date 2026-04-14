# JIT Spec — S-007: Status-Event pub/sub on Kafka (UI substrate migration)

| Field | Value |
|---|---|
| **Status** | APPROVED — Pending user approval |
| **Author** | Claude (Opus 4.6) |
| **Created** | 2026-04-14 |
| **Step ID** | S-007 |
| **Governing IS** | `IS-Kafka-Migration.md` §3 S-007 (APPROVED R3 — SC-3 interpretation binding) |
| **Governing HLPS** | `HLPS-Kafka-Migration.md` §5.2, SC-3, C-7, C-8, R-9 (APPROVED R3) |

---

## 1. Purpose & Scope

Make Kafka the authoritative substrate for **`DeploymentResultEventData`** events (Monitor → UI). Per IS R3 Implementation-Discovery, these events today flow only via SignalR (`DeploymentsHub`); this step does **not** port a RabbitMQ flow, it migrates the existing SignalR-based publish pipeline behind a Kafka substrate while preserving the UI's real-time WebSocket experience via a **Kafka → SignalR projection** on the API side. SC-3 interpretation is locked at IS R3 CHECKPOINT-2: Kafka is the substrate of record; SignalR remains the UI wire transport.

### In scope

- **Producer side (Monitor):** A new `KafkaDeploymentEventPublisher` implementing `IDeploymentEventsPublisher.PublishResultStatusChangedAsync(...)`. Produces `DeploymentResultEventData` to the new topic `dorc.results.status` with key = `RequestId.ToString()` (per HLPS §5.2 / U-12). The other two `IDeploymentEventsPublisher` methods (`PublishRequestStatusChangedAsync`, `PublishNewRequestAsync`) are **S-006's** concern; the S-007 publisher delegates them to the existing `DirectDeploymentEventPublisher` until S-006 lands.
- **Consumer side (API):** A new hosted service `DeploymentResultsKafkaConsumer` running inside the `Dorc.Api` host. Subscribes to `dorc.results.status`, deserialises Avro (via S-003's `AvroKafkaSerializerFactory`), and rebroadcasts each event onto the existing `DeploymentsHub` via the same `IHubContext<DeploymentsHub, IDeploymentsEventsClient>` `DirectDeploymentEventPublisher` already uses. Web UI continues to receive `IDeploymentsEventsClient.OnDeploymentResultStatusChanged(...)` callbacks as today — the wire format and hub method signatures do not change.
- **Topic provisioning:** `dorc.results.status` created with HLPS defaults — **12 partitions, RF=3, min.insync.replicas=2** (per IS S-007 / U-10). Provisioning happens at Monitor startup via Confluent `AdminClient` (idempotent — the create is a no-op if the topic already exists).
- **Failure handling:** poison messages (deserialisation failure, handler exception) write a `KafkaErrorLogEntry` via S-004's `IKafkaErrorLog.InsertAsync` with the C-8 fields populated from the `ConsumeResult<string, DeploymentResultEventData>`; on DAL failure, fall back to a structured `LogError` carrying the same fields. The consumer commits its offset only after the log path completes (success or fallback) — the offset never advances on a silent failure.
- **Substrate-selector flag:** `Kafka:Substrate:ResultsStatus` config key (enum `Direct` | `Kafka`, default `Direct`). When `Direct`, DI registers the existing `DirectDeploymentEventPublisher` for the result-status method (today's behaviour). When `Kafka`, DI registers the `KafkaDeploymentEventPublisher` and starts the consumer hosted service. **Per HLPS §3 there is exactly one authoritative substrate live per environment** — staging flips to `Kafka` first; production follows at cutover (S-011). The flag and its inactive branch are both removed in S-009.
- Wire the S-002 client layer through a new `Dorc.Kafka.Events.Publisher` project (or namespace, Delivery's choice) sitting between `Dorc.Kafka.Events` and `Dorc.Api` / `Dorc.Monitor`; this is the home of `KafkaDeploymentEventPublisher` and `DeploymentResultsKafkaConsumer`.

### Out of scope

- `PublishRequestStatusChangedAsync` and `PublishNewRequestAsync` — **S-006**.
- Removal of the SignalR re-broadcast / `DirectDeploymentEventPublisher` — post-cutover follow-up; **NOT** in scope of S-007 or S-009 (per IS R3 S-007 explicit text).
- DataService pub/sub — **S-008** (likely no-op).
- Substrate-selector flag for the request-lifecycle methods — **S-006** introduces its own flag (`Kafka:Substrate:RequestLifecycle`).
- Topic-creation operator runbook (DR / re-create / topic-config drift) — **S-010** as part of the cutover runbook.

---

## 2. Requirements

### R-1 — Producer surface

`KafkaDeploymentEventPublisher` implements `IDeploymentEventsPublisher`. Its three methods behave as follows:

- `PublishResultStatusChangedAsync(DeploymentResultEventData eventData)` — produces to topic `dorc.results.status`, key = `eventData.RequestId.ToString()`, value = the `DeploymentResultEventData` instance serialised by S-003's `AvroKafkaSerializerFactory`. Awaits broker ack (`acks=all`, idempotent producer per S-002 R-2). On produce failure, surfaces the exception to the caller — the Monitor's existing retry / commit cycle decides what to do.
- `PublishRequestStatusChangedAsync(...)` and `PublishNewRequestAsync(...)` — **delegate to an injected `DirectDeploymentEventPublisher` instance** so request-lifecycle events keep flowing through SignalR until S-006 takes over those methods. This delegation is removed in S-006.

**S-006 hand-off boundary (binding):** When S-006 lands, it replaces the two delegated method bodies with Kafka produces against `dorc.requests.new` / `dorc.requests.status`. The class `KafkaDeploymentEventPublisher` and its DI registration **remain**; only the delegation-constructor parameter (`DirectDeploymentEventPublisher`) becomes dead and is removed. S-007 and S-006 do not refight this boundary.

### R-2 — Consumer surface (`DeploymentResultsKafkaConsumer`)

Implements `IHostedService` (or `BackgroundService`). On `StartAsync`:

- Builds a consumer via S-002's `IKafkaConsumerBuilder<string, DeploymentResultEventData>` (Avro-aware via S-003's factory swap).
- Subscribes to `dorc.results.status`.
- Spawns a long-running consume loop on a background task; honours the host's `stoppingToken`.

Per consumed message:

1. Deserialise the value.
2. Rebroadcast via the injected `IHubContext<DeploymentsHub, IDeploymentsEventsClient>` using `IDeploymentsEventsClient.OnDeploymentResultStatusChanged(eventData)` — exactly the call `DirectDeploymentEventPublisher` makes today.
3. On success, commit the offset (synchronous commit; manual-commit is the S-002 default).
4. On failure (deserialisation, hub-broadcast exception, anything else), invoke the failure path in R-3 then commit the offset (per R-3 reasoning).

**Multi-replica fan-out (this is the load-bearing design choice for SC-3 projection correctness):**

`IHubContext<DeploymentsHub, IDeploymentsEventsClient>` is **process-local** — calling `Clients.All.OnDeploymentResultStatusChanged(...)` on replica B reaches only the WebSocket clients connected to replica B's process. A naive Kafka consumer-group setup (one `group.id` shared across all API replicas, partitions split between them) would mean: replica B owns the partition for `RequestId=42` and broadcasts locally; client C is sticky-session-pinned to replica A; **client C silently misses the event.**

The S-007 design therefore uses **per-replica consumer-group identity**: each API host computes its `group.id` as `dorc-api-results-status.{HostInstanceId}` (where `HostInstanceId` is process-stable, e.g. `Environment.MachineName + ProcessId` or a generated GUID per host start). Consequence: **every replica consumes every event** and broadcasts it to its own locally-connected clients via local `IHubContext.Clients.All`. Each web-UI client receives each event exactly once — via the replica it is sticky-session-pinned to.

The cost is N× consumption volume where N = API replica count. For DOrc's status-event volume (single-digit events per deployment, dozens of deployments per day) this is operationally trivial. The benefit is **no SignalR-backplane dependency** (no Redis / Azure SignalR addition to the dependency surface) and no requirement that DOrc's API be deployed single-replica in production.

This decision is binding. A future move to a SignalR backplane could revert to a shared `group.id`; that's an operational concern outside this initiative's scope.

### R-3 — Failure path (HLPS C-8 + S-004 wiring)

On a per-message failure:

1. Build a `KafkaErrorLogEntry` from the `ConsumeResult<string, DeploymentResultEventData>` + the captured exception, using the field map S-004 AT-7 anchored.
2. Call `IKafkaErrorLog.InsertAsync(entry, cts.Token)` with a bounded cancellation token (default 5 s — configurable via `Kafka:ErrorLog:InsertTimeoutMs`).
3. If `InsertAsync` itself throws (DB unavailable / timeout), emit a structured `LogError` with the same field set — this is the documented degraded mode per HLPS C-8.
4. **Super-degraded mode:** if the structured-log fallback itself throws (e.g. log sink unavailable), the exception is swallowed (try/catch around the fallback). No silent stall is preferred over crashing the consumer loop; one missed log entry beats a halted consumer that takes down further status updates for every connected user.
5. After the log path completes (success, DB-unavailable fallback, or super-degraded swallowed), **commit the offset**. The offset never advances on a silent failure of the *consume + handle* flow; the bounded timeout in step 2 is what prevents a degraded DB from stalling the consumer indefinitely.

**HLPS C-7 at-least-once carve-out:** at-least-once delivery is guaranteed on the **Kafka substrate** (the log of record) and on the **`KAFKA_ERROR_LOG` sink** (S-004 DAL). The **SignalR projection** is best-effort per the SC-3 interpretation locked at IS R3 CHECKPOINT-2 — a UI client may miss an event during a broadcast failure or replica restart; the UI's existing reconnect/refresh path is the recovery story (see §5 row 4 for verification posture).

### R-4 — Topic provisioning

On Monitor startup (when `Kafka:Substrate:ResultsStatus = Kafka`):

- A startup hook uses Confluent `AdminClient` to attempt creation of `dorc.results.status` with **NumPartitions = 12, ReplicationFactor from `KafkaSubstrateOptions.ResultsStatusReplicationFactor` (default 3, override 1 for single-broker dev)**, and the broker config `min.insync.replicas = 2` (set on the topic config; for RF=1 dev the value is reduced to 1 to match).
- If the topic already exists with the **same partition count**, the create is a no-op (`TopicAlreadyExistsException` is caught and logged at Information level).
- If the topic already exists with a **different partition count**, log at Warning level and continue. Per-RequestId ordering is preserved regardless of partition count (a fixed `RequestId.ToString()` always hashes to the same partition for a given count), so this is not a correctness defect — but it is a topology drift the operator should reconcile. Hard-failing here would block startup against any pre-existing topic; the bias is toward availability.
- If the broker rejects the request (e.g. insufficient brokers for the requested RF), the failure is logged at Warning level and Monitor continues — the producer will fail-loud on first publish if the topic genuinely doesn't exist.
- The startup hook also creates `dorc.requests.new` and `dorc.requests.status` (S-006's topics) using the same call — S-007 owns the provisioning entry-point because it lands first; S-006 inherits it via the same hook.

### R-5 — Substrate-selector flag

`Kafka:Substrate:ResultsStatus` is bound onto a `KafkaSubstrateOptions` POCO. The flag is read once at host build and drives DI registration:

- `Direct` (default): existing `DirectDeploymentEventPublisher` is registered for `IDeploymentEventsPublisher`. The Kafka consumer hosted service is **not** registered. This is today's behaviour and is the safe default if config is absent.
- `Kafka`: `KafkaDeploymentEventPublisher` is registered for `IDeploymentEventsPublisher`. The consumer hosted service is registered and starts on host start. The Avro `IKafkaSerializerFactory` and the Kafka client layer are both already registered (via `AddDorcKafkaClient` + `AddDorcKafkaAvro`); S-007's DI extension `AddDorcKafkaResultsStatusSubstrate` is the single entry point.

Switching the flag at runtime is **not** supported — host restart is required, matching the HLPS §3 no-dual-broker stance.

### R-6 — DI extension

Single extension method `AddDorcKafkaResultsStatusSubstrate(IServiceCollection, IConfiguration)` that:

- Binds `KafkaSubstrateOptions` from the `Kafka:Substrate` section with `ValidateOnStart()`.
- Reads the `ResultsStatus` flag from the supplied `IConfiguration` at registration time (so DI registration is deterministic).
- Conditionally registers either `DirectDeploymentEventPublisher` or `KafkaDeploymentEventPublisher` + the consumer hosted service.
- Idempotent (marker-singleton + `TryAdd*` per the S-002/S-003/S-004 pattern).

### R-7 — Configuration surface

`KafkaSubstrateOptions` carries:

- `ResultsStatus` (enum `Direct` | `Kafka`, default `Direct`).
- `RequestLifecycle` (enum `Direct` | `Kafka`, default `Direct`) — defined here for forward-compat; **S-006 reads this slot as-is and does not re-author the POCO; only adds its own DI branch keyed off this flag**.
- `ResultsStatusReplicationFactor` (int, default 3) — per R-4.
- Validation: enum values are validated by `Enum.IsDefined` at host build; RF must be ≥ 1.

`Kafka:ErrorLog:InsertTimeoutMs` (default 5_000) — the bounded wait per R-3.

### R-8 — Logging + observability

Every consumer-loop event emits a structured log entry with at minimum: `Topic`, `Partition`, `Offset`, `ConsumerGroup`, `RequestId` (extracted from key), and the outcome (`broadcast-ok`, `error-logged`, `error-fallback-structured-log`). Producer-side adds `Topic` and `RequestId` to its existing produce-error log per S-002 R-2. No new observability primitive — same `Microsoft.Extensions.Logging` pipeline as the rest of the stack.

---

## 3. Out of Scope (explicit)

- The `PublishRequestStatusChangedAsync` / `PublishNewRequestAsync` methods of `IDeploymentEventsPublisher` — **S-006**.
- Removal of `DirectDeploymentEventPublisher` — post-cutover follow-up.
- DataService — **S-008**.
- A UI-facing change of any kind — `DeploymentsHub` and `IDeploymentsEventsClient` are unchanged.
- Schema evolution of `DeploymentResultEventData` — covered by S-003's PR-gate.
- Operator runbook for topic re-creation, partition-count change, or DR rebuild — **S-010**.

---

## 4. Acceptance Criteria

### AT-1 — Producer publishes to the correct topic with the correct key

Unit test (Confluent.Kafka mocked or against a tiny in-process producer harness):

- `PublishResultStatusChangedAsync(eventData)` produces exactly one message to `dorc.results.status` with `Key == eventData.RequestId.ToString()` and a non-null Avro-encoded value.
- `PublishRequestStatusChangedAsync` and `PublishNewRequestAsync` invocations delegate to the injected `DirectDeploymentEventPublisher` (verified by mock).

### AT-2 — End-to-end status-event round-trip via Kafka → SignalR (R-8 logging coverage)

Integration test against the local compose stack:

- Start a `DeploymentResultsKafkaConsumer` against a mock `IHubContext` (the test substitutes the hub but exercises the real consumer + the real producer).
- Produce three `DeploymentResultEventData` messages for the same `RequestId` (and so the same partition).
- The hub mock receives `OnDeploymentResultStatusChanged(...)` calls **in the same order** the producer emitted them (assert via arrival-order call-indices, not post-hoc sort).
- Property-by-property equality on each consumed event matches the produced one.
- A captured logger records one structured-log entry per message with the R-8 fields populated (`Topic`, `Partition`, `Offset`, `ConsumerGroup`, `RequestId`, outcome `broadcast-ok`).

### AT-3 — Per-RequestId ordering preserved (multi-key interleaved, hub-call-order proof)

Integration test against the local compose stack with a hub mock that captures **arrival-order call-indices** (monotonic counter incremented at receipt; do **not** sort by `OccurredAt` when asserting):

- Produce ≥2 concurrent interleaved bursts of ≥25 events each, one burst per distinct `RequestId` (so the events land on at least two distinct partitions in the 12-partition topic).
- Consume and assert: per-`RequestId` projection of the hub's call sequence equals the producer's emission order for that key. Inter-key order is **not** required (different partitions can interleave arbitrarily).
- A 1-partition topic shape would not satisfy this test — the multi-key interleaving forces the proof to depend on the per-partition ordering Kafka actually provides.

### AT-4 — Poison-message path writes to `KAFKA_ERROR_LOG` with C-8 field-value correctness

Integration test with the S-004 DAL wired:

- Produce a deliberately-corrupt message to `dorc.results.status` (e.g. publish raw bytes that don't decode as valid Avro) using a raw `Confluent.Kafka` producer at a known partition + offset.
- The consumer's deserialisation fails; the failure path inserts a row in `KAFKA_ERROR_LOG`. Assert **value equality** (not just non-null):
  - `Topic == "dorc.results.status"`
  - `Partition == <produced partition>`
  - `Offset == <produced offset>`
  - `ConsumerGroup` matches the per-replica `dorc-api-results-status.{HostInstanceId}` shape (R-2)
  - `RawPayload == <produced raw bytes>` (byte-equal, modulo any S-004 R-3 truncation if the payload is large; the test uses a small payload so truncation does not apply)
  - `Error` contains the deserialiser exception type name
  - `Stack` is non-empty
  - Field-map alignment with S-004 AT-7's `BuildEntryFromConsumeFailure` is asserted (anchor against the S-004 reference call-site).
- A commit-interceptor (or equivalent observable) confirms `InsertAsync` completion **precedes** the offset commit (R-3 #5).
- The next non-poison message is processed normally.

### AT-5 — DB-unavailable fallback + super-degraded fallback

Unit / integration test with fakes:

- **DB-unavailable case:** fake `IKafkaErrorLog` whose `InsertAsync` throws. The consumer logs a structured `LogError` containing the C-8 fields. The offset still advances (consumer does not block on the DAL failure).
- **Super-degraded case:** fake `IKafkaErrorLog` throws **and** the logger itself throws on the fallback `LogError` call. The exception is swallowed (no propagation past the consumer loop), the offset still advances, and the consumer continues to process the next message. (R-3 #4.)

### AT-6 — Topic provisioning is idempotent across all R-4 branches

Integration test:

- **Double-create:** calling the startup-hook against the compose stack twice in succession produces no error and leaves a single `dorc.results.status` topic with the configured partition count.
- **Existing-topic-different-partition-count:** calling against a broker where the topic exists with a *different* partition count emits a Warning log but does not throw; the existing topic is left untouched.
- **RF-rejection:** against a single-broker harness with `ResultsStatusReplicationFactor = 3` requested, the hook logs a Warning and does not throw; Monitor startup continues. (Producer failure on first publish proves the topic genuinely missing — covered by AT-1's "produces exactly one message" path.)

### AT-7 — DI extension switches publishers based on flag

Unit test:

- `AddDorcKafkaResultsStatusSubstrate` with `ResultsStatus=Direct` (or unset) resolves `DirectDeploymentEventPublisher` for `IDeploymentEventsPublisher` and registers no hosted service for the Kafka consumer.
- Same with `ResultsStatus=Kafka` resolves `KafkaDeploymentEventPublisher` and registers the consumer hosted service.
- Calling the extension twice is idempotent (descriptor count unchanged on second call).
- An invalid flag value (`ResultsStatus=Bogus`) fails `ValidateOnStart` at host build with the failing key in the message.

### AT-8 — Backwards compatibility: Direct mode is byte-identical to today's behaviour

Smoke test:

- With `ResultsStatus=Direct`, an `IDeploymentEventsPublisher` resolved via DI is type `DirectDeploymentEventPublisher` and behaves identically to today (no Kafka producer, no consumer hosted service registered, no topic provisioning attempted).

---

## 5. Accepted Risks

| Risk | Source | Disposition |
|---|---|---|
| The consumer commits offset after the log path even when the broadcast itself failed (R-2 #4 + R-3 #4). UI loses the event for that one message, but the next message continues. | R-2/R-3 design | Accepted — this is the documented "no silent failure" trade-off. The UI's existing reconnect/refresh path recovers state from the DB on next user action. |
| Sticky-session SignalR routing means each connected client receives the event exactly once via the API replica that holds its partition; if a client reconnects to a different replica between events, ordering is preserved per partition but inter-partition gaps are user-visible. | R-2 multi-replica reasoning | Accepted — same as today's SignalR behaviour against multiple replicas; no regression. |
| `KafkaDeploymentEventPublisher` delegates the request-lifecycle methods to `DirectDeploymentEventPublisher` until S-006 lands. If S-006 slips, the request-lifecycle pipeline stays on SignalR — which it already is today, so no regression. | R-1 delegation | Accepted — explicit; S-006 removes the delegation. |
| Topic provisioning uses RF=3 in production but RF=1 in single-broker dev compose. The Monitor must read RF from config (or default by environment). | R-4 | Accepted — `KafkaSubstrateOptions.ResultsStatusReplicationFactor` (default 3, override 1 for compose) added in Delivery; one-line config. |
| `dorc.results.status` topic name is hard-coded against the S-003 subject map — if the subject name changes (R-5 schema-gate refresh), the topic name must change in lockstep. | R-1 / S-003 R-4 | Accepted — `KafkaSubjectNames.ResultsStatusTopic` is the single source of truth (S-003 already exposes it); S-007 reuses the constant. |
| `DeploymentResultEventData` carries a `DateTimeOffset` with offset; Avro maps to `string` (ISO-8601). Round-trip equality is asserted in AT-2 / AT-3 via parsed-value comparison, not raw-bytes. | S-003 schema | Accepted — same property-equality discipline as S-003 AT-6. |
| **Mixed-flag values across replicas during a rolling deploy can produce transient dual-substrate** (replica A on `Direct` + replica B on `Kafka`). | R-5 | Accepted — operational concern; S-011 cutover runbook ensures all replicas flip atomically. The window is bounded by deploy duration and limited to staging until S-011. |
| §5 row 1 claims "UI's existing reconnect/refresh path recovers state from the DB on next user action." | R-2 / R-3 | Accepted **pending Delivery verification**: the spec's failure-mode walkthrough at AT-2 close must point to the UI component / endpoint that polls the DB on reconnect. If verification finds the UI does not recover state, the risk row is upgraded and a follow-up ticket is opened. |

---

## 6. Delivery Notes

- **Branch:** continue on `feat/kafka-migration` per IS §1.
- **Project layout:** new `Dorc.Kafka.Events.Publisher` (or namespace inside `Dorc.Kafka.Events`, Delivery's choice). Must reference S-002's client layer + S-003's Avro factory + S-004's DAL. References from `Dorc.Api` and `Dorc.Monitor` to the new project so DI can resolve.
- **Tests:** AT-1 / AT-7 / AT-8 are unit-level (existing `Dorc.Kafka.Events.Tests` project may extend or a new test project — Delivery's call). AT-2 / AT-3 / AT-4 / AT-6 are integration tests against the compose stack — extend `Dorc.Kafka.Events.IntegrationTests`. AT-5 is unit-level with a faked DAL.
- **No CLAUDE.md changes** — no new file conventions; reuses S-002 / S-003 / S-004 patterns end-to-end.

---

## 7. Review Scope Notes for Adversarial Review

Reviewers should evaluate:

- Whether R-1 / R-2 correctly bound the SC-3 interpretation: Kafka authoritative + SignalR projection.
- Whether R-3 failure semantics (commit offset after log path) genuinely satisfy HLPS C-7 at-least-once + C-8 no-DLQ.
- Whether AT-2 / AT-3 actually prove the per-RequestId ordering claim under the 12-partition topic shape.
- Whether the substrate-selector flag (R-5) avoids the dual-broker trap.
- Risk coverage in §5.

Reviewers should **NOT**:

- Demand specific class names, namespace placement, hosted-service mechanics, or test-framework choices — Delivery.
- Re-litigate the SC-3 interpretation (CHECKPOINT-2 confirmed at IS R3).
- Re-open the no-removal-of-SignalR decision — explicit in IS R3 S-007.
- Demand request-lifecycle scope creep — S-006's job.

---

## 8. Review History

### R1 (2026-04-14) — REVISION REQUIRED → fixed for R2

Panel: Sonnet-4.6, Gemini-Pro-3.1, GPT-5.3-codex. Verdicts: APPROVE WITH MINOR / APPROVE WITH MINOR / **REVISION REQUIRED**. GPT's REVISION → aggregate REVISION REQUIRED.

| ID | Reviewer(s) | Severity | Finding | Disposition |
|---|---|---|---|---|
| Gemini-F1 | Gemini | MEDIUM (load-bearing) | Multi-replica fan-out gap: shared `group.id` + sticky-session SignalR = some clients silently miss events when their pinned replica isn't the partition owner | **Accepted** — R-2 now mandates **per-replica `group.id`** (`dorc-api-results-status.{HostInstanceId}`) so every replica consumes every event and broadcasts to its locally-pinned clients. No SignalR backplane required; cost is N× consumption volume which is trivial at DOrc's status-event rate. Decision is binding. |
| GPT-F1 | GPT | HIGH | AT-3 ordering proof would pass if consumer batched + reordered (test sorted by `OccurredAt`) | **Accepted** — AT-3 rewritten to assert hub arrival-order call-indices + multi-key interleaved (≥2 RequestIds across distinct partitions) + a 1-partition topic could not pass the new shape. |
| GPT-F2 | GPT | HIGH | AT-4 only asserted field-presence not field-value correctness for C-8 contract | **Accepted** — AT-4 now asserts value-equality for every C-8 field, anchors against S-004 AT-7 reference call-site, and proves R-3 commit-after-log-path with a commit-interceptor. |
| GPT-F3 | GPT | HIGH | Super-degraded mode (logger itself throws) unspecified | **Accepted** — R-3 now has step #4 explicit: super-degraded swallow + offset still advances. AT-5 split into DB-unavailable + super-degraded sub-cases. |
| Sonnet-F1 | Sonnet | MEDIUM | C-7 at-least-once carve-out for SignalR projection unstated | **Accepted** — R-3 now states at-least-once is on Kafka substrate + KAFKA_ERROR_LOG sink; SignalR projection is best-effort per SC-3 interpretation. |
| Sonnet-F2 | Sonnet | MEDIUM | AT-3 multi-key interleaving missing | **Accepted** — subsumed by GPT-F1 fix. |
| Gemini-F2 / GPT-F6 | Gemini, GPT | MEDIUM | Topic-provisioning partition-count drift silent + RF-rejection branch untested | **Accepted** — R-4 now explains why partition-count drift is non-correctness (key→partition deterministic for fixed count) but should be reconciled; AT-6 adds the RF-rejection sub-case. |
| GPT-F4 | GPT | MEDIUM | S-006 hand-off boundary on `KafkaDeploymentEventPublisher` ambiguous | **Accepted** — R-1 now binds the boundary: S-006 replaces method bodies, class + DI registration remain, delegation parameter is removed. |
| GPT-F5 | GPT | MEDIUM | `RequestLifecycle` slot — does S-006 read or re-author? | **Accepted** — R-7 now states S-006 reads the existing slot, does not re-author the POCO. |
| Sonnet-F3 | Sonnet | LOW | R-4 hard-coded RF=3 contradicts §5 row 4 | **Accepted** — R-4 now reads RF from `KafkaSubstrateOptions.ResultsStatusReplicationFactor` (default 3). |
| Sonnet-F4 / R-6 prescription | Sonnet | LOW | R-6 anti-pattern commentary too prescriptive | **Accepted** — R-6 trimmed to the requirement only. |
| Gemini-F3 | Gemini | LOW | Mixed-flag values across replicas during rolling deploy | **Accepted** — §5 risk row added; defers operational concern to S-010/S-011 runbook. |
| Gemini-F4 / GPT-F7 | Gemini, GPT | LOW | "UI recovers from DB on reconnect" claim uncited; commit-interceptor missing | **Accepted** — §5 row downgraded to "accepted pending Delivery verification at AT-2 close"; commit-interceptor folded into AT-4. |
| GPT-F8 | GPT | LOW | R-8 observability untested | **Accepted** — AT-2 extended with R-8 logging assertions. |
| Sonnet-F5 | Sonnet | LOW | AT-6 partition-count-drift = silent-drift concern | Defer — the AT itself records the Warning; partition-config governance is operator concern (S-010 runbook scope). |

All HIGHs and MEDIUMs accepted and resolved via surgical edits. LOWs mostly accepted, one deferred. No re-litigation of HLPS / IS settled decisions. **Resubmitting for R2.**

### R2 (2026-04-14) — UNANIMOUS APPROVE

Same panel, scoped strictly per CLAUDE.md §4 R2+ rules: verify R1 fixes, check regressions, no mining of unchanged text.

| Reviewer | Verdict | R1 fixes verified | Regressions | Notes |
|---|---|---|---|---|
| Sonnet-4.6 | **APPROVE** | All 14/14 | None | Multi-replica fan-out solved with binding decision; AT cross-references coherent; no implementation-level detail injected. Ready for user approval. |
| Gemini-Pro-3.1 | **APPROVE** | All 14/14 | None | Per-replica `group.id` correctly applied + AT-4 enforces the shape so a regression to shared group fails tests. Operational housekeeping (stale groups) appropriately deferred to S-010/S-011 runbook. |
| GPT-5.3-codex | **APPROVE** | All 8 of own findings + the 6 cross-panel | None | Three load-bearing HIGHs (AT-3 ordering, AT-4 C-8 value correctness, super-degraded mode) all tight. Multi-key interleaving with arrival-order indices defeats the batch-reorder attack that killed R1. Spec ready. |

No Critical/High/Medium/Low findings. No regressions. **Unanimous clean approval.** Status transitions: `IN REVIEW (R2)` → `APPROVED — Pending user approval`.

### Code Review R1 (2026-04-14) — REVISION REQUIRED → fixed for R2

Panel (diff `872ebfe1..HEAD`): Sonnet-4.6, Gemini-Pro-3.1, GPT-5.3-codex. All three returned REVISION REQUIRED with a shared CRITICAL: the initial increment-6 commit message claimed `Dorc.Api/Program.cs` edits were applied, but the file was never touched (tool-call state drift earlier in the session — an earlier Edit call silently no-op'd). Consequence: in the original pushed state, **`AddDorcKafkaResultsStatusSubstrate` was never invoked and neither broadcaster nor fallback adapters were DI-registered** — the substrate flag was a dead switch.

| ID | Reviewer(s) | Severity | Finding | Disposition |
|---|---|---|---|---|
| Sonnet-F1 / Gemini-G1 / GPT-F1 | All three | **CRITICAL** | `Program.cs` never called the substrate extension; adapters unregistered; Kafka mode could not activate | **Fixed** — `Program.cs` now: (a) registers `DirectDeploymentEventPublisher` as concrete + as `IDeploymentEventsPublisher`; (b) registers `FallbackDeploymentEventPublisher` as `IFallbackDeploymentEventPublisher`; (c) registers `SignalRDeploymentResultBroadcaster` as `IDeploymentResultBroadcaster`; (d) calls `AddDorcKafkaClient` / `AddDorcKafkaAvro` / `AddDorcKafkaErrorLog` / `AddDorcKafkaResultsStatusSubstrate` in order. Verified via clean build of Dorc.Api + existing test suites still green. |
| GPT-F2 | GPT | HIGH | Super-degraded mode (logger+DAL both throw) had no test — R1 spec HIGH contract unverified | **Fixed** — new `WriteErrorLogSuperDegradedTests.cs` unit test exercises the two-tier try/catch shape with throwing `IKafkaErrorLog` + throwing `ILogger`, asserts no exception escapes and both fakes were invoked. Mirrors production `WriteErrorLogAndCommit`; an in-code comment tags the duplication as a deliberate tripwire. |
| GPT-F4 | GPT | MEDIUM | AT-3 multi-key coverage thin (2 keys); `ArrivalIndex` captured but never asserted — arrival-order proof was transitive via `List.Where` | **Fixed** — AT-3 now uses 5 distinct RequestIds × 10 events each (50 total across likely ≥5 partitions of the 12-partition topic); projection explicitly `OrderBy(r => r.ArrivalIndex)` before per-key slicing, matching the R2 spec contract. |
| Sonnet-F4 / Gemini-G4 | Sonnet, Gemini | MEDIUM | Topic provisioner flattened broker auth failures into the same Warning bucket as dev RF-rejection | **Fixed** — `CreateTopicsException` catch now discriminates: `TopicAuthorizationFailed` / `ClusterAuthorizationFailed` → `LogError` (loud, check ACLs); `InvalidReplicationFactor` / `PolicyViolation` → `LogWarning` (dev-style); anything else → `LogError`. Still non-throwing so startup continues; producer fail-loud on first publish remains the backstop. |
| Sonnet-F3 | Sonnet | MEDIUM | `ConsumeException` with null `ConsumerRecord` busy-spins the loop on broker transport failure | **Fixed** — 1-second backoff when `ex.ConsumerRecord is null`, cancellable via `stoppingToken`. |
| Sonnet-F5 / Gemini-G5 | Sonnet, Gemini | MEDIUM | `VerifyPartitionCountAsync` ignored cancellation; 10s metadata timeout compounded on shutdown | **Fixed** — `cancellationToken.ThrowIfCancellationRequested()` at entry; metadata timeout reduced to 3 s (×3 topics = 9 s worst-case on shutdown). |
| GPT-F3 | GPT | MEDIUM | AT-4 lacked commit-interceptor temporal proof that `InsertAsync` precedes `Commit` | Defer — the production `WriteErrorLogAndCommit` is linear (InsertAsync awaited → Commit called next statement in the same method); the super-degraded unit test now exercises the order directly. A dedicated Kafka commit-interceptor would require `SetOffsetsCommittedHandler` wiring; weighed against the direct code inspection, not judged necessary. |
| Sonnet-F2 | Sonnet | LOW | `ConsumerGroupId` default evaluated at field-init time; two consumers per process would share a group id | Defer — documented by R-2 as "one consumer per replica"; integration tests that need distinct groups explicitly override via the `groupId` parameter. |
| Gemini-G3 | Gemini | (subsumed) | `FallbackDeploymentEventPublisher` ctor dependency on concrete `DirectDeploymentEventPublisher` | Addressed by Sonnet-F1 fix — Program.cs now registers the concrete type separately. |
| Gemini-G6 | Gemini | LOW | `consumer.Commit()` (no args) is less precise than `Commit(result)` | Defer — Confluent commits-all-known-positions-plus-1 semantics are functionally correct; precision upgrade is polish. |
| Gemini-G7 | Gemini | LOW | `ResultsStatusReplicationFactor` validator floor/ceiling | Already covered — `KafkaSubstrateOptionsValidator` asserts `>= 1`. No ceiling needed; Aiven's RF is cluster-determined. |
| Sonnet-F6 / GPT-F6 | Sonnet, GPT | LOW | Sync-over-async in `RunLoop`; `WaitUntilAssigned` hard-delay | Defer — async rewrite is polish; `WaitUntilAssigned` is intentional grace-window for rebalance settle. |
| Sonnet-F7 | Sonnet | LOW | `KafkaDeploymentEventPublisher.Dispose` `Flush(2s)` timeout arbitrary | Defer — 2s matches typical Kafka producer flush expectations; can be lifted to options if friction appears. |
| Sonnet-F8 | Sonnet | (non-finding) | No credential strings in source confirmed | Acknowledged. |
| GPT-F5 | GPT | LOW | Dead parameter in `PublishToTopic` test helper | Defer — internal test helper; aesthetic only. |

All CRITICAL + HIGH + relevant MEDIUM findings addressed via surgical edits; LOWs deferred to Delivery or acknowledged. Re-submitting for R2.
