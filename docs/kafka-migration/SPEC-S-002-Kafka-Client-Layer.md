# JIT Spec — S-002: Internal Kafka Client Layer

| Field | Value |
|---|---|
| **Status** | APPROVED (user-approved 2026-04-14) |
| **Author** | Claude (Opus 4.6) |
| **Created** | 2026-04-14 |
| **Step ID** | S-002 |
| **Governing IS** | `IS-Kafka-Migration.md` §3 S-002 (APPROVED R2) |
| **Governing HLPS** | `HLPS-Kafka-Migration.md` §5.4, §5.5, C-1, C-10, R-7 (APPROVED R3) |

---

## 1. Purpose & Scope

Introduce a **thin internal abstraction** inside DOrc that wraps `Confluent.Kafka` 2.11.1 and `Chr.Avro` 10.x directly, providing one centralised place for:

- Configuration binding (bootstrap servers, authentication, schema-registry endpoint, consumer group identity, per-component tuning).
- Producer + consumer construction (builder-style; no runtime dependency on the C-10 reference library).
- Rebalance-callback observability (log shape matching the reference library's partition-assigned / revoked / lost format — see §4.3).
- DI registration onto `Microsoft.Extensions.DependencyInjection` via a dedicated extension entry point.
- An **optional** message-envelope accept/reject convention that call-sites may opt into on a per-topic basis.

This step does **not**:

- Define Avro schemas for any DOrc message type — that is S-003.
- Wire the DB error-log into any consumer — that is S-004 DAL + S-006/S-007/S-008 per-flow integration.
- Migrate any existing RabbitMQ flow — those are S-006 onwards.
- Introduce Kafka-based leader election — that is S-005.

Out-of-scope additions during delivery must be rejected and deferred to the relevant step's own JIT Spec.

---

## 2. Requirements

Requirements are expressed as capabilities the implementation must deliver, not as method signatures or package layouts. The Delivery phase chooses names, file structure, DI registration mechanics, and test patterns.

### R-1 — Configuration model

A typed configuration surface that binds from `Microsoft.Extensions.Configuration` (appsettings + environment variables) and carries at minimum:

- Bootstrap-servers list.
- Authentication mode (at minimum: `PLAINTEXT` for local dev, `SASL_SSL + SCRAM-SHA-256` for Aiven; extension for other SASL mechanisms is a future concern, not S-002's).
- SASL username / password (sourced from the secret store in non-dev environments; plain string in dev compose).
- Optional CA certificate location for environments requiring custom trust (deferred to §5 "Accepted Risks" — Aiven uses public CA so this is a forward-compat hook, not tested end-to-end in S-002).
- Schema-registry base URL + optional basic-auth credentials.
- Consumer-group identity for consumer components; per-component override supported.
- Auto-offset-reset policy (default `earliest`, per-component override).
- Per-component `EnableAutoCommit` toggle — default `false` (manual commit) to satisfy HLPS C-7 at-least-once.

Binding failures must fail fast at DI-container build time with a message naming the missing/invalid key.

### R-2 — Producer construction

A builder entry point that, given the config and a logger, produces a `Confluent.Kafka` producer configured for:

- Idempotent production (`enable.idempotence = true`) to satisfy HLPS C-7.
- `acks = all`.
- Schema-registry-backed Avro value serialisation (via a pluggable serializer factory; S-003 provides the Chr.Avro-derived serializer per message type, so S-002 exposes the extension point, not a concrete schema binding).
- String key serialisation (keys are Deployment Request IDs per HLPS §5.2 / U-12, which are strings today; if the eventual concrete type differs, the Delivery phase selects the appropriate serializer — the builder must not hard-code `string`).
- Structured error logging on produce-error callback.

The builder must be reusable across multiple producer instances with distinct topic/key/value type parameters.

### R-3 — Consumer construction

A builder entry point that, given the config and a logger, produces a `Confluent.Kafka` consumer configured for:

- Manual commit (per R-1 default, overridable per component).
- Schema-registry-backed Avro value deserialisation (pluggable factory per R-2).
- Rebalance callbacks wired to the log shape in §4.3.
- Error and statistics callback logging (structured).
- Cooperative-sticky assignment strategy (baseline; the leader-election step S-005 may override for its specific consumer).

### R-4 — Rebalance observability

On every rebalance event, the consumer must emit a structured log entry that is **shape-compatible** with the C-10 reference library (§4.3) so that operators familiar with the reference library's logs recognise the format immediately. The log shape is the contract here; the wrapping class / method names are not.

### R-5 — DI registration entry point

A single extension point that registers the configuration binding, a connection/config provider, and the producer + consumer builder services into an `IServiceCollection`, such that consumer components receive builders via constructor injection and can request named or typed producers/consumers without each component re-implementing `Confluent.Kafka` construction.

Idempotent: calling the entry point twice must not double-register.

### R-6 — Optional envelope pattern

Expose (but do not mandate) an envelope contract that call-sites may opt into per topic: a thin wrapper carrying message headers (correlation id, message id, source component identity, timestamp) alongside the payload. Non-envelope call-sites produce and consume raw payloads directly.

The envelope contract must be purely additive — no consumer or producer builder should *require* envelope use to function.

### R-7 — OSI-only transitive-closure license audit (acceptance gate)

The step cannot be marked complete until a license audit covering the full NuGet transitive closure of the new client-layer assembly (and the smoke-test / integration-test projects that exercise it) is captured as a deliverable under `docs/kafka-migration/` and every package resolves to an OSI-approved license (per HLPS R-7 / C-1). A non-OSI finding triggers IS revision — remediation options: upstream patch, alternative package, or vendor swap. Silent acceptance is forbidden.

The audit method is a Delivery-phase choice (e.g. a `dotnet-project-licenses` run, a hand-collated table from `dotnet list package --include-transitive` cross-referenced to SPDX identifiers, or equivalent). The deliverable is the **report** plus a one-line verdict; the mechanism is not prescribed.

### R-8 — Local dev + CI parity

The client layer must be exercisable end-to-end against the S-001 `compose.kafka.yml` stack with no code changes between local and CI — only configuration differs. CI exercises the integration test from §4 against the compose stack brought up per S-001's pipeline.

---

## 3. Out of Scope (explicit)

- Avro schema definitions for DOrc message types → **S-003**.
- Schema-registry PR gate for incompatible changes → **S-003**.
- DB error-log DAL and consumer error-handling wiring → **S-004** (DAL) + **S-006/S-007/S-008** (consumer wiring per flow).
- Kafka-based leader election / partition-affinity for singleton work → **S-005**.
- Migration of any existing RabbitMQ flow → **S-006 onwards**.
- Envelope schema evolution rules → deferred; revisit if S-003 finds envelope fields need schema governance.
- OAuth / Kerberos / mTLS connection providers → not required by Aiven's SASL/SCRAM path; add only when a concrete need arises.

---

## 4. Acceptance Criteria

All must pass before S-002 can enter Adversarial Review for the code diff.

### AT-1 — Build & package hygiene

- The new client-layer assembly builds clean (0 warnings, 0 errors) in `Release` on the same pipeline that runs S-001.
- Configuration binding unit tests cover: valid config round-trips all R-1 fields; missing mandatory key fails DI-container build with a message naming the key.

### AT-2 — Producer/consumer round-trip integration test

Against the S-001 compose stack (PLAINTEXT, no auth):

- A producer built via the R-2 builder publishes a message with a string key and a byte-array value; a consumer built via the R-3 builder in a fresh consumer group reads the same message and asserts key + value equality.
- Manual-commit path: consumer commits offset explicitly after processing; restart of consumer resumes past the committed offset with no re-read.

This is a superset of the S-001 smoke test (which uses raw `Confluent.Kafka`), now exercising the S-002 abstraction.

### AT-3 — Rebalance-log shape test

- Two consumer instances in the same group subscribe to a topic with **≥4 partitions** (sized so each side has a non-empty assignment delta on join under cooperative-sticky). On the second instance joining, each instance emits a rebalance entry whose incrementally-assigned and/or incrementally-revoked parameters accurately reflect the delta it observed; empty deltas are permitted and must still be logged with the §4.3 shape.
- Forced un-subscribe on the second instance produces the revoked-log shape on the consumer(s) that experience the revocation; any incremental re-assignment on the remaining consumer is logged with the assigned-log shape.
- Simulated lost-partition path (e.g. by forcing session-timeout expiry) produces the lost-log shape. If simulating session-timeout expiry proves impractical at test scope, the Delivery phase may substitute a direct invocation of **the same handler used by R-3 production consumers** (not a test double) with a fabricated partition list, documenting the substitution in the test file.

### AT-4 — DI registration test

- Starting a minimal host with the R-5 extension point registered produces consumer/producer builders resolvable by constructor injection.
- Calling the extension point twice against the same `IServiceCollection` produces no duplicate registrations (idempotent) — evidenced by service-descriptor count unchanged on second call.

### AT-5 — Envelope pattern test

- A consumer configured with the envelope wrapper extracts expected header fields from an envelope-wrapped message.
- A consumer configured **without** the envelope wrapper reads a raw payload produced by a non-envelope producer — proving R-6's optionality.
- Both sub-tests must be driven by the **same R-2 / R-3 builder entry points**; envelope engagement is purely a call-site opt-in, not a separate builder variant.

### AT-6 — OSI license audit deliverable (R-7 gate)

- A report file `docs/kafka-migration/S-002-license-audit.md` exists, lists every direct + transitive package in scope, each with its SPDX license identifier, and carries a one-line verdict: *All packages resolve to OSI-approved licenses.*
- **Audit scope** is: the new client-layer assembly plus any additional projects introduced or modified in S-002 that reference Kafka / Avro / schema-registry packages. The S-001 `Dorc.Kafka.SmokeTests` project is **re-included** in the audit on the basis that S-002 may add packages to it — its current closure (Confluent.Kafka 2.11.1 + MSTest) is trivially in scope.
- Any non-OSI finding blocks acceptance and escalates per R-7.

### AT-7 — Aiven connectivity spike (soft gate, deferred if AT-4 of S-001 slips)

If Aiven SASL/SCRAM credentials are available at S-002 completion time, a single connectivity test from a developer workstation (or CI with appropriate egress) authenticates against the Aiven non-prod cluster using the R-1 `SASL_SSL + SCRAM-SHA-256` path and retrieves the cluster's metadata.

If credentials are **not** yet available (the Aiven credentials hard-date deliverable in S-001 / HLPS R-8 has slipped past 2026-05-01), AT-7 is **carried forward as an unresolved acceptance item** tracked on the project risk register until Aiven credentials land, at which point it executes against the first step able to exercise Aiven connectivity (typically S-003 on schema-registry-backed producer init). S-002 proceeds to review on the basis of AT-1 through AT-6. Deferring AT-7 is a documented accepted risk below.

---

## 4.3 Rebalance-log Shape Contract

Consumers must emit, on rebalance events:

- **On partitions assigned** — Information level, message template referencing the incrementally-assigned partitions and the full post-assignment set, with both rendered as comma-separated partition-number lists inside structured parameters.
- **On partitions revoked** — Information level, message template referencing the incrementally-revoked partitions and the remaining-after-revoke set, same rendering.
- **On partitions lost** — Warning level, message template referencing the lost partitions.

Structured-parameter names and the semantic content (incrementally-assigned vs all-after, incrementally-revoked vs remaining, lost) are the contract. Exact message-template wording is a Delivery-phase detail provided it clearly conveys the same semantics; operators reading the logs must be able to tell *what changed* and *what the new steady state is* from a single entry.

---

## 5. Accepted Risks (carried forward to code review)

| Risk | Source | Disposition |
|---|---|---|
| AT-7 may be deferred if Aiven credentials (S-001 AT-4) are not delivered by S-002 completion | IS §3 S-002 hard-date clause + HLPS R-8 | Accepted: S-002 can close on AT-1–AT-6; AT-7 executes as first S-003 entry-gate. |
| Cooperative-sticky assignment may be overridden by S-005 for the singleton-leader consumer | IS §3 S-005 | Accepted: R-3 baseline is the default; S-005 is entitled to vary for its specific consumer. |
| No OAuth / Kerberos / mTLS connection provider | §3 Out of Scope | Accepted: Aiven uses SASL/SCRAM; YAGNI until a concrete need appears. |
| Chr.Avro's license status must be verified in AT-6 as part of R-7 | HLPS R-7 | Accepted: AT-6 is the verification; a non-OSI finding triggers IS revision (not silent acceptance). |
| CA-cert location in R-1 is a forward-compat hook only — not tested E2E in S-002 | R-1 | Accepted: Aiven uses public CA; custom-CA paths will be validated when / if a private-CA environment is added. |
| Confluent.Kafka ships the native `librdkafka` binary (BSD-2-Clause) as a transitive dependency | HLPS R-7 | Accepted: native binary license is verified as part of the AT-6 transitive-closure audit; a non-OSI classification there triggers IS revision. |
| Cooperative-sticky assignment (R-3 baseline) carries a two-leader window possibility during rebalance | HLPS §5.1, IS §3 S-005 | Accepted: mitigation is S-005's responsibility (idempotency / fencing / SQL fallback per the ADR); S-002 only sets the baseline assignment strategy. |

---

## 6. Delivery Notes

- **Branch:** continue on `feat/kafka-migration` per IS §1 (all step branches merge into a single long-lived integration branch until cutover-ready). Incremental commits per logical increment; no prescribed count.
- **Test-first where practical:** AT-1 and AT-4 lean naturally unit-test-first; AT-2, AT-3, AT-5 are integration tests and are authored alongside the capability under test.
- **Docs:** update `docs/kafka-migration/README-local-dev.md` with any new env vars or compose-usage instructions the client layer introduces. The license-audit report (AT-6) is a new deliverable file.
- **Commit messages:** small, topical; the Delivery phase chooses the granularity that keeps diffs reviewable.

---

## 7. Review Scope Notes for Adversarial Review

Reviewers should evaluate:

- Clarity and completeness of requirements against IS §3 S-002 and the governing HLPS sections.
- Acceptance-criteria coverage: does every requirement R-1…R-8 have a corresponding AT?
- Risk coverage: are known risks named and dispositioned?
- Feasibility of the acceptance criteria against the S-001 compose stack.

Reviewers should **NOT**:

- Evaluate pseudocode for syntactic correctness (no pseudocode is prescribed here by design).
- Demand specific method signatures, file layouts, DI registration mechanics, assertion-library choices, test-project boundaries, commit counts, or commit messages — per CLAUDE.md §2 JIT Spec Abstraction Level, these are Delivery-phase concerns.
- Re-litigate settled HLPS / IS decisions (OSI-only, no DLQ, Aiven SASL/SCRAM, Avro+Karapace, Confluent.Kafka 2.11.1, Chr.Avro, 12-partition default, at-least-once + idempotent).

---

## 8. Review History

### R1 (2026-04-14) — UNANIMOUS APPROVE WITH MINOR

Panel: Claude Sonnet 4.6, Gemini Pro 3.1, GPT 5.3-codex. Verdicts: APPROVE WITH MINOR / APPROVE WITH MINOR / APPROVE WITH MINOR. No HIGH/CRITICAL findings. No re-litigation of HLPS/IS decisions.

| ID | Reviewer | Severity | Finding (summary) | Disposition |
|---|---|---|---|---|
| Sonnet F-1 | Sonnet-4.6 | LOW | R-8 parity has no dedicated AT | Defer to Delivery — AT-2/AT-3/AT-5 run against S-001 compose stack unchanged, satisfies R-8 by reference. |
| Sonnet F-2 | Sonnet-4.6 | LOW | AT-1 doesn't distinguish missing vs invalid config values | Defer to Delivery — "fail fast at DI-container build time" in R-1 covers both; Delivery phase chooses assertion matrix. |
| Sonnet F-3 | Sonnet-4.6 | LOW | AT-5 doesn't strictly prove R-6 non-mandation | **Accepted** — tightened AT-5 to require same builder entry points. |
| Sonnet F-4 | Sonnet-4.6 | LOW | librdkafka native binary not called out in risk register | **Accepted** — row added to §5. |
| Sonnet F-5 | Sonnet-4.6 | LOW | AT-3 lost-path substitution could bypass production handler | **Accepted** — AT-3 tightened to require the real R-3 handler, not a test double. |
| Gemini G-1 | Gemini-Pro-3.1 | MEDIUM | "AT-4 of S-001" is a dangling citation | **Accepted** — replaced with descriptive reference to S-001's credentials hard-date / HLPS R-8. |
| Gemini G-2 | Gemini-Pro-3.1 | MEDIUM | AT-6 audit scope ambiguous re S-001 smoke-test project | **Accepted** — AT-6 now explicitly scopes the audit and re-includes the S-001 smoke-test project. |
| Gemini G-3 | Gemini-Pro-3.1 | LOW | Session/heartbeat/max-poll defaults not surfaced in R-1 | Defer to Delivery — R-1 cites HLPS C-10 via the governing doc list; C-10 defaults apply by reference. |
| Gemini G-4 | Gemini-Pro-3.1 | LOW | AT-3 "both sides" wording flaky under cooperative-sticky | **Accepted** — AT-3 reworded to reflect incremental-delta semantics and raised partition count to ≥4. |
| Gemini G-5 | Gemini-Pro-3.1 | LOW | Two-leader window not in S-002 risk register | **Accepted** — row added to §5. |
| Gemini G-6 | Gemini-Pro-3.1 | LOW | AT-7 reaches into a not-yet-written S-003 JIT Spec | **Accepted** — AT-7 deferral reworded to "carried forward on risk register"; no cross-step assumption. |
| GPT F-1 | GPT-5.3-codex | MEDIUM | AT-3 cooperative-sticky delta semantics could be flaky | **Accepted** — subsumed by Gemini G-4 fix: ≥4 partitions + delta-accurate wording. |
| GPT F-2 | GPT-5.3-codex | LOW | AT-5 doesn't require same builder for both envelope + raw modes | **Accepted** — subsumed by Sonnet F-3 fix. |
| GPT F-3 | GPT-5.3-codex | LOW | "test projects" wording could mean zero scope in AT-6 | **Accepted** — subsumed by Gemini G-2 fix. |
| GPT F-4 | GPT-5.3-codex | LOW | AT-1 doesn't enumerate mandatory-vs-optional R-1 fields | Defer to Delivery — R-1 bullets make the mandatory set derivable; Delivery chooses test matrix. |
| GPT F-5 | GPT-5.3-codex | LOW | R-2 key-type genericity not gated by any AT | Defer to Delivery — compile-time constraint; Delivery phase verifies by instantiation. |

All MEDIUM findings accepted and resolved via surgical edits. 6 LOWs accepted (mostly subsumed); 5 LOWs deferred to Delivery per CLAUDE.md §4 dispositions. No text material to a code-correctness concern remains unaddressed.

Per CLAUDE.md §4, three APPROVE-tier verdicts with no HIGH/CRITICAL constitute **unanimous approval**. Status transitions: `IN REVIEW (R1)` → `APPROVED — Pending user approval`.
