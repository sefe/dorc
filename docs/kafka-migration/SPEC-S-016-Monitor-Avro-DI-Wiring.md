# SPEC-S-016 — Monitor-side Avro DI wiring (`AddDorcKafkaAvro` in `Dorc.Monitor/Program.cs`)

| Field | Value |
|---|---|
| **Status** | APPROVED — R2 unanimous APPROVE (2026-04-20); user auto-pilot satisfies user-approval gate per memory `project_kafka_autopilot`. |
| **Author** | Claude (Opus 4.7 1M) |
| **Created** | 2026-04-20 |
| **Governing HLPS** | `HLPS-Kafka-Migration.md` (APPROVED, R3) |
| **Governing IS** | `IS-Kafka-Migration.md` (APPROVED, R4) |
| **Step ID** | S-016 |
| **Branch** | `feat/kafka-migration` (single PR strategy — per memory `feedback_pr_strategy`) |
| **Blocks** | S-011 production cutover |

---

## 1. Motivation

Post-S-014 the Monitor starts cleanly against an installed `appsettings.json` with root-level `Kafka` config. Post-S-015 the SchemaRegistry URL is populated at install time. Nevertheless, the Monitor **cannot deserialise Avro-encoded payloads** on the Request-lifecycle topics it consumes.

Verification walk:

- `src/Dorc.Monitor/Program.cs` calls only `builder.Services.AddDorcKafkaDistributedLock(configurationRoot)` and `builder.Services.AddDorcKafkaRequestLifecycleSubstrate(configurationRoot)`. Neither calls `AddDorcKafkaAvro`.
- `AddDorcKafkaRequestLifecycleSubstrate` (see `src/Dorc.Kafka.Events/DependencyInjection/KafkaRequestLifecycleSubstrateServiceCollectionExtensions.cs`) registers the consumer and latching poll-signal, but does **not** transitively register Avro.
- `AddDorcKafkaDistributedLock` transitively calls `AddDorcKafkaClient`, which registers the no-op `DefaultKafkaSerializerFactory` (see `src/Dorc.Kafka.Client/DependencyInjection/KafkaClientServiceCollectionExtensions.cs`).
- `DefaultKafkaSerializerFactory.GetValueDeserializer<T>()` returns `null` (verified in `src/Dorc.Kafka.Client/Serialization/IKafkaSerializerFactory.cs`). `DeploymentRequestsKafkaConsumer.BuildConsumer()` guards this with `if (valueDeserializer is not null) builder.SetValueDeserializer(...)`, so when the factory returns null the setter is skipped and Confluent.Kafka falls back to its built-in primitive deserialiser. Because `DeploymentRequestEventData` is not a Confluent primitive, **every message on `dorc.requests.new` / `dorc.requests.status` raises a deterministic `ConsumeException` at the Monitor's first consume attempt.** SC-3 Request-lifecycle pub/sub is functionally broken on the consumer side.

The API side dodges this because `Dorc.Api/Program.cs` explicitly calls `AddDorcKafkaAvro`, which `RemoveAll<IKafkaSerializerFactory>()` + `AddSingleton<IKafkaSerializerFactory, AvroKafkaSerializerFactory>()` — replacing the no-op factory with the real one.

S-016 closes this by adding the missing DI call to the Monitor. The surface is one line of code + a DI-composition test.

---

## 2. Scope

### In scope

1. Add `builder.Services.AddDorcKafkaAvro(configurationRoot);` to `src/Dorc.Monitor/Program.cs` in the existing Kafka-DI region (adjacent to the two existing `AddDorcKafka*` calls).
2. Verify DI composition: after `Build()`, the resolved `IKafkaSerializerFactory` is `AvroKafkaSerializerFactory`, not the no-op default.
3. Regression test covering the DI composition, placed in the Monitor's test project or — if that project doesn't idiomatically host DI tests — in `src/Dorc.Kafka.Events.Tests/` alongside the existing Avro DI tests.
4. Optional but encouraged: a one-line Delivery note in `docs/kafka-migration/README-local-dev.md` confirming the Monitor's Avro path is now exercised by the standard compose smoke-test.

### Explicitly out of scope

- Any change to `AvroKafkaSerializerFactory` itself, the serializer-factory contract, or the Avro subject-naming policy (S-003 territory).
- Any change to `AddDorcKafkaAvro` — it is reused as-is. If the extension's current contract does not cope with being called alongside `AddDorcKafkaDistributedLock` (the transitive `AddDorcKafkaClient` registers the no-op factory first), the DI-composition test will catch it and Delivery will surface the defect to the user. But S-016 does not pre-emptively modify the extension.
- The Dorc.Api side — no Program.cs change; the API already wires Avro correctly.
- Installer and `appsettings.json` work — S-016 is pure DI-code; no template or WiX touch.

### Explicitly not touched

- Any `.wxs` / `appsettings.json` / `Setup.Dorc.msi.json` / `Install.Orchestrator.bat`.
- Any code under `src/Dorc.Kafka.*/`.

---

## 3. Branch + commit strategy

- `feat/kafka-migration` integration branch.
- One or two commits: one for the `Program.cs` line + test; a second if a README note is added.
- Do not amend merged commits; do not skip hooks.

---

## 4. Requirements

### R1 — DI composition contract

`src/Dorc.Monitor/Program.cs` must, after `var builder = Host.CreateApplicationBuilder(args);` and before `var app = builder.Build();` (equivalent host-build point), call `builder.Services.AddDorcKafkaAvro(configurationRoot);`. The call should be placed in the existing Kafka-DI region adjacent to `AddDorcKafkaDistributedLock` / `AddDorcKafkaRequestLifecycleSubstrate`. Call order relative to the other two is not correctness-critical — `AddDorcKafkaAvro` uses `RemoveAll<IKafkaSerializerFactory>()` + `AddSingleton<IKafkaSerializerFactory, AvroKafkaSerializerFactory>()` precisely so call ordering is immaterial — but for readability, placing all three `AddDorcKafka*` calls contiguously is encouraged.

### R2 — DI-composition regression test (minimum-fix, reuses existing test file)

The existing test file `src/Dorc.Kafka.Events.Tests/DependencyInjection/KafkaAvroServiceCollectionExtensionsTests.cs` already covers the Avro-factory replacement contract comprehensively (before/after call order with `AddDorcKafkaClient`, idempotence, in/out-of-scope serializers, schema-registry resolution). **Delivery must NOT create a parallel Monitor-trio test that duplicates this coverage.**

The genuine regression-closure gap is the **negative case**: the existing test suite does not assert that without `AddDorcKafkaAvro` the resolved factory is `DefaultKafkaSerializerFactory`. That is the exact regression S-016 closes. Add one new `[TestMethod]` to the existing file:

- **Name (guidance only):** a negative-case sibling of `AddDorcKafkaAvro_AfterClient_ReplacesDefaultFactoryWithAvro` — effectively `WithoutAvroCall_ResolvesToDefaultFactory`.
- **Shape:** register only `AddDorcKafkaClient(ValidConfig())` (no `AddDorcKafkaAvro`), build the service provider, resolve `IKafkaSerializerFactory`, assert it is exactly `DefaultKafkaSerializerFactory`.
- **Size:** ≤15 lines including arrange/act/assert. Reuse the existing `BuildBaseServices()` / `ValidConfig()` helpers in that file.

**A Monitor-trio composition test (Lock + RequestLifecycle + Avro in Monitor's order) is not required** — neither `AddDorcKafkaDistributedLock` nor `AddDorcKafkaRequestLifecycleSubstrate` touches `IKafkaSerializerFactory` registration, so the call-order permutations already in the existing test suite cover the substantive behaviour. A Monitor-specific composition test would add no additional assertion value.

Test placement is unambiguously `src/Dorc.Kafka.Events.Tests/DependencyInjection/KafkaAvroServiceCollectionExtensionsTests.cs` as a new `[TestMethod]` sibling.

### R3 — Verification intent

- Build succeeds with no new warnings attributable to the change.
- All existing tests pass; the new test(s) pass.
- Local smoke test: Monitor running against a compose-broker-backed test environment consumes an Avro-encoded `DeploymentRequestEventData` produced by the API and reaches its expected handler without null-deserialisation or primitive-type errors.
- Runtime log observation: on Monitor startup, log emission from `AvroKafkaSerializerFactory` (if instrumented) appears; absence of it, or presence of `DefaultKafkaSerializerFactory` log lines, indicates S-016 is incomplete.

### R4 — Documentation

- No IS edit beyond marking S-016 done in §6 (no step-table or §3 content changes).
- `docs/kafka-migration/SPEC-S-010-Cutover-Runbook.md` R-1 smoke-test catalogue may gain an explicit "Monitor consumes one Avro-encoded Request-lifecycle event" bullet — Delivery judges whether this adds signal over the existing end-to-end deployment-request smoke test.

### R5 — Tests and invariants

- `dotnet test src/Dorc.Kafka.Events.Tests/` (or the chosen home) passes with the new test included.
- Full Kafka test suite (`dotnet test src/Dorc.Kafka.*` from repo root) passes.

---

## 5. Acceptance criteria

| # | Criterion | Verifier |
|---|---|---|
| AC-1 | `src/Dorc.Monitor/Program.cs` contains exactly one `AddDorcKafkaAvro(configurationRoot)` call in the Kafka-DI region | diff review + grep |
| AC-2 | Existing positive-case coverage (`AddDorcKafkaAvro_AfterClient_ReplacesDefaultFactoryWithAvro` and siblings in `KafkaAvroServiceCollectionExtensionsTests.cs`) continues to pass — S-016 adds no regression | `dotnet test` passes |
| AC-3 | New `[TestMethod]` in the same file: with only `AddDorcKafkaClient` registered (no `AddDorcKafkaAvro` call), resolving `IKafkaSerializerFactory` returns `DefaultKafkaSerializerFactory` | `dotnet test` passes |
| AC-4 | No change to any `.wxs`, `appsettings.json`, or `Setup.Dorc.msi.json` file | git diff |
| AC-5 | A Delivery-engineer-observed end-to-end run: Monitor process launched locally against compose broker + Karapace registry + API process producing one Avro-encoded `DeploymentRequestEventData`, Monitor deserialises without `ConsumeException`. **Observation procedure recorded in the PR description** (exact compose commands, Monitor run command, API trigger command, expected log line). `docs/kafka-migration/README-local-dev.md` may be extended at Delivery's discretion if the steps are reusable; amendment is NOT an S-016 gate. | PR description review |
| AC-6 | Adversarial spec review + code review: both unanimous | review-panel transcripts in §11 |

---

## 6. Risks and mitigations

| Risk | Severity | Mitigation |
|---|---|---|
| `AddDorcKafkaAvro` called before `AddDorcKafkaDistributedLock` might register the Avro factory only to have the lock extension's transitive `AddDorcKafkaClient` re-register the no-op default on top (if the marker-singleton idempotence guard is not ordering-safe) | MEDIUM | `AddDorcKafkaAvro` uses `RemoveAll<IKafkaSerializerFactory>()` + fresh `AddSingleton` — call-order-immune by design (verified in the extension's current code). The DI-composition test in R2 empirically validates this by asserting the resolved factory's concrete type, catching any regression if the extension's implementation changes. |
| Integration test to assert "Monitor deserialises Avro" requires a compose broker + registry — may regress CI time | LOW | Keep R2 test as pure DI-composition (no broker, no network). The end-to-end smoke test in R3 is a local-operator step, not a CI step. |
| `AddDorcKafkaAvro` itself has validation that might fail Monitor startup with a SchemaRegistry issue if S-015 has not landed yet | MEDIUM | Per `AddDorcKafkaAvro` code: `SchemaRegistry.Url` is checked lazily on first serialize — Monitor startup completes even without a URL. S-016 does not depend on S-015 closing first; dependency is functional (end-to-end Avro) not startup (host-start). IS §3 S-016 correctly declares S-016 independent of S-014/S-015. |
| Extension calls `services.RemoveAll<IKafkaSerializerFactory>()` — if the Monitor hosts additional code that also registers its own factory (e.g. a test-specific stub), S-016 silently removes it | LOW | Monitor does not currently register any custom `IKafkaSerializerFactory`. Future tests/harnesses would need to handle registration-order explicitly, which is a pattern the Avro extension already documents. Not a risk to production composition. |
| Delivery engineer places the new call in the wrong region (e.g. mixed in with unrelated Monitor DI) and readability suffers | LOW | R1 guidance names the correct region; code review panel will flag placement drift. |

---

## 7. Delivery phase guidance (non-prescriptive)

- The `Program.cs` edit is one line. Place it adjacent to the existing Kafka DI lines (`AddDorcKafkaDistributedLock` / `AddDorcKafkaRequestLifecycleSubstrate`) and preserve the SPEC-S-009 comment-block structure for reviewer traceability.
- The test is ≤15 lines in the existing `KafkaAvroServiceCollectionExtensionsTests.cs`. Over-engineering (replica Monitor harness, mocked host-builder, injected config providers) is against CLAUDE.md §2 Fix Scope Discipline.

---

## 8. Review scope notes for the adversarial panel

Reviewers of this spec should evaluate:

- Whether one call is genuinely the minimal fix, or whether Monitor needs additional DI (e.g. producer registrations the API has but Monitor doesn't).
- Whether the risk table accurately captures call-order behaviour of `AddDorcKafkaAvro`.
- Whether the test strategy (R2) catches the exact regression a future engineer removing the Avro call would introduce.
- Whether the independence-from-S-015 claim is factually correct (lazy validator, not startup validator).

Reviewers should NOT evaluate:

- `AddDorcKafkaAvro` internal implementation — accepted as-is from S-007's approvals.
- `DefaultKafkaSerializerFactory` contract — accepted from S-002.
- Schema-registry URL plumbing — S-015 territory.

---

## 9. Accepted risks from approved documents

| Risk Summary | Citation |
|---|---|
| `AvroKafkaSerializerFactory` is the canonical factory; the `DefaultKafkaSerializerFactory` exists only as a no-op placeholder until a real factory is registered | `src/Dorc.Kafka.Client/DependencyInjection/KafkaClientServiceCollectionExtensions.cs` (approved under S-002) |
| `AddDorcKafkaAvro` is idempotent and call-order-immune via marker singleton + `RemoveAll` | S-007 approvals + SPEC-S-007 |
| S-011 blocked by S-016 closure | IS-Kafka-Migration.md §3 S-016 "Blocking" clause |

---

## 10. Unknowns Register

| ID | Description | Owner | Blocking | Status |
|---|---|---|---|---|
| U-S016-1 | Whether there is a canonical Monitor test project at `src/Dorc.Monitor.Tests/` vs. placing the DI test in `src/Dorc.Kafka.Events.Tests/` | author | No | **RESOLVED (R1 triage):** Test placement is unambiguously `src/Dorc.Kafka.Events.Tests/DependencyInjection/KafkaAvroServiceCollectionExtensionsTests.cs` as a new `[TestMethod]` sibling. `src/Dorc.Monitor.Tests/` exists but hosts request-processor unit tests, not DI-composition tests. |
| U-S016-2 | Whether any Monitor-specific code path registers a custom `IKafkaSerializerFactory` that `AddDorcKafkaAvro` would `RemoveAll` | author | No | Grep-verifiable at Delivery time; Risk table already flags as LOW |

No blocking unknowns. Ready for Adversarial Review.

---

## 11. Review History

### R1 (2026-04-20) — REVISION REQUIRED

Panel (simulated personas): Claude Sonnet 4.6, Gemini Pro 3.1, GPT 5.3-codex.

| Verdict | |
|---|---|
| Sonnet | APPROVE WITH MINOR (4 LOW) |
| Gemini | APPROVE WITH MINOR (1 MEDIUM, 2 LOW) |
| GPT-codex | **REVISION REQUIRED** (1 HIGH, 2 MEDIUM, 3 LOW) |

Aggregate: **REVISION REQUIRED** (GPT F1 HIGH on test redundancy).

| ID | Reviewer | Severity | Finding | Disposition |
|---|---|---|---|---|
| GPT-F1 | GPT | **HIGH** | R2 DI-composition test duplicates existing coverage in `KafkaAvroServiceCollectionExtensionsTests.cs` (8 tests: before/after call order, idempotence, SchemaRegistry resolution, scope serializers). Monitor-trio replica adds no assertion value since neither Lock nor RequestLifecycle touches `IKafkaSerializerFactory`. The real gap is a single negative-case test asserting "without `AddDorcKafkaAvro`, factory resolves to `DefaultKafkaSerializerFactory`." | **Accept.** R2 rewritten: mandates a single negative-case `[TestMethod]` in the existing file (≤15 lines); explicitly forbids parallel Monitor-trio test. Test placement now unambiguous. |
| Gemini-G1 | Gemini | MEDIUM | §1 Motivation failure-mode description "null-valued payloads OR primitive-deserialisation exception" is imprecise. Actual path: `DefaultKafkaSerializerFactory.GetValueDeserializer` returns null → consumer skips `SetValueDeserializer` → Confluent.Kafka falls back to primitive deserialiser → deterministic `ConsumeException` because `DeploymentRequestEventData` is not a primitive. | **Accept.** §1 rewritten with the exact null-propagation path and a "deterministic `ConsumeException`" statement. |
| GPT-F2 | GPT | MEDIUM | AC-3 wording ambiguous about "replica DI composition with Avro removed" — the honest shape is `AddDorcKafkaClient` alone → `DefaultKafkaSerializerFactory`. | **Accept.** AC-3 now names the concrete test shape: "with only `AddDorcKafkaClient` registered (no `AddDorcKafkaAvro` call)." |
| GPT-F3 | GPT | MEDIUM | AC-5 unsatisfiable by cited `README-local-dev.md` — there is no Monitor+API local-run harness there. Compose brings up Kafka+Karapace only. | **Accept.** AC-5 rewritten: Delivery-engineer-observed end-to-end run with observation procedure recorded in the PR description; README amendment optional, not gating. |
| Sonnet-F1 | Sonnet | LOW | R2 permits either test home; Monitor test project hosts request-processor tests, not DI. | **Accept.** R2 now unambiguously names `Dorc.Kafka.Events.Tests` as the single test home; subsumed by GPT-F1's full rewrite. |
| Sonnet-F2 | Sonnet | LOW | R2 "trio of DI extensions in the same order" needs context that only Lock transitively registers the no-op factory. | **Subsumed by GPT-F1.** Trio replica no longer required; the concern is moot. |
| Sonnet-F4 | Sonnet | LOW | §7 "file-comparison test at AppSettingsTemplateShapeTests level" is a category error for DI wiring. | **Accept.** §7 red-herring sentence removed. |
| GPT-F4 | GPT | LOW | Same red-herring sentence in §7. | **Subsumed by Sonnet-F4.** |
| Sonnet-F3 | Sonnet | LOW | Risk row 3 (S-015 independence) could be more precise about failure-timing (runtime consume vs. startup). | **Defer to Delivery** — informational precision; spec correctness unaffected. Delivery engineer can verify the consumer does not eagerly resolve schema-registry client at host-start. |
| Gemini-G2 | Gemini | LOW | `SchemaRegistry.Url` lazy check assertion rests on a property of `AvroKafkaSerializerFactory` not demonstrated in the spec. | **Defer to Delivery** — verification at Delivery/test time; corrects by inspection. |
| Gemini-G3 | Gemini | LOW | Test placement distance from `Program.cs`. | **Defer to Delivery** — placement chosen; naming convention tied to S-016 makes future reverse-linking traceable. |
| GPT-F5 | GPT | LOW | "before `var app = builder.Build();`" imprecision (Monitor has no `app` variable). | **Defer to Delivery** — engineer will see the correct host-build shape on inspection. |
| GPT-F6 | GPT | LOW | U-S016-1 already resolvable by inspection. | **Accept.** U-S016-1 marked RESOLVED in §10 with the chosen test home. |

Status: `IN REVIEW (R1)` → `REVISION` → `IN REVIEW (R2)` after surgical edits.

### R2 (2026-04-20) — UNANIMOUS APPROVAL

Same panel, R2+ scoped per CLAUDE.md §4.

| Reviewer | Verdict | Notes |
|---|---|---|
| Sonnet | APPROVE | GPT F1 HIGH closure verified (single-test rewrite, Monitor-trio forbidden); all R1 fixes proportional; no regressions. |
| Gemini | APPROVE | G-1 MEDIUM failure-mode precision verified; GPT F1 cross-reviewer HIGH proportionally fixed. No regressions. |
| GPT-codex | APPROVE | All 6 own R1 findings verified closed or properly deferred (F5 Defer-to-Delivery); no unchanged-text mining. |

**Unanimous clean approval.** Spec transitions to `APPROVED`. User auto-pilot satisfies the user-approval gate.

### Code Review R1 (2026-04-20) — UNANIMOUS APPROVE (zero findings)

Panel (S-016 is a narrow two-file change, 2-reviewer panel per CLAUDE.md §4 minimum): Sonnet 4.6, Gemini Pro 3.1. Reviewed `src/Dorc.Monitor/Program.cs` + `src/Dorc.Kafka.Events.Tests/DependencyInjection/KafkaAvroServiceCollectionExtensionsTests.cs` against SPEC R1, R2, AC-1..AC-4.

| Reviewer | Verdict | Notes |
|---|---|---|
| Sonnet | APPROVE | AC-1..AC-4 satisfied; AC-5 correctly defers to PR description; AC-6 served by this panel. Monitor-trio duplicate test correctly absent. Test body ≤15 lines code, proportional. Above-average test hygiene (regression-trigger comment). |
| Gemini | APPROVE | Build 0 warnings / 0 errors. Filtered test 1/1 pass; full suite 9/9 pass (8 existing + 1 new). Comment block explains the exact defect path (null-returning factory → skipped SetValueDeserializer → Confluent primitive fallback → ConsumeException). Test assertion robust against regression. |

**Zero findings at any severity.** Code diff satisfies all ACs. Ready to commit.
