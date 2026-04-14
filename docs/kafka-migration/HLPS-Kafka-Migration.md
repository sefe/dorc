# HLPS — DOrc Kafka Migration

| Field | Value |
|---|---|
| **Status** | APPROVED — Pending user approval |
| **Author** | Claude (Opus 4.6) |
| **Created** | 2026-04-13 |
| **Target completion** | 2026-12-31 (hard deadline) |
| **Supersedes** | Monitor Robustness initiative S-002 (paused) |

---

## 1. Problem Statement

DOrc currently uses **RabbitMQ** as its messaging substrate for:
- Deployment Monitor HA coordination (distributed lock via `RabbitMqDistributedLock`).
- Request/status event pub/sub between API, Monitor, and Runner services.
- Data service pub/sub (`RabbitDataService`).

The RabbitMQ HA implementation has proven persistently unreliable (three documented failure modes captured in `docs/monitor-robustness/`; HA feature-switched **off** in production as of 2026-04-13). Continued investment in hardening RabbitMQ is no longer strategically justified.

Leadership has directed a migration to **Apache Kafka** as the messaging substrate, to be completed by end of calendar year 2026. The internal SEFE deployment will use **Aiven for Apache Kafka** as the managed platform; the open-source DOrc project must remain deployable against any standard Kafka cluster.

## 2. Goals

1. Replace all RabbitMQ usage in DOrc with Kafka for deployment HA and messaging.
2. Restore Monitor HA functionality using a Kafka-native leader-election mechanism.
3. Preserve DOrc's status as an open-source project — no proprietary/commercial-licensed dependencies.
4. Complete the **production cutover** to Kafka by **2026-12-31**.

## 3. Non-Goals

- No broader architectural change to DOrc (no rewrite of Monitor, Runner, or API contracts beyond what the substrate swap requires).
- No dual-broker operation — a single flag-day cutover is acceptable.
- No migration of in-flight deployment state across the cutover (see §6 Constraint C-2).
- No extension of messaging scope beyond current RabbitMQ usage (no new event streams, no audit bus, no deployment event replay store).

## 4. Constraints

| ID | Constraint |
|---|---|
| **C-1** | Open-source license compatibility. All new dependencies must be OSI-approved. `Confluent.Kafka` (Apache 2.0) is pre-approved for the .NET client. Avro serializer libraries must also be OSI-approved (Apache/MIT/BSD). IS must enumerate every new NuGet dependency with exact package + version + SPDX license and verify OSI compatibility before pinning (see R-7). |
| **C-2** | **Hard cutover.** In-flight deployments at cutover time will be terminated. No drain requirement. Cutover communication to deployment users is the ops team's responsibility. |
| **C-3** | **Aiven for Kafka** is the target managed platform for the internal deployment. The solution must run against Aiven's standard offering (including Karapace schema registry if used). |
| **C-4** | **Avro** is the message serialization format (supersedes current JSON payloads). Schema registry required. |
| **C-5** | Hard deadline: production cutover must complete by **2026-12-31**. |
| **C-6** | DOrc must remain deployable against any vanilla Kafka cluster (not Aiven-specific features). |
| **C-7** | **Delivery semantics: at-least-once** with **idempotent consumer handling** as a required design property. Applies to all flows. Exactly-once is explicitly not pursued. |
| **C-8** | **Poison-message handling: DB error-log table (no DLQ).** The target Kafka deployment does not support DLQ features. On a message-processing failure (deserialization error, business-logic exception, or repeated failure), DOrc MUST: (a) write a row to a dedicated DOrc DB error-log table capturing at minimum — topic, partition, offset, consumer group, message key, raw payload (or a reference to it), error message and stack trace, timestamp; (b) commit the offset; (c) continue processing the stream. Consumer groups must never be stalled by a single failing message. **If the error-log write itself fails** (DB unavailable, table locked, transient connectivity), the consumer MUST fall back to structured-log capture of the same fields and still commit the offset — DB unavailability must never stall the consumer group; IS defines the fallback log sink and operator alerting signal. **Retention/growth**: a purge or archival policy for the error-log table must be defined in IS (unbounded growth is not acceptable); raw payloads beyond a size cap may be truncated with a flag. Table schema, retry/replay tooling, and alerting thresholds deferred to IS. |
| **C-9** | **Rollback retention window: 14 days post-cutover.** RabbitMQ infrastructure and the last RabbitMQ-capable release tag must be retained in cold standby for 14 days. After this window, RabbitMQ infra may be decommissioned (per SC-6) and rollback is no longer available. |
| **C-10** | **Design reference (not a dependency): `Trading.Core.Messaging.Kafka` v4.2.0** (`C:\src\trading-core-messaging-kafka`) is an **internal, non-OSS** library. DOrc MUST NOT take a runtime dependency on it (would violate Goal 3). The library is a source of vetted design patterns and defaults, to be **reimplemented inline** in DOrc using snippets as needed. Conventions to inherit by reimplementation: Confluent.Kafka + Chr.Avro as direct dependencies, auto-registration of Avro schemas on producer init, subject-name aligned to topic, SaslSsl + SCRAM-SHA-256, Acks=All, manual commit, CooperativeSticky partition assignment, structured logging on partitions-assigned / revoked / lost callbacks, session=30s / heartbeat=10s / max-poll=5min defaults. The envelope (accept/reject) pattern from the library is a useful idea but **not mandatory** — DOrc's usage is tailored enough that a thinner abstraction (or none) may be preferable; IS to decide per call-site. |

## 5. Proposed Solution Shape

### 5.1 Distributed Lock / Monitor HA
**Approach: Kafka consumer-group single-partition leader election.**

- A dedicated topic (e.g. `dorc.monitor.leader`) with **1 partition**.
- All Monitor instances join a single consumer group.
- Kafka's group coordinator assigns the partition to exactly one consumer at any *stable* point in time → that instance is the active leader.
- Leader loss (crash, network partition, broker rebalance) triggers automatic reassignment → new leader elected within the group's `session.timeout.ms`.
- Replaces `RabbitMqDistributedLock` entirely. No external lock store (ZooKeeper, etcd, Redis) required.

**Safety property (critical):** Kafka consumer-group leadership under `CooperativeSticky` assignment is **cooperative**, not a fenced mutex. During a rebalance there is a brief window in which the outgoing owner has not yet processed the partition-revoked callback while the incoming owner has been notified of partition-assigned. The design MUST therefore assume the possibility of two-concurrent-leaders windows and protect against them. Acceptable strategies (IS to select): (a) Monitor actions are idempotent at the Request level such that duplicate execution is safe (current `CancelStaleRequests` logic post-S-004 is already idempotency-oriented — see `docs/monitor-robustness/`); (b) fencing tokens derived from generation/epoch IDs carried in DB writes; (c) both. If semantic equivalence with `LockLostToken`/fenced-lock contracts cannot be achieved via (a)/(b), the fallback is a DB-backed advisory lock in SQL Server (DOrc already depends on it) with Kafka used only for event flow.

### 5.2 Pub/Sub Messaging
- RabbitMQ queues and exchanges → Kafka topics. Default partition count **12** per topic (U-10); deviations only with justification in IS.
- Competing-consumer semantics (current Rabbit pattern) map naturally to Kafka consumer groups.
- Manual ack semantics map to manual offset commits.
- **Ordering:** producers MUST set the Kafka **message key = Deployment Request ID** on publish for Request-scoped flows. This guarantees all messages for a given Request land on the same partition and are consumed in order (Kafka's per-partition ordering guarantee). For flows without a natural Request ID — notably the single-partition leader-election topic (keying moot) and DataService events — IS must select and justify a keying strategy (entity ID, null-key round-robin, or explicit partitioner) against SC-3.

### 5.3 Serialization
- **Avro** via **Chr.Avro** (MIT) for schema generation from .NET types, with **Confluent Schema Registry** wire protocol (Karapace on Aiven is wire-compatible).
- Schemas auto-registered on producer init; subject name aligned to topic (pattern borrowed from the C-10 reference).
- Schemas version-controlled in-repo alongside contract definitions.
- No backwards-compatibility burden across the cutover (hard cutover = greenfield topics); compatibility mode for post-cutover evolution to be set per-subject in IS (default Schema Registry mode is `BACKWARD`).

### 5.4 Client Stack
- Direct dependencies only: **`Confluent.Kafka`** (Apache 2.0) and **`Chr.Avro`** (MIT). Both are OSI-approved.
- DOrc owns a **minimal internal layer** wrapping these packages — just enough to centralise config (auth, schema registry, rebalance callbacks, logging) and give call-sites a typed producer/consumer. Design snippets may be lifted from the C-10 reference, but no runtime dependency on it.
- The envelope `accept/reject` pattern from the C-10 reference is **optional**. Favour it where a call-site genuinely benefits from deferred-ack semantics; otherwise a simpler `ConsumeResult<T>` handler + explicit `Commit()` is acceptable. IS decides per flow.
- DI wiring onto `Microsoft.Extensions.DependencyInjection` is DOrc's responsibility.

### 5.5 Rebalance Observability
- DOrc MUST wire partition-assigned, partition-revoked, and partition-lost callbacks (via `ConsumerBuilder<TK,TV>.SetPartitionsAssignedHandler` etc.) to its existing structured-logging pipeline.
- Log shape mirrors the C-10 reference convention (partition list, total assignment count, remaining-after-revocation count) so SC-2c's "no duplicate execution" claim is auditable from logs alone.

## 6. Success Criteria

| ID | Criterion | Verification |
|---|---|---|
| **SC-1** | All RabbitMQ code paths and configuration removed from DOrc. | No `RabbitMQ.*`, `EasyNetQ`, `RabbitDataService`, `RabbitMqDistributedLock`, `amqp://`, `amqps://` references in source; no RabbitMQ NuGet package references in `.csproj` files; no Rabbit hostnames/queue names in `appsettings*.json`, Helm charts, or IaC. |
| **SC-2a** | Leader reassignment on failure. | Integration test: kill active leader; partition is reassigned to a standby instance within 60s (default `session.timeout.ms` ~45s + rebalance). |
| **SC-2b** | New deployments accepted by the new leader post-failover. | After reassignment, a newly-submitted deployment request is picked up and begins processing within a further 30s. In-flight deployments on the failed leader are **not** expected to resume (consistent with §3 Non-Goal on in-flight state). |
| **SC-2c** | No duplicate-execution of deployment side-effects during rebalance windows. | Induced rebalance test: across N ≥ 20 forced rebalances, no deployment Request is advanced through the same state transition twice. Verified via DB audit trail. |
| **SC-3** | All existing pub/sub flows (request lifecycle, status updates, data service events) function end-to-end on Kafka. | Full E2E deployment test suite passes against Kafka-backed environment. |
| **SC-4** | Message payloads are Avro-serialized with schema registry. | Payload capture confirms Avro binary encoding; schema registry contains registered subjects; schema-compatibility mode documented and enforced in PR gate. |
| **SC-5** | Solution runs against Aiven for Kafka (internal) and vanilla Kafka (OSS users). | CI validates against a vanilla Kafka + Karapace docker-compose; SEFE staging validates against Aiven. |
| **SC-6** | Production cutover completed by 2026-12-31; RabbitMQ infra decommissioned after the C-9 rollback window. | Deployment record; RabbitMQ infra retained in cold standby for 14 days post-cutover per C-9, then decommissioned. |
| **SC-7** | No regression in deployment reliability vs the pre-HA-disablement baseline. | Baseline source: DOrc deployment success-rate metric from the 30 days immediately prior to HA being disabled (2026-03-14 to 2026-04-13). Post-cutover measurement window: 30 days or until ≥ 200 production deployments have executed, whichever is later. If volume is insufficient at 30 days, window extends. Baseline value and query to be captured in IS. |
| **SC-8** | Cutover execution is controlled and reversible within its window. | Documented cutover runbook with: pre-cutover smoke tests, explicit go/no-go gates, max cutover duration (to be defined in IS, target ≤ 4h), explicit rollback trigger criteria, post-cutover smoke-test suite. Runbook dry-run executed against staging at least once before prod cutover. |

## 7. Unknowns Register

| ID | Description | Owner | Blocking | Status |
|---|---|---|---|---|
| **U-1** | Message serialization format | User | Yes | **RESOLVED** — Avro + schema registry |
| **U-2** | In-flight deployment handling at cutover | User | Yes | **RESOLVED** — hard cutover, terminate in-flight |
| **U-3** | .NET Kafka client library | User | Yes | **RESOLVED** — Confluent.Kafka |
| **U-4** | Authentication mechanism to Aiven Kafka | User | Yes | **RESOLVED** — SASL/SCRAM-SHA-256 over TLS |
| **U-5** | Retention and durability targets per topic | User | Yes | **RESOLVED** — 7-day retention across all topics; replication factor 3, min.insync.replicas 2 (assumed standard; flag if different) |
| **U-6** | Consumer session timeout / leader failover target | User | Yes | **RESOLVED** — Kafka defaults (session.timeout.ms ~45s, heartbeat.interval.ms ~3s); SC-2 threshold set to failover within 60s |
| **U-7** | Aiven Kafka cluster availability for integration testing | User | Yes | **RESOLVED** — cluster already provisioned; only outstanding infra task is creating SASL/SCRAM user + password for DOrc |
| **U-8** | Schema registry | User | No | **RESOLVED** — Aiven-hosted schema registry (Karapace-compatible); for OSS users/CI a local Karapace container will be provided via docker-compose (per R-5). |
| **U-9** | Observability expectations | User | No | **RESOLVED** — reuse DOrc's existing logging/tracing mechanism; log the same shape as the C-10 reference library's partition-assigned/revoked/lost + consumer-error callbacks. No separate metrics export mandated at HLPS level. |
| **U-10** | Topic naming convention and partition/replication defaults | User | No | **RESOLVED** — Topic names use a **placeholder/templated** form in code and config; the final SEFE naming standard is substituted at deploy time (env/IaC responsibility). Default partition count = **12**. Replication factor = 3, min.insync.replicas = 2 (per U-5). |
| **U-11** | `LockLostToken` semantic mapping under Kafka rebalances — see §5.1 Safety Property; now a named risk (R-1) rather than just an open question | Agent | No | OPEN — to be resolved in IS design step |
| **U-12** | Per-flow ordering requirements | User | No | **RESOLVED** — Ordering guaranteed per-partition by using the **Deployment Request ID as the Kafka message key** on publish. All messages for a given Request land on the same partition and are processed in order. Flows without a natural Request ID (if any) handled on a per-flow basis in IS. |
| **U-13** | Aiven plan/tier and feature restrictions | User | No | **RESOLVED** — Custom Aiven tier; no practical limits on partition count, ACL model, or quotas. |
| **U-14** | Credential management: rotation cadence, secret store, per-environment isolation for the SASL/SCRAM user | User | No | **RESOLVED** — SASL user/password has **no expiry** (Aiven/vendor-managed); TLS certificate expiry is handled by the vendor. IS covers per-environment isolation and secret-store placement only. |

**Gate:** All blocking unknowns resolved. HLPS is ready to transition to `IN REVIEW` for Adversarial Review.

## 8. Out of Scope (explicit)

- Kafka Streams / KSQL usage.
- Event sourcing or deployment history replay.
- Changing deployment orchestration logic.
- Multi-region / geo-replicated Kafka.
- Migration of in-flight state across cutover.

## 9. Risks

| ID | Risk | Mitigation |
|---|---|---|
| **R-1** | **Cooperative (non-fenced) leadership correctness.** Kafka consumer-group leadership admits a brief two-concurrent-leader window during rebalance. Without idempotency or fencing, this is a correctness defect for deployment orchestration, not merely a tuning concern. | Enforced via §5.1 Safety Property; SC-2c measures it directly. IS must select and verify an idempotency and/or fencing approach; fallback to SQL-Server-based advisory lock is documented. |
| **R-2** | **Rebalance thrash under broker instability.** Rebalance storms could cause repeated leader changes. | IS must include a deliberate design step analyzing rebalance behaviour, session/heartbeat tuning. C-9 rollback window retained as last resort. |
| **R-3** | **Hard cutover has no live-rollback.** Any latent bug is discovered in production with only a redeploy-prior-release option, bounded by C-9's 14-day window. The "no dual-broker" stance (§3) was chosen deliberately for simplicity, cost, and to avoid dual-write semantic compromises — this rationale is recorded here. | Pre-cutover staging validation against Aiven; cutover runbook gated by SC-8; RabbitMQ infra retained cold for 14 days per C-9. |
| **R-4** | **Deadline risk.** End-of-year deadline is aggressive given scope (substrate swap + HA redesign + serialization change). | IS must be ruthlessly prioritized; named descope levers include (a) DataService migration last in sequence, (b) keep JSON-on-Kafka as fallback if Avro schema governance slips, (c) ship DLQ as "skip + alert" initially with retry-topic deferred. Any scope creep rejected. |
| **R-5** | **OSS users without Aiven need a local dev story.** | U-8 / IS must include a docker-compose or equivalent for local Kafka + schema registry. |
| **R-6** | **Avro schema evolution discipline is new to the codebase.** | IS must include a schema-governance step (compatibility mode, registry layout, PR review gates). |
| **R-7** | **Confluent ecosystem license mix.** While `Confluent.Kafka` itself is Apache 2.0, parts of the wider Confluent SR Serdes ecosystem have historically carried the Confluent Community License (non-OSI). DOrc's direct-dependency stack (Confluent.Kafka + Chr.Avro) is OSI-clean, but the transitive closure must be audited. | IS must enumerate the transitive NuGet closure of the direct Kafka/Avro dependencies with exact package + version + SPDX license, and verify OSI compatibility before pinning. |
| **R-8** | **Aiven credential provisioning is a single-point schedule dependency** (U-7 leaves only the SASL user/password outstanding). Slippage blocks all integration testing and thus the deadline. | IS step 1 (or earliest feasible) must be gated on credentials being in place; a hard date for credential availability must be agreed with the user/ops before IS begins. |
| **R-9** | **Poison-message / schema-incompatibility stalling a consumer group.** | Mitigated by C-8 (DB error-log table + offset commit + continue); IS defines table schema and replay tooling. |

---

---

## 10. Review History

### R1 (2026-04-13) — REVISION REQUIRED
Panel: Claude Sonnet 4.6, Gemini Pro 3.1, GPT 5.3-codex. All three returned REVISION REQUIRED.

| R1 Finding | Reviewer | Severity | Disposition | Resolution |
|---|---|---|---|---|
| SC-2 conflates partition reassignment with deployment resumption | Sonnet F-1, GPT F-2 | High | Accept | Split into SC-2a (reassignment), SC-2b (new-deployment acceptance), SC-2c (no duplicate execution). Aligned with §3 Non-Goal on in-flight state. |
| Split-brain during rebalance is a correctness property, not a tuning detail | Gemini F-1 | Critical | Accept | Added §5.1 Safety Property paragraph and R-1 as a named correctness risk; SC-2c measures it directly. |
| SC-7 baseline under-specified (degraded baseline, no sample size) | Sonnet F-3, GPT F-1 | High | Accept | SC-7 rewritten: baseline = pre-HA-disablement 30-day window (2026-03-14 to 2026-04-13); measurement window = 30 days or ≥200 deployments. |
| Rollback contradicts SC-1/SC-6 (hard cutover + decommission) | Sonnet F-9, Gemini F-4, GPT F-4 | High | Accept | Added C-9 (14-day rollback window with cold-standby RabbitMQ); SC-6 rewritten to reference C-9; R-3 rationale recorded. |
| Poison message / DLQ strategy under-classified | Gemini F-3, GPT F-5 | High | Accept | Added C-8 (DLQ pattern required at HLPS level) and R-9. |
| `LockLostToken` semantic gap is architecturally load-bearing | Sonnet F-6, GPT F-10 | High | Accept | Promoted from U-11 footnote to R-1 and §5.1 Safety Property; fallback to SQL-Server advisory lock documented. |
| SC-1 grep list incomplete | GPT F-6 | Medium | Accept | Broadened SC-1 to include package refs, config files, Helm/IaC. |
| Confluent SR Serdes license risk | Sonnet F-7, GPT F-7 | Medium | Accept | Added R-7; C-1 amended to require per-dependency SPDX verification in IS. |
| Cutover runbook gates missing | GPT F-3 | High | Accept | Added SC-8 (runbook, go/no-go gates, max duration, rollback triggers, staging dry-run). |
| Ordering + delivery semantics unspecified | Gemini F-6, F-7 | Medium | Accept (partial) | C-7 (at-least-once + idempotency) added; ordering per-flow captured as U-12. |
| Aiven tier / feature-restriction constraints missing | Gemini F-8 | Medium | Accept | Added U-13. |
| Credential lifecycle missing | Gemini F-5 | Medium | Accept | Added U-14. |
| Aiven credential provisioning is schedule-critical | GPT F-8 | Medium | Accept | Added R-8. |
| Goal 4 / SC-6 wording mismatch | GPT F-11 | Low | Accept | Goal 4 rewritten: "Complete the production cutover...". |
| §3 Non-Goal duplication with §8 Out of Scope | Sonnet F-5 | Medium | Downgrade to Low | Kept separate: §3 = product-level, §8 = technology-level (clarification added implicitly; no further restructure — minimal-fix discipline). |
| Problem statement citation missing | Sonnet F-8 | Low | Accept | Cited `docs/monitor-robustness/`. |
| No descope levers for R-3 | Gemini F-10 | Low | Accept | Named descope candidates added to R-4 (was R-3). |
| Schema-governance SC missing | GPT F-12 | Low | Accept (merged) | Merged into SC-4. |
| U-5 "assumed standard" caveat leaks into IS | Sonnet F-4 | Medium | Reject | U-5 resolved as industry-standard defaults (RF=3, min.insync.replicas=2). If SEFE ops require different values this will surface during IS credential/cluster provisioning; not an HLPS-blocking concern. |

### Post-R1 addendum (2026-04-13, user-directed)

After R1 triage, the user directed that the existing internal library **`Trading.Core.Messaging.Kafka` v4.2.0** (`C:\src\trading-core-messaging-kafka`) be used **as a design reference only** — it is an internal, non-OSS library, so a runtime dependency on it would violate Goal 3 (DOrc is OSS). DOrc will reimplement the useful patterns inline on top of `Confluent.Kafka` + `Chr.Avro` directly. The envelope (accept/reject) pattern from the reference is a good idea but is **optional** — DOrc's usage is tailored enough that a thinner or absent abstraction may be preferable in some call-sites. Changes:

- Added **C-10** framing the library as a **design reference, not a dependency**. Conventions inherited by reimplementation: Confluent.Kafka + Chr.Avro direct deps, schema auto-register, subject-aligned-to-topic, SaslSsl+SCRAM-SHA-256, Acks=All, manual commit, CooperativeSticky, structured rebalance logging, session=30s / heartbeat=10s / max-poll=5min defaults.
- §5.3 rewritten to depend directly on Chr.Avro + Schema Registry.
- §5.4 rewritten: minimal internal wrapper over Confluent.Kafka; envelope pattern optional per call-site.
- §5.5 mandates DOrc wires the partition-assigned / revoked / lost callbacks itself into its logging pipeline (supports SC-2c auditability).
- §5.1 Safety Property retains the cooperative-leadership concern — mechanism unchanged (`CooperativeSticky` + partition-revoked callback).
- R-7 re-scoped: audit the transitive closure of DOrc's **direct** Kafka/Avro dependencies (not the reference library's closure).
- The reference library confirms — by its own omissions — that DLQ, leader election, and topic provisioning are non-trivial: DOrc owns these (C-8, §5.1, U-10).

Status after fixes: `REVISION` → ready for resubmission as `IN REVIEW (R2)`.

### R3 (2026-04-13) — targeted, UNANIMOUS APPROVE WITH MINOR

Same panel, scoped strictly to the C-8 amendment, §5.2 ordering addition, and R-9 rewrite.

| Reviewer | Verdict | Convergent findings |
|---|---|---|
| Claude Sonnet 4.6 | APPROVE WITH MINOR | Error-table retention + ops-visibility |
| Gemini Pro 3.1 | APPROVE WITH MINOR | DB-unavailable correlated-failure fallback |
| GPT 5.3-codex | APPROVE WITH MINOR | DB-unavailable fallback + retention + non-Request-flow enumeration |

No Critical/High. Three Medium findings triaged as **Accept**, all addressable as one-line HLPS additions:

| Finding | Disposition | Fix |
|---|---|---|
| DB-unavailable fallback (C-8's own dependency) | Accept | C-8 extended: "fall back to structured-log capture + commit; IS defines sink + alerting." |
| Error-log table retention / growth | Accept | C-8 extended: "purge/archival policy required in IS; raw payloads beyond size cap may be truncated." |
| Non-Request flows not enumerated in §5.2 | Accept | §5.2 extended to name leader-election topic (keying moot) and DataService events; IS selects keying against SC-3. |
| Hot-partition skew under Request-ID keying | Reject | DOrc per-Request event volume is modest and bounded; reviewers agreed as "no HLPS change required." |
| Replay beyond 7-day Kafka retention | Defer to IS | C-8 captures raw payload inline, which makes replay independent of Kafka retention. IS confirms. |
| Ops-visibility / alerting | Accept (folded into DB-fallback fix above) | IS defines alerting signal. |

Panel verdict aggregated: **unanimous APPROVE WITH MINOR**, all minors addressed in-line. No R4 required (and §4 caps rounds at R3).

### Post-R2 addendum (2026-04-13, user-directed) — unknown resolutions + one constraint amendment

After R2 unanimous approval, the user resolved several non-blocking unknowns and amended one constraint. Changes:

**Unknown resolutions (in-scope of R2 approval, no re-review required):**
- **U-8**: Aiven-hosted schema registry; local Karapace docker-compose for OSS/CI.
- **U-9**: Reuse DOrc's existing logging/tracing; adopt the C-10 reference library's rebalance/error log shape.
- **U-10**: Topic names as placeholders in code/config, substituted at deploy time by IaC. Default partition count **12**. RF=3, min.insync.replicas=2.
- **U-12**: Per-partition ordering via Kafka message key = **Deployment Request ID**.
- **U-13**: Custom Aiven tier, no practical feature limits.
- **U-14**: SASL user/password has no expiry; TLS cert expiry vendor-managed. IS covers per-env isolation + secret-store placement.

**Constraint amendment (substantive — see §10 Re-review Decision below):**
- **C-8 changed**: DLQ pattern **replaced** by a DOrc DB error-log table. Target Kafka deployment does not support DLQ features. On failure: row the error (topic, partition, offset, consumer group, key, payload, error, timestamp) → commit offset → continue. R-9 updated accordingly; §5.2 updated to describe message keying.

### Re-review Decision

The C-8 amendment materially changes a panel-approved constraint. Under CLAUDE.md §4, revisions to APPROVED artifacts must go through the Quality Gate. **Pending user direction**: re-run a targeted R3 review scoped to the C-8 amendment (+ U-12 ordering + §5.2 update), OR accept the amendment as a user-authoritative simplification without re-review (risk: reviewers may have challenged the error-table approach on replay/operational grounds).

Recommendation: a **focused R3 on C-8 + §5.2 + U-12 only** (30-min turnaround, same panel) is cheap insurance. Unknown resolutions (U-8, U-9, U-10, U-13, U-14) do not require re-review — they close placeholders the panel already accepted as IS-deferrable.

### R2 (2026-04-13) — UNANIMOUS APPROVAL

Same panel (Sonnet 4.6, Gemini Pro 3.1, GPT 5.3-codex). R2 scoped strictly per CLAUDE.md §4: verify R1 fixes, check for regressions, no mining of unchanged text.

| Reviewer | Verdict | Regressions found | Notes |
|---|---|---|---|
| Claude Sonnet 4.6 | APPROVE | None | All 10 R1 findings verified as adequately addressed. C-10 framing cleanly integrated. |
| Gemini Pro 3.1 | APPROVE WITH MINOR | None | All 11 R1 findings resolved; no required fixes. |
| GPT 5.3-codex | APPROVE WITH MINOR | None | All 12 R1 findings resolved. Single soft flag: SC-8's ≤4h max-cutover is a target, not a hard HLPS commitment — noted for IS to harden. Not a blocker. |

No Critical/High findings. No regressions from R1 fixes. No mandatory rework. **Unanimous Approval** — status transitions to `APPROVED — Pending user approval`.

The GPT soft flag (SC-8 cutover-duration hardening) is explicitly deferred to IS per its own disposition and the reviewer's own classification.
