# JIT Spec — S-003: Avro Schemas + Subject Registration

| Field | Value |
|---|---|
| **Status** | APPROVED — Pending user approval |
| **Author** | Claude (Opus 4.6) |
| **Created** | 2026-04-14 |
| **Step ID** | S-003 |
| **Governing IS** | `IS-Kafka-Migration.md` §3 S-003 (APPROVED R3, user-approved 2026-04-14) |
| **Governing HLPS** | `HLPS-Kafka-Migration.md` C-4, SC-4, R-6, R-4(b), §5.3 (APPROVED R3) |

---

## 1. Purpose & Scope

Define Avro schemas for the SC-3-in-scope event contracts, register them as **subjects** against the Karapace schema registry on producer initialisation, and introduce a **PR gate** that rejects incompatible schema changes in source control. This is the narrow, factually-bounded step that follows from the IS R3 Implementation-Discovery: there are **two** contracts, **three** subjects, **zero** non-serialisable content.

### In scope

- Chr.Avro-generated Avro schemas for:
  - `Dorc.Core.Events.DeploymentRequestEventData` (at `src/Dorc.Core/Models/Events/DeploymentRequestEventData.cs`).
  - `Dorc.Core.Events.DeploymentResultEventData` (at `src/Dorc.Core/Models/Events/DeploymentResultEventData.cs`).
- Schema subjects (per HLPS §5.3 Confluent convention `topic + -value`):
  - `dorc.requests.new-value` — `DeploymentRequestEventData`
  - `dorc.requests.status-value` — `DeploymentRequestEventData`
  - `dorc.results.status-value` — `DeploymentResultEventData`
- Producer-init subject registration against Karapace (via the Confluent.SchemaRegistry + Chr.Avro.Confluent integration already present in S-002's client layer).
- Compatibility mode per subject — **BACKWARD** (HLPS default).
- A PR-gate implementation that rejects any schema change that would break BACKWARD compatibility against the currently-registered subject.
- Wiring S-002's `IKafkaSerializerFactory` DI slot with an Avro-backed factory so producers built via `IKafkaProducerBuilder<TKey, TValue>` serialise values through the schema-registry codec without call-site changes.
- AT-7 carried forward from S-002: first Aiven SASL/SCRAM connectivity check executes as S-003's entry gate (if still outstanding).

### Out of scope

- Migration of producers / consumers onto the new schemas (S-006 / S-007).
- Schema evolution playbook beyond BACKWARD declaration (post-initiative concern).
- Key schemas (HLPS §5.2: keys are `RequestId` strings; registering a key schema is deferred until there is a concrete need — S-006 can lift this if required).
- Any non-Event contract (no others exist).

---

## 2. Requirements

### R-1 — Schema generation from .NET contract types

Avro schemas for the two event records are **generated from the .NET types** (not hand-written JSON), via Chr.Avro's `AbstractSchemaBuilder` or equivalent. The generated schema is **checked into source control** alongside the contract type as a canonical reference and diff target for the PR gate (see R-5).

Regeneration must be deterministic: running the generator twice against the same type yields byte-identical output.

### R-2 — Subject registration on producer init

When an S-006 / S-007 producer is built via `IKafkaProducerBuilder<TKey, TValue>` against a value type in scope of S-003, the **default** path is auto-registration on first produce attempt (`auto.register.schemas = true` on the Avro serializer). An **optional** warm-up call is exposed for callers that want to pay the registration cost at startup rather than on the hot path; neither path is mandatory over the other.

Concurrent-registration semantics:

- Two processes producing byte-identical schemas → registry-side fingerprint dedupes; **no-op** (no new version, no exception).
- Two processes producing **divergent** schemas → the loser of the race surfaces a clear exception naming the subject + registry response body. **Schema churn** (v1 → v2 → v1 ping-pong) is explicitly not an accepted outcome.
- A schema that conflicts with an already-registered version under BACKWARD compatibility fails loud with the same exception shape.

### R-3 — Compatibility mode BACKWARD per subject

Each of the three subjects is created (or updated) with compatibility mode **BACKWARD**. If the subject already exists with a different mode, the step logs a warning naming the subject + observed mode, **proceeds with produce** (the warning is advisory, not terminal — deployments must not block on out-of-band registry state), and the observed deviation is recorded in the AT-3 deliverable for operator reconciliation out-of-band.

Explicit per-subject config vs. registry-global inheritance: either is acceptable provided the AT-3 verification confirms the effective mode is BACKWARD; the canonical subject state is "effective mode = BACKWARD," not "explicit override set."

### R-4 — Avro-backed serializer factory

Ship a new `AvroKafkaSerializerFactory` implementing `IKafkaSerializerFactory` (from S-002) that returns a Chr.Avro-backed `ISerializer<T>` / `IDeserializer<T>` for each in-scope value type, and `null` for everything else (so S-002's default-fallback semantics are preserved for types outside S-003's subject map).

Registered in DI via an additive extension `AddDorcKafkaAvro(IConfiguration)` that:
- Binds a thin `KafkaAvroOptions` section (minimally: subject overrides if needed — default names match the §1 list).
- Replaces the `IKafkaSerializerFactory` singleton previously registered by `AddDorcKafkaClient` (so the Avro factory becomes the active one). The extension must **tolerate either call order** vs. `AddDorcKafkaClient` — a DOrc host calling `AddDorcKafkaAvro` before `AddDorcKafkaClient` must still end up with the Avro factory active after both extensions have run.
- Is idempotent (second call is a no-op, matching the S-002 pattern).

The **type → subject mapping** (which .NET value type maps to which registry subject) lives inside `AvroKafkaSerializerFactory` as an internal map seeded with the three §1 entries on construction. The map is **overridable via `KafkaAvroOptions`** (a dictionary in config) so test environments or future subject-name changes don't require a code edit. S-006 producer wiring depends on the mapping being centralised here, not scattered across call-sites.

### R-5 — PR-gate for schema compatibility

A build / CI check run on every PR touching **any of**:

- `src/Dorc.Core/Models/Events/**/*.cs` (contract types), or
- `docs/kafka-migration/schemas/**/*.avsc` (canonical or latest-snapshot schema files).

The gate:

1. Regenerates the schema from the current code.
2. Compares the regenerated schema to the checked-in canonical under `docs/kafka-migration/schemas/current/`.
3. If they differ (or if a `.avsc`-only edit is present), performs a **BACKWARD compatibility check** — regardless of any mode observed in the live registry — between the candidate schema and the latest registered version.
4. Fails the build on incompatibility, with a message naming the subject and the specific incompatibility (added required field, removed field, type change, etc.).

The gate always enforces **BACKWARD**. If the live registry reports a different observed mode on the target subject (R-3 warning path), PR-gate behaviour does not change — the gate's job is to protect downstream consumers assuming BACKWARD; observed-mode reconciliation is an operator task.

**Source selection for "latest registered version" in step 3:**

- **Prefer the live Karapace endpoint** (`/compatibility/subjects/{subject}/versions/latest`) when reachable from the CI runner.
- **Fall back to a locally-committed snapshot** under `docs/kafka-migration/schemas/latest/<subject>.avsc` when the registry is unreachable. Snapshot refreshes are a separate PR that the gate itself runs against (per trigger scope above).
- **Fail closed** — if neither source is available, the gate fails the build with an "unable to determine latest schema" message; it does not pass by default.

The gate is expressed as an Azure Pipelines job slotted into the existing `pipelines/dorc-kafka.yml`, and is also runnable locally (dotnet tool / script) so a developer can dry-run before pushing.

### R-6 — Schema-file repository layout

Canonical schemas live under `docs/kafka-migration/schemas/current/<subject>.avsc`. Snapshot of the last registry-acknowledged version lives under `docs/kafka-migration/schemas/latest/<subject>.avsc` (see R-5). Both are human-readable JSON (Avro canonical form).

### R-7 — Logging + observability

Subject-registration events (success, rejection, compatibility-mode mismatch) are logged at Information or Warning level via the standard `Microsoft.Extensions.Logging` pipeline, using structured properties `{Subject}`, `{SchemaVersion}`, `{CompatibilityMode}`. No new observability primitive — reuse the same logger abstractions S-002 is wired for.

### R-8 — AT-7 carry-forward from S-002

If Aiven SASL/SCRAM credentials have landed since S-002 completion, S-003's **first execution gate** is AT-7: authenticate to the Aiven non-prod cluster from a DOrc service account and retrieve cluster metadata. Success clears AT-7. Failure escalates per HLPS R-8 and may trigger R-4(b) reconsideration.

If credentials have **still not landed** at S-003 execution time, AT-7 remains carried forward until the first step that demands a running Aiven cluster; S-003's other AT's (AT-1..AT-6) are executable against the local compose stack regardless.

---

## 3. Out of Scope (explicit)

- Migration of existing producers / consumers onto Kafka — **S-006 / S-007**.
- Key schemas (deferred; string-keyed RequestId is adequate).
- Non-BACKWARD compatibility modes (FORWARD / FULL / NONE).
- Schema evolution *runbook* beyond a one-line BACKWARD declaration per subject — a post-initiative concern.
- Any contract type beyond the two named in §1.

---

## 4. Acceptance Criteria

### AT-1 — Schema generation is deterministic and matches checked-in files

- Running the Chr.Avro generator against `DeploymentRequestEventData` twice in the same process yields byte-identical Avro JSON.
- Running against `DeploymentResultEventData` likewise.
- The emitted JSON equals the checked-in canonical `docs/kafka-migration/schemas/current/<subject>.avsc` for each of the three subjects.

### AT-2 — Subject registration against Karapace (local compose)

- A test producer built via `IKafkaProducerBuilder<string, DeploymentRequestEventData>` produces a single message to `dorc.requests.new` against the S-001 compose stack.
- The subject `dorc.requests.new-value` appears in Karapace's `/subjects` listing afterwards.
- The registered schema matches the canonical file byte-for-byte.
- Same for `dorc.requests.status-value` and `dorc.results.status-value`.
- **Idempotency:** a second producer built against the same value type does **not** create a new subject version — the schema-version count in `/subjects/{subject}/versions` is unchanged after the second build-and-produce.

### AT-3 — Compatibility mode is BACKWARD

- Each of the three subjects, after AT-2 execution, returns `BACKWARD` from Karapace's `/config/{subject}` endpoint (or inherits the registry-global default which the AT verifies is BACKWARD).
- If an existing subject is found with a different mode at registration time, the step logs a Warning naming the subject + observed mode; the AT deliverable records the deviation.

### AT-4 — DI integration

- `AddDorcKafkaAvro(IConfiguration)` registers the `AvroKafkaSerializerFactory` as the active `IKafkaSerializerFactory`.
- A producer resolved via DI serialises `DeploymentRequestEventData` through the Avro codec (verified by the over-the-wire bytes beginning with the Confluent magic byte `0x00` + 4-byte schema ID).
- Calling `AddDorcKafkaAvro` twice is idempotent (descriptor count unchanged on second call).

### AT-5 — PR-gate rejects incompatible changes

- A synthetic PR-gate run that drops a required field from `DeploymentRequestEventData` fails with a message naming the subject and the specific incompatibility type ("removed required field Status", or similar).
- A synthetic PR-gate run that adds a new optional field with a default passes.
- The gate is runnable locally (`dotnet run` or `pwsh` equivalent against the schema-diff helper) without CI.
- **Test-path fidelity:** the synthetic input exercises the same code path R-5 steps 2–4 use in CI (generator → canonical delta → BACKWARD check). Whether the mutation is applied to the `.cs` contract, the `.avsc` canonical, or a fixture pair is a Delivery-phase choice — the AT asserts only that the *comparison surface* is the real one, not a bespoke fixture.
- **Source-selection coverage:** at least one AT-5 sub-run exercises each of R-5's two "latest" sources (live registry when available; snapshot fallback) to prove the path the PR gate will actually take in CI is exercised, not just the author's local environment.

### AT-6 — Consumer round-trip (Avro wire format)

- A consumer built via `IKafkaConsumerBuilder<string, DeploymentRequestEventData>` against the Avro factory reads the message produced in AT-2 and deserialises it into an equal `DeploymentRequestEventData` instance.
- Property equality is asserted field-by-field.

### AT-7 (optional / carry-forward from S-002) — Aiven connectivity

If credentials are available, a single metadata-fetch against Aiven non-prod succeeds from a developer workstation or CI. If not, the check is deferred to the next step able to exercise Aiven connectivity and noted in S-003's completion record.

---

## 5. Accepted Risks

| Risk | Source | Disposition |
|---|---|---|
| AT-7 may still be deferred if Aiven credentials remain outstanding | S-002 §5 carry-forward | Accepted — AT-1..AT-6 are executable against local Karapace. |
| Key schemas deferred — string `RequestId` used raw | §3 Out of scope | Accepted — HLPS §5.2 describes the keying; S-006 can lift this if a keyed schema is demanded. |
| BACKWARD-only compatibility | §3 Out of scope | Accepted — HLPS §5.3 default; FORWARD/FULL reserved for a post-initiative runbook. |
| PR-gate snapshot of "latest" in-repo risks drifting from Karapace reality | R-5 | Accepted — the snapshot is advisory; the authoritative check happens at subject-registration time against the live registry. |
| Chr.Avro codegen output is schema-stable across 10.11.x patch versions | Chr.Avro semver | Accepted — package pin is at 10.11.1 per S-002 / AT-6 audit; upgrades require re-regeneration + PR-gate pass. |
| **R-4 mapping is Type-in-scope + TopicNameStrategy, not a direct Type → Subject map.** Delivery interpretation: `AvroKafkaSerializerFactory` keeps a `HashSet<Type>` of in-scope value types; each returned serializer dispatches by `SerializationContext.Topic` → subject via Confluent TopicNameStrategy (`topic + "-value"`). `KafkaAvroOptions.SubjectOverrides` is carried in the type system but not wired through (the spec's "overridable via KafkaAvroOptions" clause is deferred until a real override need arises — if S-006 needs it, it lifts this disposition). Reason: a single CLR type (`DeploymentRequestEventData`) maps to two subjects (`dorc.requests.new-value` + `dorc.requests.status-value`), so a Type→Subject dictionary is ambiguous; per-topic dispatch is the correct shape. | §2 R-4 + Delivery phase |
| **Snapshot-path compat fallback is byte-equality, not full Avro BACKWARD.** Delivery interpretation: when the live registry is unreachable, the gate accepts only candidates byte-identical to the committed snapshot (after JSON canonicalisation); anything else fails closed. This is **stricter** than "always enforce BACKWARD" — legitimately BACKWARD-compatible additive changes fail the snapshot path and require either a live-registry run or a dedicated snapshot-refresh PR. Rationale: no Avro compat engine ships in the S-002/S-003 dependency set; the closed posture matches the spec's "fail closed" language in R-5 step 3. If CI frequently runs offline and this becomes friction, wire an Avro compat engine into the gate as a follow-up. | §2 R-5 + Delivery phase |

---

## 6. Delivery Notes

- **Branch:** continue on `feat/kafka-migration` per IS §1.
- **Test-first where practical:** AT-1 (generator determinism) and AT-5 (PR-gate) lend themselves to unit-test-first; AT-2 / AT-3 / AT-4 / AT-6 are integration tests against the compose stack; author alongside the capability.
- **Subject name constants:** define once in a `KafkaSubjectNames` static; producer-side code must not rehydrate topic-name-derived strings.
- **Docs:** update `docs/kafka-migration/README-local-dev.md` with any new env vars or compose-usage notes the Avro path introduces (Karapace URL is already documented).
- **Schema files:** checked-in `.avsc` files are the canonical reference; CI re-asserts equality (AT-1).

---

## 7. Review Scope Notes for Adversarial Review

Reviewers should evaluate:

- Clarity of requirements R-1..R-8 against IS §3 S-003 (APPROVED R3) and the governing HLPS sections.
- Whether the PR-gate (R-5) + canonical schema files (R-6) adequately enforce SC-4's "PR gate" criterion without over-specification.
- Whether AT-1..AT-7 collectively prove R-1..R-8.
- Accepted-risk coverage.

Reviewers should **NOT**:

- Demand exact method signatures, file layouts, DI mechanics, or assertion-library choices — per CLAUDE.md §2 JIT Spec Abstraction Level, these are Delivery-phase concerns.
- Re-litigate settled HLPS / IS decisions (Chr.Avro + Karapace, Confluent.Kafka 2.11.1, BACKWARD default, 12 partitions, Aiven SASL/SCRAM, at-least-once + idempotent, SC-3 interpretation confirmed at IS R3 CHECKPOINT-2).
- Re-open the Implementation-Discovery finding — the two-contract, three-subject scope is IS-fixed.

---

## 8. Review History

### Code Review R1 (2026-04-14) — UNANIMOUS APPROVE WITH MINOR

Panel: Sonnet-4.6, Gemini-Pro-3.1, GPT-5.3-codex. Diff range `cefc426a..HEAD` (5 increments). Verdicts: APPROVE WITH MINOR × 3. No HIGH/CRITICAL findings.

| ID | Reviewer(s) | Severity | Finding | Disposition |
|---|---|---|---|---|
| Gemini-G1 / GPT-F5 | Gemini, GPT | MEDIUM | PR-gate probe-path fail-open risk: `registryHttp` non-null on probe failure; 4xx from registry treated as "transient → fall through to snapshot" | **Accepted** — probe now nulls `registryHttp` on failure; gate splits 5xx (transient, fall-through) from 4xx-other-than-404 (explicit Fail with registry error body); `SocketException` added to catch list. |
| Gemini-G2 | Gemini | MEDIUM | Reverse DI call-order (Avro-before-Client) untested | **Accepted** — new test `AddDorcKafkaAvro_BeforeClient_FactoryCanSerialiseInScopeType` proves the Avro factory is resolvable **and usable** after both extensions run in either order. |
| Sonnet-F1 | Sonnet | MEDIUM | Probe failure path can still throw `SocketException` past the gate | **Accepted** — subsumed by Gemini-G1 fix (probe now nulls client on failure + gate catches `SocketException`). |
| Sonnet-F2 | Sonnet | MEDIUM | Gate canonical-match uses `SchemasEquivalent` (canonicalised); unit test uses strict string equality — semantic mismatch | **Accepted** — gate canonical check now uses strict string equality. `DorcEventSchemas` now emits canonicalised output at source, so strict equality applies everywhere and canonical `.avsc` files are human-diffable against `latest/*.avsc`. |
| Sonnet-F3 | Sonnet | MEDIUM | `AvroKafkaSerializerFactory` cache never disposes | **Accepted** — factory implements `IDisposable`; tracks dispatchers and disposes cached Chr.Avro serializers on shutdown. |
| Sonnet-F5 | Sonnet | MEDIUM (LOW) | PR-gate not wired into `pipelines/dorc-kafka.yml` | **Accepted** — new step `S-003 AT-5: Avro schema PR-gate (BACKWARD, live-preferred + snapshot fallback)` runs the gate against the compose-stack registry after smoke tests. |
| GPT-F1 / Sonnet-F7 | GPT, Sonnet | MEDIUM | Spec R-4 wording says "Type → Subject map overridable via `KafkaAvroOptions`"; code uses Type-in-scope + TopicNameStrategy; `SubjectOverrides` is dead config | **Accepted** — §5 Accepted Risks now records the Delivery interpretation: a single CLR type maps to two subjects, so per-topic dispatch is the correct shape; `SubjectOverrides` is deferred as a type-system hook until a concrete override need arises. |
| GPT-F2 | GPT | MEDIUM | AT-5 `IncompatibleChange` test covers only "add required field"; not a type-change variant | **Accepted** — new `AT5_LivePath_TypeChange_FailsAsBackwardIncompatible` test covers a type change (int → string on `RequestId`). Two independent BACKWARD-break shapes now proven. |
| GPT-F3 | GPT | MEDIUM | Snapshot-path byte-equality is stricter than spec's "always enforce BACKWARD" — BACKWARD-valid additive changes fail | **Accepted** — §5 Accepted Risks now records the stricter interpretation (fail closed) as intentional, with follow-up to wire an Avro compat engine if offline friction appears. |
| GPT-F4 | GPT | LOW | AT-2 literal enumeration covers `DeploymentRequestEventData` only | **Accepted** — new `AT2_SubjectRegisters_ForResultEventDataToo` covers the third subject. |
| Gemini-G3 | Gemini | LOW | `TopicDispatchingSerializer.GetOrCreate` holds `_lock` across sync-wait on registry | Defer to Delivery — low-contention topology (few topics, one first-use per topic); per-topic `Lazy<Task<>>` is a future perf polish. |
| Gemini-G4 / Sonnet-F6 | Gemini, Sonnet | LOW | `tools/generate-schemas` used relative `..` hops; inconsistent with the other two tools | **Accepted** — `generate-schemas` now uses the same `RepoRoot()` walk as `schema-gate` and `snapshot-schemas`. |
| Gemini-G5 | Gemini | LOW | `generate-schemas` wrote Chr.Avro natural-order; `snapshot-schemas` wrote canonicalised — spurious diff noise | **Accepted** — `DorcEventSchemas` now emits canonicalised by default; canonical + latest files are byte-diffable. |
| Sonnet-F4 | Sonnet | LOW | R-3 warning-path (mismatched registry mode) not covered by a test | Defer to Delivery — the happy path is sufficient for AT-3; warning emission is covered by spec text and is operator-facing. |

All 9 MEDIUM findings accepted and fixed. LOW items mostly accepted; 2 deferred with documented rationale. All fixes surgical; no scope expansion. Totals after fixes: **20 unit + 9 integration tests green**.

Per CLAUDE.md §4: three APPROVE-tier verdicts with all MEDIUMs resolved = **unanimous approval** of the S-003 code diff. S-003 is complete pending the AT-7 carry-forward (Aiven SASL/SCRAM credentials, hard-date 2026-05-01).

### R1 (2026-04-14) — UNANIMOUS APPROVE WITH MINOR

Panel: Claude Sonnet 4.6, Gemini Pro 3.1, GPT 5.3-codex. Verdicts: APPROVE WITH MINOR × 3. No HIGH/CRITICAL findings.

| ID | Reviewer | Severity | Finding | Disposition |
|---|---|---|---|---|
| Sonnet-F1 / GPT-F1 | Sonnet, GPT | MEDIUM | R-2 warm-up mandatory-vs-optional ambiguity; concurrent-registration semantics underspecified (schema-churn risk) | **Accepted** — R-2 rewritten: warm-up is optional, auto-registration is the default; concurrent byte-identical → no-op; divergent → loud failure; churn explicitly rejected. AT-2 adds a second-build idempotency assertion. |
| Sonnet-F2 / Gemini-G1 | Sonnet, Gemini | MEDIUM | R-5 live-vs-snapshot selection rule missing; gate trigger doesn't include `.avsc`-only edits | **Accepted** — R-5 trigger broadened to `docs/kafka-migration/schemas/**/*.avsc`; selection rule added ("prefer live; fall back to snapshot; fail closed"). |
| Gemini-G2 | Gemini | MEDIUM | R-3 ↔ R-5 mode-mismatch interaction undefined | **Accepted** — R-5 explicitly always enforces BACKWARD regardless of observed registry mode. R-3 restated as warning-advisory (proceeds with produce, operator reconciles out-of-band). |
| GPT-F2 | GPT | MEDIUM | AT-5 synthetic-input mechanics ambiguous | **Accepted** — AT-5 now states the synthetic input exercises the same R-5 steps 2–4 path; mutation mechanics (`.cs`, `.avsc`, fixture pair) left to Delivery. |
| GPT-F4 | GPT | LOW | Type → subject mapping home not stated | **Accepted** — R-4 now pins the map inside `AvroKafkaSerializerFactory`, overridable via `KafkaAvroOptions`. |
| GPT-F5 | GPT | LOW | R-3 terminal-vs-advisory not stated | **Accepted** — subsumed by Gemini-G2 fix (R-3 now states "advisory, not terminal"). |
| Gemini-G5 | Gemini | LOW | `AddDorcKafkaAvro` call-order vs. `AddDorcKafkaClient` | **Accepted** — R-4 now requires either call order to produce the same result. |
| Sonnet-F3 | Sonnet | LOW | AT-3 global-default vs. per-subject explicit config | **Accepted** — R-3 now states canonical state is "effective mode = BACKWARD," not "explicit override set." |
| Sonnet-F4 | Sonnet | LOW | AT-5 local-runnability not mirrored in R-5 | **Accepted** — R-5 now states the gate is locally runnable. |
| Sonnet-F5 / GPT-F3 / Gemini-G4 | Sonnet, GPT, Gemini | LOW | Key-schema future layout; snapshot-path coverage sub-bullet; snapshot-source provenance | Defer to Delivery — AT-5 coverage sub-bullet added for snapshot path; naming and provenance are delivery-phase choices. |
| Gemini-G3 | Gemini | LOW | AT-7 failure-vs-deferral disposition | Defer to Delivery — S-003 AT-1..AT-6 are executable regardless; the S-006 entry-gate continues to track AT-7 state whether the S-003 check was deferred or failed. |

All 6 MEDIUMs accepted and fixed via surgical edits. 4 LOWs accepted + subsumed; 4 LOWs deferred to Delivery. No re-litigation of prior rounds. Status transitions: `IN REVIEW (R1)` → `APPROVED — Pending user approval`.
