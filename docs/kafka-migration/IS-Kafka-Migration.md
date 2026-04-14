# IS — DOrc Kafka Migration

| Field | Value |
|---|---|
| **Status** | APPROVED — Pending user approval |
| **Author** | Claude (Opus 4.6) |
| **Created** | 2026-04-13 |
| **Target completion** | 2026-12-31 (per HLPS C-5) |
| **Governing HLPS** | `HLPS-Kafka-Migration.md` (APPROVED, R3) |

---

## 1. Sequence Principles

- Each step is **bite-sized** and independently valuable where possible, but several steps must land before the first cutover-candidate build exists.
- Ordering respects HLPS dependencies: credentials (R-8) gate everything; reference-library-inspired client layer gates all producers/consumers; leader election gates Monitor cutover; DataService migration is last per HLPS R-4 descope lever (a).
- All step branches merge to a single long-lived integration branch (`feat/kafka-migration`) until cutover-ready; cutover deploys the squash-merged main.
- The 14-day rollback window (C-9) is satisfied by redeploying a pre-cutover release tag, so RabbitMQ code paths are fully removed **before** cutover; rollback is a redeploy-prior-release, not a feature-switch.
- Each step carries its own JIT Spec (authored at step start) and its own Adversarial Review cycle.

---

## 2. Step Table (Master Roadmap)

| ID | Title | Depends on | Addresses |
|---|---|---|---|
| **S-001** | Aiven credentials + local dev environment | — | HLPS R-8, R-5, SC-5 |
| **S-002** | Internal Kafka client layer over Confluent.Kafka + Chr.Avro | S-001 | HLPS C-1, C-4, C-10, §5.4, §5.5, R-7 |
| **S-003** | Avro schemas for current message types + subject registration | S-002 | HLPS C-4, SC-4, R-6 |
| **S-004** | DB error-log table + data-access layer only (no consumer wiring) | — (parallelisable with S-002/S-003) | HLPS C-8, R-9 |
| **S-005** | Kafka-based leader election (decision spike + implementation) | S-002, S-004 | HLPS §5.1, SC-2a/b/c, R-1, R-2 |
| **S-006** | Migrate Request-lifecycle pub/sub to Kafka | S-002, S-003, S-004, S-005 decision | HLPS §5.2, SC-3, C-7, U-12 |
| **S-007** | Migrate Status-event pub/sub to Kafka | S-002, S-003, S-004 | HLPS §5.2, SC-3 |
| **S-008** | Migrate DataService pub/sub to Kafka (descope-last per R-4a) | S-002, S-003, S-004 | HLPS §5.2, SC-3 |
| **S-009** | Cut `release/pre-kafka-cutover` tag, then remove all RabbitMQ code | S-005, S-006, S-007, S-008 | HLPS SC-1, R-3 rollback |
| **S-010** | Cutover runbook + staging dry-run (incl. rollback-tag deploy test) | S-009 | HLPS SC-8, C-2, R-3 |
| **S-011** | Production cutover execution | S-010 | HLPS SC-6 |
| **S-012** | RabbitMQ decommission at T+14d (C-9 window elapsed) | S-011 | HLPS SC-6, C-9 |
| **S-013** | Post-cutover SC-7 monitoring + baseline report | S-011 (parallel with S-012 after T+14d) | HLPS SC-7 |

13 steps. IDs are stable and will not renumber if any step is removed or deferred.

---

## 3. Step Detail

### S-001 — Aiven credentials + local dev environment

**What:** Obtain SASL/SCRAM-SHA-256 credentials for DOrc against the existing Aiven Kafka cluster (non-prod + prod). Publish a `docker-compose` for OSS contributors and CI that stands up a vanilla Kafka broker + Karapace schema registry. Wire the docker-compose into CI per S-002's verification needs.

**Why:** HLPS R-8 makes credentials the single-point schedule dependency gating all integration testing. HLPS R-5 + SC-5 require a local dev story for OSS users and CI.

**Dependencies:** None — this is the critical-path kickoff.

**Hard date (per HLPS R-8):** Aiven non-prod credentials delivered by **2026-05-01**. If unmet, JIT Spec for S-002 cannot pass pre-execution audit (blocks S-002 execution gate) and escalation to user for R-4 descope discussion is triggered.

**Verification intent:** A DOrc developer can run the docker-compose locally and a sample Confluent.Kafka producer/consumer round-trips a message through it. A named SEFE engineer can authenticate to the Aiven non-prod cluster from a DOrc service account.

---

### S-002 — Internal Kafka client layer

**What:** Introduce a thin internal abstraction within DOrc that wraps `Confluent.Kafka` and `Chr.Avro` directly. Centralises: config binding (auth, bootstrap, schema-registry URL), connection construction, rebalance-callback logging (partitions-assigned / revoked / lost / error), DI registration onto `Microsoft.Extensions.DependencyInjection`. Patterns are lifted from the C-10 reference library but reimplemented inline — no runtime dependency on the reference library. The envelope accept/reject pattern is offered but not mandatory; call-sites choose.

**Why:** HLPS C-1 (OSI deps only), C-10 (library is reference-only), §5.4 (minimal internal wrapper), §5.5 (rebalance observability), R-7 (license-clean transitive closure).

**Dependencies:** S-001 (needs credentials + local cluster to validate).

**Acceptance gate (R-7):** NuGet transitive-closure license audit produces an OSI-only report. S-002 cannot close until this report is captured as a deliverable. A non-OSI finding triggers an IS revision (not silent acceptance) — remediation options: upstream patch, alternative package, or vendor swap.

**Verification intent:** Integration test: two instances of a sample consumer join a group against the docker-compose Kafka, exchange messages, and on forced rebalance produce the expected structured log shape (matching the C-10 reference library's partition-assigned/revoked/lost log format).

---

### S-003 — Avro schemas for current message types + subject registration

**What:** Define Avro schemas (via Chr.Avro generation from .NET contract types) for every message type currently carried by RabbitMQ. Implement auto-registration on producer init against the schema registry. Subject naming aligned to topic per HLPS §5.3. Document compatibility mode per subject (default BACKWARD). The PR-gate implementation may land on a separable sub-branch from schema definitions.

**Why:** HLPS C-4 (Avro mandated), SC-4 (schema registry + compatibility mode + PR gate), R-6 (schema governance).

**Dependencies:** S-002.

**Descope gate (R-4b):** Schema-governance + PR-gate must be ready by **2026-06-15**. If not, R-4(b) JSON-on-Kafka fallback is evaluated and decision recorded in IS §6. This is the dated trigger for lever (b).

**Verification intent:** A producer-init integration test registers every subject against Karapace; an attempt to register an incompatible schema is rejected by the PR gate.

---

### S-004 — DB error-log table + data-access layer only

**What:** Define a new DOrc SQL Server table capturing the C-8 fields (topic, partition, offset, consumer group, message key, raw payload, error, stack, timestamp). Provide a narrow data-access layer (insert, query, purge) the consumer runtime will later call. **Consumer-side wiring** (hooking the DAL into a `ConsumeResult<T>` failure path, including the DB-unavailable fallback to structured-log) lands in S-006/S-007/S-008 as each pub/sub flow migrates — this avoids retrofitting an access layer against a still-settling S-002 consumer contract. Retention/purge cadence specified here (rolling N-day purge + size cap with payload truncation beyond a configured size).

**Why:** HLPS C-8, R-9.

**Dependencies:** None for table + DAL (parallelisable with S-002/S-003). Consumer wiring inherits S-002's consumer contract in the migration steps.

**Verification intent:** Unit + integration tests: (a) DAL insert persists every C-8 field correctly; (b) retention purge runs and deletes rows beyond the configured window; (c) DAL contract is callable from the S-002 consumer shape. Poison-message E2E behaviour is verified per-flow in S-006/S-007/S-008.

---

### S-005 — Kafka-based leader election (two sub-phases under one step ID)

**S-005a — Decision spike (time-boxed, ≤2 weeks).** Produce an Architecture Decision Record (ADR) selecting one of: (i) Kafka consumer-group single-partition leader election with Request-level idempotency, (ii) (i) + fencing tokens, or (iii) SQL Server advisory-lock fallback (Kafka used only for event flow). The ADR must demonstrate — via a proof-of-concept under the S-002 client layer — that the chosen mechanism provides the §5.1 Safety Property (no duplicate execution of deployment side-effects during a rebalance window). Explicit decision gate before S-005b begins.

**S-005b — Implementation.** Replace `RabbitMqDistributedLock` with the mechanism selected in S-005a. If (iii) SQL fallback is chosen, adjust S-006/S-007/S-008 scope notes accordingly (Kafka for events only; lock remains in DB).

**Slip trigger (per HLPS R-4):** If S-005a has not converged on an ADR by **2026-07-15**, the SQL-advisory-lock fallback is adopted by default and S-005b begins with option (iii). This removes research-risk from the critical path.

**Why:** HLPS §5.1 Safety Property, SC-2a/b/c, R-1 (cooperative-not-fenced leadership is a correctness risk), R-2 (rebalance thrash), U-11.

**Dependencies:** S-002 (client layer), S-004 (DAL available for operational visibility during rebalance storms).

**Verification intent:**
- **SC-2a**: Leader-kill test — partition reassigned within 60s.
- **SC-2b**: New deployment submitted post-failover is accepted by the new leader within a further 30s.
- **SC-2c**: ≥20 induced rebalances produce zero duplicate **Request state-transitions** (all transitions driven by the Monitor during a deployment's lifecycle — e.g. Pending→Running, Running→Succeeded/Failed/Cancelled — are in scope, not only terminal states). The audit-trail query that exercises this check is authored as an S-005 deliverable alongside the implementation; its correctness is part of the step's acceptance.
- **R-2**: session/heartbeat tuning evaluated under simulated broker flap.

---

### S-006 — Migrate Request-lifecycle pub/sub

**What:** Port the Request-lifecycle message flow (API → Monitor and related) from RabbitMQ to Kafka. Producers set **message key = Deployment Request ID** per HLPS §5.2 / U-12. Consumers adopt manual-commit, at-least-once + idempotent handling per C-7. Wire the S-004 DAL into the consumer failure path (including the DB-unavailable structured-log fallback) for this flow. Create the Kafka topic at the placeholder name (U-10) with default **12 partitions**, RF=3, min.insync.replicas=2.

A substrate-selector **feature flag** (Rabbit vs Kafka) is introduced in DOrc config to allow test-environment comparison; this is a **substrate selector, not dual-publish** — exactly one substrate is live at a time per environment, preserving HLPS §3's no-dual-broker stance. The flag and its inactive branch are both removed in S-009.

**Why:** HLPS §5.2, SC-3, C-7, U-12.

**Dependencies:** S-002, S-003, S-004, S-005 ADR (so keying strategy and leader-semantics are known).

**Verification intent:** End-to-end deployment test — a Request submitted via API is picked up by Monitor via Kafka, processed, and status recorded; ordering verified across a burst of status changes for a single Request; injection of a poison message for this flow produces an error-log row and offset advances.

---

### S-007 — Migrate Status-event pub/sub

**What:** Port status-event pub/sub (Monitor → API / UI consumers) from RabbitMQ to Kafka. Same keying + semantics as S-006 where Request-scoped. Wire the S-004 DAL into consumer failure paths for this flow. Create Kafka topic(s) with 12 partitions default (U-10), RF=3, min.insync.replicas=2.

JIT Spec must enumerate the specific status-event sub-flows migrated in this step and pick a keying strategy per sub-flow (most will naturally carry a Deployment Request ID; any that do not are selected per HLPS §5.2 addendum against SC-3).

**Why:** HLPS §5.2, SC-3.

**Dependencies:** S-002, S-003, S-004.

**Verification intent:** E2E test: a deployment's status transitions appear in the UI in the same order they were emitted by Monitor; poison-message injection produces error-log row with offset advance.

---

### S-008 — Migrate DataService pub/sub (descope-last)

**What:** Port `RabbitDataService` pub/sub flows to Kafka. Per HLPS R-4(a), this is the last functional migration, giving descope headroom against the deadline. Keying strategy selected per-flow against SC-3 (DataService events likely have no natural Request ID — JIT Spec enumerates and justifies). Wire the S-004 DAL into consumer failure paths. Create topics with 12 partitions default, RF=3, min.insync.replicas=2.

**Why:** HLPS §5.2, SC-3; R-4(a) schedule-risk lever.

**Descope gate (R-4a):** If the aggregate of S-005b + S-006 + S-007 is not complete by **2026-10-15**, S-008 is deferred and rescoped: DataService remains on RabbitMQ through cutover, and the Rabbit-removal step (S-009) omits the DataService module until a post-cutover follow-up. This invokes R-4(a) as a named lever rather than a mid-flight improvisation.

**Dependencies:** S-002, S-003, S-004.

**Verification intent:** E2E test: DataService pub/sub paths exercise all existing subscriptions with no functional regression; poison-message injection produces error-log row with offset advance.

---

### S-009 — Cut `release/pre-kafka-cutover` tag, then remove all RabbitMQ code

**What:** Two-phase step on the integration branch:

1. **Cut and archive the `release/pre-kafka-cutover` tag** from the last green S-008 build (or S-007 if R-4(a) was invoked). This tag is the canonical rollback target per HLPS R-3 / C-9. Verify the tag deploys cleanly into staging as a standalone artifact.
2. **Remove all RabbitMQ code**, NuGet package references, config keys, queue/exchange names, Helm/IaC references, and the substrate-selector feature flag introduced in S-006 along with its now-inactive Rabbit branch.

After S-009 the integration branch is Kafka-only; the only Rabbit-capable artifact is the archived tag.

**Why:** HLPS SC-1; HLPS R-3 (rollback = redeploy this specific tag).

**Dependencies:** S-005, S-006, S-007, S-008 (all functional migrations complete — or S-007 + R-4(a) deferral, in which case DataService-specific Rabbit code is retained temporarily and S-009 scope narrows).

**Verification intent:**
- `release/pre-kafka-cutover` tag exists, is immutable (branch-protected), and is deployable into staging end-to-end (smoke-test suite passes against it).
- SC-1 grep suite passes on the post-removal integration branch (no `RabbitMQ.*`, `EasyNetQ`, `RabbitDataService`, `RabbitMqDistributedLock`, `amqp://`, `amqps://` in source, csproj, appsettings, Helm/IaC — subject to R-4(a) narrowing if invoked).

---

### S-010 — Cutover runbook + staging dry-run

**What:** Author the production cutover runbook per HLPS SC-8. Runbook contents:
- Pre-cutover smoke tests.
- Explicit go/no-go gates.
- **Hard max cutover duration: 4 hours.** This IS commits to the target as a binding SC-8 commitment. Any projected overrun during the staging dry-run triggers runbook revision before S-011 can proceed.
- Rollback trigger criteria (error-rate threshold, smoke-test failure, HA thrash signal).
- Rollback procedure: redeploy the `release/pre-kafka-cutover` tag from S-009.
- Post-cutover smoke-test suite.

Execute the runbook end-to-end against staging Aiven at least once. The dry-run must include a full rollback rehearsal: deploy Kafka build → trigger rollback → redeploy pre-kafka-cutover tag → verify deployable state.

**Why:** HLPS SC-8, C-2, R-3.

**Dependencies:** S-009.

**Verification intent:** Staging dry-run completes within 4h; rollback rehearsal completes successfully with the pre-kafka-cutover tag deploying cleanly and passing smoke tests.

---

### S-011 — Production cutover execution

**What:** Execute the runbook in production. In-flight deployments terminated at cutover per HLPS C-2. RabbitMQ infrastructure moved to cold standby (not decommissioned — that happens in S-012 at T+14d per C-9). The `release/pre-kafka-cutover` tag remains available for redeploy throughout the C-9 window.

**Why:** HLPS SC-6.

**Dependencies:** S-010.

**Verification intent:** Post-cutover smoke-test suite passes; deployment success-rate monitoring shows no immediate regression; RabbitMQ infra confirmed in cold-standby state; pre-kafka-cutover tag deployability spot-checked.

---

### S-012 — RabbitMQ decommission at T+14d

**What:** At **T+14 days post-cutover** (i.e. after the C-9 rollback window has elapsed), decommission RabbitMQ infrastructure: broker, credentials, network rules, IaC. This is an **unconditional** decommission gated solely on the elapsed C-9 window — SC-7 monitoring is a separate, parallel activity (S-013) and does not gate this step. The `release/pre-kafka-cutover` tag is archived but retained indefinitely for audit.

Step completion date: T+14d (approximately mid-December 2026 or early January 2027 depending on S-011 execution date).

**Why:** HLPS C-9, SC-6.

**Dependencies:** S-011 + 14-day elapsed window.

**Verification intent:** RabbitMQ broker, Aiven-or-other Rabbit hosting, and Rabbit-related network/IaC are gone; archived pre-kafka-cutover tag remains retrievable.

---

### S-013 — Post-cutover SC-7 monitoring + baseline report

**What:** Monitor deployment reliability against the SC-7 baseline (2026-03-14 to 2026-04-13 pre-HA-disablement window) for **30 days post-cutover or ≥200 production deployments, whichever is later**. Produce a baseline comparison report. This step runs **in parallel with S-012** after cutover; it is not a dependency of decommission.

Step completion date: **later of** T+30d or when ≥200 production deployments have executed — may extend into Q1 2027 depending on volume.

**Why:** HLPS SC-7.

**Dependencies:** S-011 (runs alongside S-012 after T+14d).

**Verification intent:** SC-7 baseline-comparison report published; deployment success-rate at or above pre-HA-disablement baseline. If the report surfaces a regression, a remediation plan is authored (separate initiative — not within this IS).

---

## 4. Schedule Sketch (non-binding, for deadline-risk awareness)

~8.5 months from 2026-04-13 to 2026-12-31. Rough banding (S-003 runs concurrently with S-004):

| Band | Steps | Target end |
|---|---|---|
| Foundation | S-001, S-002 | 2026-05-15 |
| Schemas + error-log (parallel) | S-003, S-004 | 2026-06-15 |
| Leader election | S-005a spike + S-005b | 2026-08-15 (widened from R1 review) |
| Pub/sub migrations | S-006, S-007 | 2026-10-15 |
| DataService + tag + removal | S-008, S-009 | 2026-11-15 (widened from R1 review) |
| Cutover prep + execute | S-010, S-011 | 2026-12-15 |
| Decommission | S-012 | T+14d post S-011 |
| SC-7 monitoring | S-013 | T+30d or ≥200 deployments (may extend into Q1 2027) |

### 4a. Descope Decision Gates (R-4 operationalisation)

Each HLPS R-4 lever has a dated trigger and owning step:

| Lever | Trigger date | Owning step | Action if triggered |
|---|---|---|---|
| **R-4(a)** — defer DataService | 2026-10-15 | S-008 | S-008 deferred post-cutover; S-009 narrows scope to exclude DataService Rabbit removal |
| **R-4(b)** — JSON-on-Kafka | 2026-06-15 | S-003 | Ship JSON payloads; Avro migration deferred post-cutover; schema-registry integration stubbed |
| **R-4(c)** — simplified error handling | continuous | S-004 | Already built-in: "write row → commit → continue" is the minimum-viable DLQ-equivalent |
| **S-005 research-risk** — SQL fallback | 2026-07-15 | S-005a | Adopt SQL-advisory-lock fallback by default; S-005b begins with option (iii) |
| **R-8 credentials** — schedule | 2026-05-01 | S-001 | Escalate to user; S-002 pre-execution audit cannot pass without credentials |

A lever triggering does **not** require user approval to invoke — the dated decision is pre-authorised by this IS. The trigger event is recorded in §6 review history and in project memory.

---

## 5. Review Scope Notes for Adversarial Review

Reviewers of this IS should evaluate:
- Logical step ordering + dependency correctness
- Atomicity (each step independently deliverable)
- Completeness against HLPS success criteria (every SC traceable to at least one step)
- Risk mitigation coverage
- Schedule realism against the hard 2026-12-31 deadline

Reviewers should **NOT** evaluate:
- Specific method signatures, package versions, or config keys (JIT Spec / Delivery phase concern).
- Whether cited class names or file paths currently exist in the codebase (per HLPS §2 IS Abstraction Level rules).
- Implementation-level detail within any step.

---

## 6. Review History

### R1 (2026-04-13) — REVISION REQUIRED

Panel: Claude Sonnet 4.6, Gemini Pro 3.1, GPT 5.3-codex. Verdicts: APPROVE WITH MINOR / REVISION REQUIRED / APPROVE WITH MINOR. Gemini's REVISION made the aggregate verdict REVISION REQUIRED. No uncovered HLPS SC or R; all findings were about testability, decision-gate discipline, and deadline-risk operationalisation.

| Finding | Reviewer | Severity | Disposition | Resolution |
|---|---|---|---|---|
| S-009 removes Rabbit before S-010 dry-run; rollback target never exercised end-to-end | Sonnet F-1, Gemini F-2, GPT F-1 | High (unanimous) | Accept | S-009 rewritten to explicitly cut and archive `release/pre-kafka-cutover` tag from last green S-008 build, verified deployable into staging; S-010 adds rollback rehearsal. |
| S-005 hides an architectural fork behind a single step ID | Sonnet F-2, Gemini F-1 | High | Accept | S-005 restructured into S-005a (decision spike + ADR, time-boxed ≤2 weeks) and S-005b (implementation). Dated slip-trigger 2026-07-15 defaults to SQL-advisory-lock fallback if ADR unconverged. |
| R-4 descope levers not time-gated | Gemini F-4, GPT F-2 | High | Accept | §4a Descope Decision Gates table added with dated triggers and owning steps for R-4(a), R-4(b), R-4(c), S-005 research-risk, R-8 credentials. |
| R-7 license audit under-gated | Sonnet F-3, GPT F-6 | Medium/High | Accept | S-002 "Acceptance gate" added: audit is a deliverable; non-OSI finding triggers IS revision. |
| SC-2c verification intent under-specified | GPT F-3 | High | Accept | S-005 verification intent expanded: named the state-transition scope (all Monitor-driven transitions, not only terminal states); required audit-trail query authored as step deliverable. |
| S-012 conflates decommission (C-9) with SC-7 monitoring | Sonnet F-6, Gemini F-8, GPT F-7 | High | Accept | Split into S-012 (decommission at T+14d unconditional) and new S-013 (SC-7 monitoring, parallel, may extend into Q1 2027). Total step count 12→13. |
| S-010 max-duration still soft | Sonnet F-5, GPT F-5 | Medium | Accept | S-010 hardens to **4 hours** as a binding commitment; staging dry-run must validate within 4h. |
| S-004 parallelism overstates independence from S-002 | Gemini F-3 | High | Accept | S-004 scope narrowed to **table + DAL only**; consumer-side wiring moved into S-006/S-007/S-008 where the S-002 consumer contract is already settled. |
| R-8 credentials hard date missing | Gemini F-7, GPT F-4 | Medium | Accept | S-001 adds hard date **2026-05-01**; slip triggers R-4 escalation. |
| S-005 / schedule slip-triggers missing | Gemini F-5 | High | Accept | S-005a slip-trigger 2026-07-15; §4a table aggregates all slip-triggers. S-005 band widened by 2 weeks. |
| Feature flag vs dual-broker contradiction | Gemini F-6 | Medium | Accept | S-006 clarifies: flag is a **substrate selector**, not dual-publish — exactly one substrate live per environment. Flag + inactive branch removed in S-009. |
| Topic provisioning / U-10 not called out | Gemini F-10 | Low | Accept | S-006/S-007/S-008 now each state topic creation with 12 partitions, RF=3, min.insync.replicas=2 default. |
| S-007 non-Request sub-flows not enumerated | GPT F-8 | Low | Accept | S-007 "What" notes JIT Spec must enumerate and justify per-sub-flow keying. |
| S-001 CI integration not named | Sonnet F-7 | Low | Accept (minor) | S-001 notes CI integration is under S-002 verification scope. |
| S-003 PR-gate separable | Gemini F-9 | Low | Accept (minor) | Noted in S-003 "What". |
| Schedule parallelism not visual | GPT F-9 | Low | Accept (minor) | §4 heading clarified "S-003 runs concurrently with S-004". |
| S-012 date clarity | Sonnet F-6 | Low | Accept | S-012/S-013 split makes date semantics unambiguous. |

Status after fixes: `REVISION` → ready for resubmission as `IN REVIEW (R2)`.

### R2 (2026-04-13) — UNANIMOUS APPROVAL

Same panel, scoped strictly per CLAUDE.md §4 R2+ rules: verify R1 fixes, check regressions, no mining of unchanged text.

| Reviewer | Verdict | Regressions | Notes |
|---|---|---|---|
| Claude Sonnet 4.6 | APPROVE | None | All 7 R1 findings fully addressed. S-005 split + §4a + S-009 tag + 4h commitment are the strongest improvements. |
| Gemini Pro 3.1 | APPROVE | None | All 10 R1 findings resolved. S-006 dependency on "S-005 ADR" (not S-005b complete) is correct and deliberate — no dependency cycle. Schedule still fits deadline with 16 days of slack. |
| GPT 5.3-codex | APPROVE | None | All 9 R1 findings resolved. S-013 extending into Q1 2027 is observational, not delivery — SC-6 remains bounded by S-011 and the 2026-12-31 contract. |

No Critical/High/Medium/Low findings. No regressions. **Unanimous clean approval** — status transitions to `APPROVED — Pending user approval`.
