# SPEC-S-017 — Configurable Kafka topic names

| Field | Value |
|---|---|
| **Status** | APPROVED — unanimous round-2 panel verdict 2026-04-27: Sonnet APPROVE WITH MINOR / Opus APPROVE WITH MINOR / Haiku APPROVE WITH MINOR. Convergent MEDIUM (schema-gate dual-path mechanism) addressed in R1.1; remaining LOW items deferred to Delivery per round-2 triage. |
| **Author** | Claude (Opus 4.7 1M) |
| **Created** | 2026-04-27 |
| **Governing HLPS** | `HLPS-Kafka-Migration.md` (APPROVED, R3) |
| **Governing IS** | `IS-Kafka-Migration.md` (APPROVED through R4 — this SPEC's closure depends on an IS R5 revision panel approval that adds the S-017 row) |
| **Step ID** | S-017 |
| **Branch** | `feat/kafka-migration` (single-PR strategy) |

> **Round-1 panel summary (2026-04-27):** Sonnet REQUEST REVISION, Opus APPROVE WITH MINOR, Haiku REQUEST REVISION. Triage applied per author memo: subject-name decoupling (original R2 / R5 / R6) deleted as YAGNI; commit order reordered to put lock-rebinding first; call-site table expanded; snapshot-directory roles disambiguated; `tools/snapshot-schemas` argument-bridge promoted from delivery-guidance to a numbered requirement; AC-7 (SEFE-cluster smoke) moved to S-010 dry-run.
>
> **Round-2 panel summary (2026-04-27):** Sonnet APPROVE WITH MINOR, Opus APPROVE WITH MINOR, Haiku APPROVE WITH MINOR — unanimous. All nine R0 findings closed. Convergent MEDIUM finding (Sonnet NF-01 / Opus F2-1 / Haiku F-10): schema-gate dual-path mechanism — POST under deployed subject vs read snapshot under default-derived filename — was asserted in R1 §2 #4 but not operationalized in the gate's signature. R1.1 addresses by specifying the `InScopeSchemas()` tuple change in §4 R2 and adding a diverged-name test fixture to AC-7 (now AC-13). LOW findings (commit-ordering nits, generate-schemas parity, lock-topic grep AC, validator safety note) addressed inline; remaining commit-ordering nits deferred to Delivery.

---

## 1. Motivation

The four Kafka topics the DOrc substrate produces to / consumes from are referenced from .NET code in two different shapes:

1. **`Kafka:Locks:Topic`** — already bound onto `KafkaLocksOptions.Topic` (default `dorc.locks`); fully configurable today (S-005b).
2. **`dorc.requests.new`, `dorc.requests.status`, `dorc.results.status`** — hard-coded as `public const string` in `src/Dorc.Kafka.Events/KafkaSubjectNames.cs`. Used by the publisher, both consumers, the topic provisioner, the Avro schema gate, the schema-snapshot tools, and ~40 test references.

Schema-Registry **subjects** are derived from those same constants by string concatenation (`<topic>-value`). The committed `.avsc` snapshot files under `docs/kafka-migration/schemas/{current,latest}/` are filenamed accordingly (`dorc.requests.new-value.avsc`, etc.).

The Aiven-hosted non-prod cluster provisioning request (Apr 2026) issued the topics under the SEFE enterprise-naming convention:

- `tr.dv.gbl.deploy.locks.il2.dorc` (replaces `dorc.locks`)
- `tr.dv.gbl.deploy.request.il2.dorc` (replaces `dorc.requests.new`)
- `tr.dv.gbl.deploy.requeststatus.il2.dorc` (replaces `dorc.requests.status`)
- `tr.dv.gbl.deploy.resultstatus.il2.dorc` (replaces `dorc.results.status`) — fourth-topic Aiven request submitted 2026-04-27.

All four names are SEFE-standards-compliant per the user's 2026-04-27 confirmation.

With the topics provisioned under the new naming convention, the current binary will not function on this broker:

- The lock topic flips via a single `appsettings.json` override (already supported).
- The other three topics are baked into compiled `const` strings; without code changes the publisher writes to `dorc.requests.new`/`dorc.requests.status`/`dorc.results.status`, which do not exist; the auto-provisioner is denied creation by ACL (`TopicAuthorizationFailed` already observed in production logs as of `MonitorNonProd_20260426.log`).
- The Schema-Registry subjects similarly need to follow the new topic names (`tr.dv.gbl.deploy.request.il2.dorc-value` etc.) under Confluent's default `TopicNameStrategy`.

S-017 closes this gap by removing topic names from compiled code and surfacing them as bound configuration, with installer support so per-environment overrides land via the existing MSI deploy-variables flow. The `<topic>-value` Schema-Registry subject derivation stays internal to the codebase — Confluent's default `TopicNameStrategy` matches the SEFE Karapace convention, so no separate subject-name configuration surface is justified at this time. (Original draft included a subject-decoupling layer; round-1 panel correctly classified that as YAGNI and it has been removed.)

---

## 2. Scope

### In scope

1. New options class `KafkaTopicsOptions` bound to `Kafka:Topics:*` carrying the four topic names (`Locks`, `RequestsNew`, `RequestsStatus`, `ResultsStatus`).
2. Removal of `public const string` topic and `-value` subject literals from `src/Dorc.Kafka.Events/KafkaSubjectNames.cs`. The class is deleted entirely; defaults live as property initializers on `KafkaTopicsOptions` (single canonical home).
3. All call-sites listed in §4 R2 read topic names through DI (`IOptions<KafkaTopicsOptions>`), not from a constant. Schema-Registry subject names are derived internally as `<topic>-value` at the call-site (or via a small private helper inside `Dorc.Kafka.Events`) — not via a public DI-resolved service.
4. Schema-snapshot files keep their environment-neutral filenames (`dorc.requests.new-value.avsc`, etc.). The schema gate's existing two-directory split is preserved — `docs/kafka-migration/schemas/current/` (canonical-equality test) and `docs/kafka-migration/schemas/latest/` (registry-fallback) — with both directories' filenames keyed off `KafkaTopicsOptions` *defaults*, not the deployed topic names. Run-time resolution: schema gate POSTs to live registry under the deployed subject (`<deployed-topic>-value`), but reads the canonical/snapshot file from disk under the default-derived filename. This decouples committed-contract filename from deployed-subject string with zero new public surface.

   **Mechanism (added R1.1 per round-2 convergent finding):** Today `AvroSchemaGate.CheckSubjectAsync(subject, …)` uses a single `subject` string both as the live-registry POST path and as the on-disk filename. The gate's internal `InScopeSchemas()` enumerator returns `(subject, schema)` pairs. R1.1 changes the enumerator to return `(CanonicalKey, LiveSubject, Schema)` triples where `CanonicalKey` is `<default-topic>-value` (used for `Path.Combine(_canonicalDir, $"{CanonicalKey}.avsc")` and the `_snapshotDir` equivalent) and `LiveSubject` is `<deployed-topic>-value` (used for the `/compatibility/subjects/{LiveSubject}/versions/latest` POST). Both directories — `current/` and `latest/` — are addressed via `CanonicalKey` only. This is the only signature change in the gate; the rest of its logic is unchanged. The change is internal — `AvroSchemaGate`'s public surface (one `CheckSubjectAsync` method) does not gain a new parameter.
5. Locks topic moves from `Kafka:Locks:Topic` to `Kafka:Topics:Locks`. The remaining `KafkaLocksOptions` class persists with its non-topic properties (`Enabled`, `PartitionCount`, `ReplicationFactor`, `ConsumerGroupId`, `LockWaitDefaultTimeoutMs`) bound to `Kafka:Locks:*`. Backwards compatibility for the old `Kafka:Locks:Topic` is **not** preserved (per project memory `project_kafka_branch_dev_status` — branch has not left dev).
6. `appsettings.json` template updates for `src/Dorc.Api/` and `src/Dorc.Monitor/` — root-level `Kafka:Topics:*` block with the historical default names (`dorc.requests.new`, etc.) so a fresh dev clone runs against a local broker without any per-env override.
7. Installer alignment: `<Json:JsonFile>` writes for the four topic names in both Monitor installers (`NonProdActionService.wxs`, `ProdActionService.wxs`) and `RequestApi.wxs` non-prod + prod components, sourced from new MSI properties registered in `Setup.Dorc.msi.json` and threaded through `Install.Orchestrator.bat`.
8. Validator: `KafkaTopicsOptionsValidator` enforces non-empty for all four topic names. **No format restriction** is imposed — SEFE topic names contain dots (`tr.dv.gbl.deploy.request.il2.dorc`) and any character allowlist would reject them. Non-empty is the only constraint.
9. `tools/snapshot-schemas` and `tools/generate-schemas` accept subject names via CLI argument (or config-file read of the same `Kafka:Topics:*` block). Defaults match `KafkaTopicsOptions` defaults so a no-arg local run is unchanged. **Mandatory** because S-010 dry-run runs `tools/snapshot-schemas` against the SEFE Karapace; no-arg invocation would register schemas under wrong subjects. `tools/generate-schemas` accepts the same argument surface for parity, even though it does not POST to a live registry — symmetry is cheaper than divergence and the tool is dev-time-only.
10. Test updates: all `KafkaSubjectNames.X` test references migrate to `IOptions<KafkaTopicsOptions>` test doubles. Existing AT-1..AT-N references in S-006/S-007 SPECs continue to assert the same wire-level behaviour against whatever topic name the test fixture supplies.

### Out of scope

- Public-surface subject-name decoupling (separate `Kafka:Schemas:Subjects:*` config block, `IKafkaSubjectResolver` DI service). Internal `<topic>-value` derivation is sufficient given Confluent's `TopicNameStrategy` matches the SEFE Karapace convention. If a future Schema Registry adopts a non-default subject-name strategy, that change is additive and small.
- Changing the Avro schema **content**. S-003's contracts stand. The schema gate continues to reject schema-content drift.
- Renaming the four .avsc snapshot files in `docs/kafka-migration/schemas/{current,latest}/`. They keep their current names; the gate keys them off `KafkaTopicsOptions` defaults regardless of deployed topic names.
- Multi-environment runtime selection (one binary serving multiple Kafka clusters with different topic naming). Out of scope — config is per-deploy.
- Production cutover. S-011 is unchanged; it just gains an additional MSI property surface.
- Live-cluster smoke against the SEFE Aiven non-prod cluster. **Moved to S-010 dry-run** (per round-1 panel triage) — S-017 closes on local-compose smoke (AC-6) only, removing the synchronous dependency on Aiven topic provisioning timing.

### Explicitly not touched

- DI extension method names and shapes (`AddDorcKafkaResultsStatusSubstrate`, `AddDorcKafkaRequestLifecycleSubstrate`, `AddDorcKafkaDistributedLock`). Only their internals change.
- Any consumer-group identity logic (S-006 + S-007 own those shapes; topic name is independent — verified during round-1 review).
- Lock partition-count / replication-factor immutability (ADR-S-005 §4 #2). Those remain operational invariants.

---

## 3. Branch + commit strategy

- Work on `feat/kafka-migration`.
- Commit granularity: small commits per concern. Order:
  1. **`KafkaTopicsOptions` + validator + DI registration + lock-substrate re-bind in one atomic commit.** The `KafkaLocksOptions.Topic` property is deleted in this same commit and the lock substrate's call-sites (`KafkaLockCoordinator`, `KafkaDistributedLockService`, `KafkaLocksTopicProvisioner`) flip to read `KafkaTopicsOptions.Locks` here — preventing an intermediate window where any commit references the deleted property. The round-1 panel flagged the original commit ordering as compile-error-vulnerable; this revision collapses three steps into one atomic step.
  2. Migrate publisher + topic provisioner + both consumers + schema gate to read `IOptions<KafkaTopicsOptions>` and derive `<topic>-value` internally. Delete `KafkaSubjectNames.cs`.
  3. Migrate `tools/generate-schemas` + `tools/snapshot-schemas` to accept topic names as CLI arguments (default to `KafkaTopicsOptions` defaults).
  4. Test fixture updates (publisher tests, schema-gate tests, integration-test harness, validator tests).
  5. `appsettings.json` template updates (API + Monitor).
  6. Installer WiX updates + `Setup.Dorc.msi.json` MSI-property registrations + `Install.Orchestrator.bat` threading.
- Do not skip commit hooks. Do not amend merged commits.

---

## 4. Requirements

### R1 — `KafkaTopicsOptions` is the single home for topic names

`src/Dorc.Kafka.Events/Configuration/KafkaTopicsOptions.cs`:

```
public sealed class KafkaTopicsOptions
{
    public const string SectionName = "Kafka:Topics";
    public string Locks { get; set; } = "dorc.locks";
    public string RequestsNew { get; set; } = "dorc.requests.new";
    public string RequestsStatus { get; set; } = "dorc.requests.status";
    public string ResultsStatus { get; set; } = "dorc.results.status";
}
```

Defaults match the historical hard-coded values so a fresh dev clone with no `Kafka:Topics` overrides behaves identically to today. **Property initializers are the single canonical home for default values** — no separate `KafkaTopicsDefaults` static class is introduced (per round-1 panel triage, F-7 / F-8).

A `KafkaTopicsOptionsValidator : IValidateOptions<KafkaTopicsOptions>` enforces **non-empty only** for all four properties at host startup. **No character-set, length, or format restriction** is imposed — SEFE topic names (`tr.dv.gbl.deploy.request.il2.dorc`) deviate from the `dorc.*` default pattern and any tighter validator would reject them. The check is `string.IsNullOrWhiteSpace(value) → fail`.

Validator-guaranteed safety assumption: any code that iterates over `KafkaTopicsOptions` properties (e.g. `KafkaResultsStatusTopicProvisioner` enumerating the four topic names for batch creation) may treat each value as non-null and non-empty without runtime defensive checks — startup validation is the single guard. Empty values are unreachable post-`IHost.StartAsync()`.

`KafkaLocksOptions.Topic` is deleted; the lock substrate reads its topic via `IOptions<KafkaTopicsOptions>.Locks`. The remaining `KafkaLocksOptions` class persists, bound to `Kafka:Locks:*`, with properties `Enabled`, `PartitionCount`, `ReplicationFactor`, `ConsumerGroupId`, `LockWaitDefaultTimeoutMs` (the non-topic concerns S-005b owns).

### R2 — Call-site migration

The following files inject `IOptions<KafkaTopicsOptions>` and remove direct `KafkaSubjectNames.*` references:

| File | Today | After |
|---|---|---|
| `src/Dorc.Kafka.Events/KafkaSubjectNames.cs` | exists | **deleted** |
| `src/Dorc.Kafka.Events/Publisher/KafkaDeploymentEventPublisher.cs` | `KafkaSubjectNames.RequestsNewTopic` etc. | `_topics.RequestsNew` etc. |
| `src/Dorc.Kafka.Events/Publisher/KafkaResultsStatusTopicProvisioner.cs` | hard-coded triple | iterates `_topics` properties |
| `src/Dorc.Kafka.Events/Publisher/DeploymentRequestsKafkaConsumer.cs` | `Subscribe(new[] { RequestsNewTopic, RequestsStatusTopic })` | `Subscribe(new[] { _topics.RequestsNew, _topics.RequestsStatus })` |
| `src/Dorc.Kafka.Events/Publisher/DeploymentResultsKafkaConsumer.cs` | `TopicName = ResultsStatusTopic` (init default) | constructor sets from `_topics.ResultsStatus` |
| `src/Dorc.Kafka.Events/SchemaGate/AvroSchemaGate.cs` | `InScopeSchemas()` returns `(subject, schema)` pairs; single string used for both file path and registry POST | `InScopeSchemas()` returns `(CanonicalKey, LiveSubject, Schema)` triples — `CanonicalKey = <default-topic>-value` drives both `current/` and `latest/` file lookups; `LiveSubject = <deployed-topic>-value` drives the `/compatibility/subjects/{LiveSubject}/versions/latest` POST. Defaults read from `KafkaTopicsOptions` initializer values; deployed values read from `IOptions<KafkaTopicsOptions>` |
| `src/Dorc.Kafka.Lock/KafkaLocksTopicProvisioner.cs` | reads `KafkaLocksOptions.Topic` | reads `KafkaTopicsOptions.Locks` |
| `src/Dorc.Kafka.Lock/KafkaLockCoordinator.cs` | `_locksOptions.Topic` | `_topics.Locks` |
| `src/Dorc.Kafka.Lock/KafkaDistributedLockService.cs` | same | same |
| `src/Dorc.Kafka.Lock/Configuration/KafkaLocksOptions.cs` | `Topic` property + `SectionName = "Kafka:Locks"` | `Topic` property removed; class otherwise unchanged |
| `src/Dorc.Kafka.Lock/Configuration/KafkaLocksOptionsValidator.cs` | validates `Topic` non-empty | `Topic` validation removed; remaining property validations stand |
| `tools/generate-schemas/Program.cs` | `KafkaSubjectNames.*Value` constants | CLI args / config-file read (mandatory for non-default registries) |
| `tools/snapshot-schemas/Program.cs` | `KafkaSubjectNames.*Value` constants | CLI args / config-file read **(mandatory; load-bearing for S-010 dry-run)** |
| `src/Dorc.Kafka.Events.Tests/Schemas/DorcEventSchemasTests.cs` | `Assert.AreEqual("dorc.requests.new-value", KafkaSubjectNames.RequestsNewValue)` and reads from canonical path | rewritten: assert `<topic>-value` derivation against `KafkaTopicsOptions` defaults; canonical-equality test reads from `docs/kafka-migration/schemas/current/<default-topic>-value.avsc` |
| `src/Dorc.Kafka.Events.Tests/SchemaGate/SchemaGateUnitTests.cs` | subject-string keys | `KafkaTopicsOptions`-defaults-derived subject strings via test-fixture options |
| `src/Dorc.Kafka.Events.Tests/Publisher/KafkaDeploymentEventPublisherTests.cs` | `Assert.AreEqual(KafkaSubjectNames.X, …)` | inject `Options.Create(new KafkaTopicsOptions { … })` test doubles; assert against the test-fixture's chosen value |
| `src/Dorc.Kafka.Events.IntegrationTests/Publisher/S007TestHarness.cs` | `topic ?? KafkaSubjectNames.ResultsStatusTopic` | `topic ?? new KafkaTopicsOptions().ResultsStatus` |
| `src/Dorc.Kafka.Events.IntegrationTests/Publisher/S006RequestLifecycleIntegrationTests.cs` | any direct constants | options-pattern fixture values |
| `src/Dorc.Kafka.Lock.Tests/KafkaLocksOptionsValidatorTests.cs` | tests `Topic` validation | `Topic` test cases removed; remaining property tests stand |

A repo-level CI assertion confirms migration completeness: a `git grep` for the regex `KafkaSubjectNames\.(RequestsNew|RequestsStatus|ResultsStatus)(Topic|Value)\b` against `src/` and `tools/` returns zero hits. (Specific regex per round-1 panel F-9.)

### R3 — `appsettings.json` templates carry `Kafka:Topics`

`src/Dorc.Api/appsettings.json` and `src/Dorc.Monitor/appsettings.json` gain a root-level block:

```
"Kafka": {
  …existing keys…,
  "Topics": {
    "Locks": "dorc.locks",
    "RequestsNew": "dorc.requests.new",
    "RequestsStatus": "dorc.requests.status",
    "ResultsStatus": "dorc.results.status"
  }
}
```

The Monitor template's existing `Kafka:Locks:Topic` key is removed (the lock substrate now reads `Kafka:Topics:Locks`). The Monitor template's other `Kafka:Locks:*` keys (`PartitionCount`, `ReplicationFactor`, `ConsumerGroupId`) stay where they are.

### R4 — Installer writes for topic names

`src/Setup.Dorc/Setup.Dorc.msi.json` registers four new MSI properties:

- `KAFKA.TOPICS.LOCKS`
- `KAFKA.TOPICS.REQUESTSNEW`
- `KAFKA.TOPICS.REQUESTSSTATUS`
- `KAFKA.TOPICS.RESULTSSTATUS`

`src/Setup.Dorc/Install.Orchestrator.bat` passes them through.

`src/Setup.Dorc/Web/RequestApi/RequestApi.wxs` (both `RequestApiComponent` and `RequestApiComponentProd`), `src/Setup.Dorc/Monitors/NonProd/NonProdActionService.wxs`, and `…/Prod/ProdActionService.wxs` add `<Json:JsonFile>` writes to `$.Kafka.Topics.{Locks,RequestsNew,RequestsStatus,ResultsStatus}` for the four MSI properties. The writes are unconditional (parallels existing Kafka writes in those WiX files; conditional `<Json:JsonFile>` was contemplated for subject overrides in the original draft and dropped with the subject-decoupling surface).

For the SEFE Aiven non-prod environment, the deploy-variables file populates:

```
KAFKA.TOPICS.LOCKS          = tr.dv.gbl.deploy.locks.il2.dorc
KAFKA.TOPICS.REQUESTSNEW    = tr.dv.gbl.deploy.request.il2.dorc
KAFKA.TOPICS.REQUESTSSTATUS = tr.dv.gbl.deploy.requeststatus.il2.dorc
KAFKA.TOPICS.RESULTSSTATUS  = tr.dv.gbl.deploy.resultstatus.il2.dorc
```

### R5 — Tests

- `KafkaTopicsOptionsValidatorTests` (new, in `src/Dorc.Kafka.Events.Tests/Configuration/`): rejects empty/whitespace for any of the four topics; accepts non-default value-set including dotted SEFE-shape names; pattern matches `KafkaLocksOptionsValidatorTests` (S-005b).
- Existing `DorcEventSchemasTests.KafkaSubjectNames_FollowConfluentValueSuffix` is rewritten to assert that internal `<topic>-value` derivation produces `"dorc.requests.new-value"` etc. given `KafkaTopicsOptions` defaults.
- Existing `KafkaDeploymentEventPublisherTests.PublishX_EmitsToYTopic` tests are rewritten to take topic strings from a test-fixture `Options.Create(new KafkaTopicsOptions { … })` rather than from `KafkaSubjectNames`.
- Schema-gate unit tests (`SchemaGateUnitTests`) replace constant subject-strings with options-derived values; behaviour is identical otherwise.
- Validator tests on `KafkaLocksOptionsValidator` lose their `Topic`-required cases.
- Repo-level grep assertion (R2 final paragraph) acts as a CI invariant. **Kept as a supplementary gate**, not the primary verification — the rewritten unit + integration tests above are the load-bearing checks.

### R6 — Documentation

- `docs/kafka-migration/IS-Kafka-Migration.md` — R5 revision section adds an S-017 row to the step table; §3 gains an S-017 detail entry. **This SPEC's APPROVED status is contingent on the IS R5 panel approval landing in the same revision cycle.** The R5 IS revision and this SPEC are reviewed together.
- `docs/kafka-migration/SPEC-S-006-Request-Lifecycle-Pubsub-On-Kafka.md` and `SPEC-S-007-Status-Event-Pubsub-On-Kafka.md` — single-paragraph footnote each indicating that topic names are now deploy-time configuration; the in-spec literal references stand as the **default** values.
- `docs/kafka-migration/SPEC-S-014-Installer-Config-Alignment.md` is **not** modified (S-014's MSI-property additions stand; S-017's are additive).
- `docs/kafka-migration/SPEC-S-010-Cutover-Runbook.md` (also `S-010-Cutover-Runbook.md`) — pre-flight catalogue gains: (a) "verify deploy-variables file populates `KAFKA.TOPICS.*` for the target environment; absent values cause services to start against `dorc.*` defaults that may not exist on the target broker"; (b) "live-cluster Kafka smoke (publish+consume against the four SEFE-named topics) — moved here from S-017 AC-7"; (c) "`tools/snapshot-schemas` is invoked with explicit `--topic-*` arguments matching the deploy-variables file before any service produces against the cluster."

---

## 5. Acceptance criteria

| # | Criterion | Verifier |
|---|---|---|
| AC-1 | `KafkaTopicsOptions` exists, is bound to `Kafka:Topics`, validator enforces non-empty (and only non-empty) | unit test + DI-config inspection |
| AC-2 | `git grep` for the regex `KafkaSubjectNames\\.(RequestsNew\|RequestsStatus\|ResultsStatus)(Topic\|Value)\\b` against `src/` and `tools/` returns zero hits | CI assertion |
| AC-3 | `src/Dorc.Kafka.Events/KafkaSubjectNames.cs` is deleted | repository inspection |
| AC-4 | `<Json:JsonFile>` writes for all four `KAFKA.TOPICS.*` MSI properties at root path `$.Kafka.Topics.*` appear in `RequestApi.wxs` (both components), `NonProdActionService.wxs`, `ProdActionService.wxs` | `git grep` |
| AC-5 | `Setup.Dorc.msi.json` registers the four `KAFKA.TOPICS.*` MSI properties; `Install.Orchestrator.bat` threads them through | `git grep` + manual inspection |
| AC-6 | Local-build `Dorc.Api` + `Dorc.Monitor` start cleanly against `appsettings.json` with no `Kafka:Topics` overrides (defaults flow through); a `DeploymentRequestEventData` round-trip succeeds against a local-compose Kafka broker | manual run against `docs/kafka-migration/README-local-dev.md` setup |
| AC-7 | Schema-gate continues to reject schema-content drift (S-003 invariant) | existing `SchemaGateUnitTests` pass after options-pattern migration |
| AC-8 | `tools/snapshot-schemas` invoked without arguments registers schemas under the historical `dorc.*` default subjects (parity with today); invoked with explicit `--topic-*` arguments registers under the supplied subjects | dev-time integration test against local Karapace |
| AC-9 | `KafkaLocksOptions.Topic` is deleted; lock substrate runs against `KafkaTopicsOptions.Locks`; no compile or test failure references the removed property | unit-test + repo grep |
| AC-10 | `git grep "KafkaLocksOptions\\.Topic"` against `src/` and `tools/` returns zero hits | CI assertion (added R1.1 per round-2 finding F2-3) |
| AC-11 | IS R5 revision adding S-017 is APPROVED by its own adversarial-review cycle | IS document status |
| AC-12 | Adversarial Review panel unanimously approves this SPEC | **MET** 2026-04-27 — round-2 panel unanimous APPROVE WITH MINOR |
| AC-13 | `SchemaGateUnitTests` includes at least one fixture where deployed topic name differs from default (e.g. `KafkaTopicsOptions.RequestsNew = "tr.dv.gbl.deploy.request.il2.dorc"`); the test asserts (a) the gate POSTs to live registry under `<deployed-topic>-value`, and (b) the canonical/snapshot file is read under `<default-topic>-value.avsc`. This verifies the dual-path mechanism at §2 #4 is not just compile-correct but behaviour-correct in the diverged-name case S-017 introduces | unit test (added R1.1 per round-2 convergent finding) |
| AC-14 | Adversarial Review panel unanimously approves the resulting code/WiX diff | review-panel output |

**Note:** the original draft's AC-7 (live SEFE-cluster smoke) has moved to S-010 dry-run per round-1 panel triage. Listed in `SPEC-S-010-Cutover-Runbook.md` pre-flight catalogue (R6 (b)).

---

## 6. Risks and mitigations

| Risk | Severity | Mitigation |
|---|---|---|
| Removing `KafkaLocksOptions.Topic` is a breaking config-key rename | ACCEPTED | `feat/kafka-migration` has not left dev (per memory `project_kafka_branch_dev_status`). Only field instances are dev/test boxes (e.g. DEPAPP03UT) which are redeployed with the post-S-017 MSI. No precedence-rule fallback is added — single canonical home is the intended end state |
| Fourth Aiven topic (`resultstatus`) provisioning lags the SPEC closure | LOW | User submitted the Aiven request on 2026-04-27 alongside this SPEC. **AC-7 (live-cluster smoke) has moved to S-010 dry-run**, so S-017 closure no longer blocks on Aiven topic provisioning timing |
| Dev-time tools (`tools/generate-schemas`, `tools/snapshot-schemas`) drift away from in-process defaults | LOW | A test asserts parity: tool default args = `KafkaTopicsOptions` default property values. Drift fails the test |
| Test-fixture migration introduces churn across ~40 references | LOW | Mechanical; covered by R5. Where many tests share the same default topic-name set, a single `KafkaTopicsOptionsTestDouble` helper reduces churn |
| `KafkaSubjectNames` deletion breaks integration test harnesses (`S007TestHarness`, `S006RequestLifecycleIntegrationTests`) before R2's full migration lands | LOW | R2 lists every harness explicitly; the deletion commit (commit 2 in §3 ordering) lands all migrations in the same atomic step. No intermediate red-build window |
| Schema-snapshot canonical-equality test (`DorcEventSchemasTests`) reads from `docs/kafka-migration/schemas/current/` while snapshot-fallback reads from `docs/kafka-migration/schemas/latest/` — two paths, one gate's coherence depends on both | LOW | R2's call-site row for `AvroSchemaGate.cs` explicitly preserves both directories' filename conventions, keyed off `KafkaTopicsOptions` defaults. Round-1 panel F-2 surfaced this; this revision documents both directories' roles in §2 #4 |

---

## 7. Delivery phase guidance (non-prescriptive)

- The `KafkaSubjectNames` file is deleted entirely. Defaults live as property initializers on `KafkaTopicsOptions`. (Round-1 panel triage F-7 / F-8 — single canonical home.)
- The `<topic>-value` Schema-Registry subject convention is internal to `Dorc.Kafka.Events`. If a private static helper is the cleanest expression, prefer it over inline string concatenation at every call-site. The helper stays internal — no public DI surface.
- The `tools/*` programs accept either a `--topic-locks=…` style argument set or a `--config=appsettings.json` argument that loads the same `Kafka:Topics` block via `JsonNode`. Either is acceptable; whichever the Delivery engineer finds cleaner. The mandatory thing is that **the tool cannot register schemas under wrong subject names by default** when run against a non-default registry.
- WiX edits are mechanical and follow the S-014 / S-015 pattern. One `<Json:JsonFile>` per `KAFKA.TOPICS.*` property; no conditional writes.
- Capture the Aiven fourth-topic request `requestId` in the S-010 dry-run evidence directory once provisioning completes.
- Lock the commit-1 atomicity in §3 — adding `KafkaTopicsOptions.Locks` and removing `KafkaLocksOptions.Topic` and migrating all three lock call-sites must happen in a single commit. Splitting them creates a compile-error window the round-1 panel correctly flagged.

---

## 8. Review scope notes for the adversarial panel

Reviewers should evaluate:

- Whether the SPEC's revised scope (topic names only, subject-name decoupling deleted) cleanly addresses the SEFE-Aiven topic-naming need without leaving a gap.
- Whether the lock-substrate's config-key rename (`Kafka:Locks:Topic` → `Kafka:Topics:Locks`) is correctly reflected in every dependent file (call-sites, validator, validator tests, template, installer).
- Whether the in-scope/out-of-scope split is clean and traceable in this revision.
- Whether the round-1 panel findings are addressed in §2 / §3 / §4 / §6 and whether any are inadequately resolved.
- Whether `tools/snapshot-schemas` argument-bridge is correctly load-bearing — running this without explicit subjects against a non-default registry would silently misregister schemas.
- Whether the `current/` vs `latest/` snapshot-directory roles are documented sufficiently in §2 #4 and §6.

Reviewers should **NOT** evaluate:

- Whether the SEFE topic naming convention itself is correct. It is environmental input.
- Whether the four-topic decomposition (locks / requests.new / requests.status / results.status) is correct. S-005b/S-006/S-007 own that decomposition; S-017 only renames.
- The DI extension method shapes — those are owned by S-006/S-007/S-005b's prior approvals.
- Whether subject-name decoupling should *return* — that decision was the round-1 panel's most consequential triage and the revision honours it. Re-litigating belongs in a future spec if and when the Schema Registry strategy genuinely shifts.
- Pseudocode syntax / hypothetical diffs.

---

## 9. Accepted risks from approved documents

| Risk Summary | Citation |
|---|---|
| Single-PR strategy on `feat/kafka-migration` means S-017 lands as additional commits on top of an already-large integration branch | IS §1 — branch policy; memory `feedback_pr_strategy` |
| The S-010 dry-run is the first end-to-end exercise of installer-driven topic-name population — undiscovered installer/template gaps surface there, not earlier | IS-Kafka-Migration.md §6 R1 — pattern is identical to how S-014 surfaced |

---

## 10. Open questions

(None remaining — both round-0 questions resolved during R0 → R1 revision.)

---

## 11. Round-2 panel — minor findings deferred to Delivery

These items were classified LOW by the round-2 panel and the panel approved without requiring spec revision. Recorded here so Delivery picks them up:

| Finding | Source | Disposition |
|---|---|---|
| Commit ordering: §3 commit 5 (`appsettings.json` template updates) lands after commit 4 (test fixtures); integration tests that load templates directly may need fixtures injected, not file-based | Sonnet NF-02 | Defer — Delivery may merge templates earlier or inject test fixtures, no panel re-approval required |
| Commit-1 atomicity boundary: §3 collapse of options + lock-rebind + lock call-sites is correct; whether *test fixtures* for the new options must also be in commit 1 is a Delivery judgement (otherwise commit-1's tests don't run cleanly) | Opus F2-2 | Defer — Delivery decides whether commits 1–4 collapse further or stay as listed |

---

## 12. Resolved during authoring

| Question | Resolution | Round |
|---|---|---|
| Convention for `results.status` topic name (`resultstatus` single-token vs. `results.status` dot-preserved) | `resultstatus` — SEFE-standards compliant (matches `requeststatus`). User confirmation 2026-04-27 | R0 |
| Whether breaking the `Kafka:Locks:Topic` → `Kafka:Topics:Locks` rename without a precedence-rule fallback is acceptable | Acceptable. `feat/kafka-migration` has not left dev. User confirmation 2026-04-27 | R0 |
| Whether all four topics need to be requested from Aiven before S-017 closes | No. AC-7 (live-cluster smoke) moved to S-010 dry-run; provisioning timing decoupled from S-017 closure | R1 (panel triage) |
| Whether subject-name decoupling (separate config block + DI service) is justified | No. Confluent `TopicNameStrategy` matches Karapace convention; speculative surface deleted. Internal `<topic>-value` derivation is sufficient | R1 (panel triage) |
| Whether subject-override MSI properties / WiX writes are justified | No. Subject decoupling deleted; the deploy surface goes with it | R1 (panel triage) |
| Whether `KafkaTopicsDefaults` static class should hold defaults alongside `KafkaTopicsOptions` initializers | No. Property initializers are the single canonical default home. `KafkaTopicsDefaults` not introduced | R1 (panel triage) |
| Whether schema-snapshot filenames migrate to slot-key names (`requests-new.avsc`) or keep environment-neutral names (`dorc.requests.new-value.avsc`) | Keep environment-neutral. Slot-key indirection deleted alongside subject decoupling | R1 (panel triage) |
| Whether `tools/snapshot-schemas` argument-bridge is mandatory or delivery-discretionary | Mandatory. Tool is run in S-010 dry-run against SEFE Karapace; no-arg invocation would misregister schemas | R1 (panel triage) |
| Whether AC-2 grep regex specificity is sufficient | Specified as `KafkaSubjectNames\.(RequestsNew\|RequestsStatus\|ResultsStatus)(Topic\|Value)\b` — covers both `*Topic` and `*Value` siblings | R1 (panel triage) |
| Whether `KafkaTopicsOptionsValidator` imposes a topic-name format constraint | No. Non-empty only. SEFE topic names contain dots; format restriction would reject them | R1 (panel triage) |
