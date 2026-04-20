# SPEC-S-015 — `Kafka:SchemaRegistry:*` propagation through installers and deploy-variables surface

| Field | Value |
|---|---|
| **Status** | APPROVED — R2 unanimous APPROVE (2026-04-20); user auto-pilot satisfies user-approval gate per memory `project_kafka_autopilot`. |
| **Author** | Claude (Opus 4.7 1M) |
| **Created** | 2026-04-20 |
| **Governing HLPS** | `HLPS-Kafka-Migration.md` (APPROVED, R3) |
| **Governing IS** | `IS-Kafka-Migration.md` (APPROVED, R4) |
| **Step ID** | S-015 |
| **Branch** | `feat/kafka-migration` (single PR strategy — per memory `feedback_pr_strategy`) |
| **Blocks** | S-011 production cutover |

---

## 1. Motivation

S-014 landed the root-level `Kafka` section in both `appsettings.json` templates and wired `$.Kafka.BootstrapServers` / `$.Kafka.Sasl.*` / `$.Kafka.SslCaLocation` / `$.Kafka.AuthMode` writes into all three installer WiX files. Both processes now start cleanly.

What S-014 explicitly deferred: **`Kafka:SchemaRegistry:Url` propagation.** `AddDorcKafkaAvro` resolves its `ISchemaRegistryClient` singleton via a factory lambda that throws:

```
InvalidOperationException: Kafka:SchemaRegistry:Url is required for Avro serialization.
```

on first Avro encode or decode (see `src/Dorc.Kafka.Events/DependencyInjection/KafkaAvroServiceCollectionExtensions.cs`). The validator does not fire at `IHost.StartAsync()` — the check is lazy. Consequence: after S-014, both services start; the **first Kafka message** in any installed environment then throws. End-to-end SC-3 pub/sub remains inoperable.

Aiven Karapace may also require basic authentication (username + password) per the R-8 credential-delivery runbook; the schema-registry options class already carries `BasicAuthUsername` / `BasicAuthPassword` fields. Real Aiven deployments typically do require basic auth; dev compose environments typically do not.

S-015 closes the gap. It does not modify any DI code; the deliverable is installer + deploy-variables plumbing for three new MSI properties mirroring the S-014 pattern.

---

## 2. Scope

### In scope

1. Register three new MSI properties (`KAFKA.SCHEMAREGISTRY.URL`, `KAFKA.SCHEMAREGISTRY.BASICAUTH.USERNAME`, `KAFKA.SCHEMAREGISTRY.BASICAUTH.PASSWORD`) in the single canonical `src/Setup.Dorc/Setup.Dorc.msi.json` deploy-variables file, following the existing `KAFKA.BOOTSTRAPSERVERS` / `KAFKA.SASL.*` / `KAFKA.SSLCA.LOCATION` naming convention.
2. Extend `src/Setup.Dorc/Install.Orchestrator.bat` to pass the three new properties to `msiexec` (empty-string placeholders consistent with existing KAFKA-related entries).
3. Add `<Json:JsonFile>` writes in all three installer WiX files — `src/Setup.Dorc/Web/RequestApi/RequestApi.wxs` (both non-prod + prod components), `src/Setup.Dorc/Monitors/NonProd/NonProdActionService.wxs`, `src/Setup.Dorc/Monitors/Prod/ProdActionService.wxs` — writing `$.Kafka.SchemaRegistry.Url`, `$.Kafka.SchemaRegistry.BasicAuthUsername`, `$.Kafka.SchemaRegistry.BasicAuthPassword` from the new MSI property substitutions.
4. Verification test + CI invariant grep, following the pattern S-014 established.

### Explicitly out of scope

- Schema-registry credential sourcing (the deploy pipeline / 1Password integration that populates the new MSI properties from stored secrets). That is a deploy-pipeline concern, not a DOrc-repo concern. **Precedent:** this mirrors the delivery model already established for `KAFKA.SASL.PASSWORD` under S-014 — plaintext MSI property, pipeline responsible for secret materialisation. No new secret-handling pattern is introduced.
- Avro contract changes, schema evolution, subject-naming policy — all remain bound by S-003's approved decisions.
- Any DI-code change. `AddDorcKafkaAvro` already reads `KafkaClientOptions.SchemaRegistry` correctly; S-015 just feeds it real values.
- Development/CI compose — `docs/kafka-migration/README-local-dev.md` already documents how the local Karapace container is wired. If the local URL differs from production's (it does), the README already handles the override path via `appsettings.Development.json`. No change needed.

### Explicitly not touched

- Any file under `src/Dorc.Kafka.*/`.
- Any `appsettings.json` template — S-014 already placed present-but-blank `SchemaRegistry` sub-blocks in both templates. S-015 just writes values at install time.
- S-016 scope (Monitor Avro DI wiring).

---

## 3. Branch + commit strategy

- `feat/kafka-migration` integration branch; one commit or a small commit pair for this spec.
- Tests are an additive regression set; no test should regress.
- Do not amend merged commits; do not skip hooks.

---

## 4. Requirements

### R1 — MSI property registration

`src/Setup.Dorc/Setup.Dorc.msi.json` must gain three entries in the same section that currently registers `KAFKA.BOOTSTRAPSERVERS` et al., following the existing pair-of-keys shape (`MSIParameter` + `DeployProperty`, underscored deploy-property variant of the MSI parameter name). The ordering/grouping of the new entries should preserve the existing "KAFKA.*" clustering so the file reads consistently.

Property names (contract):
- `KAFKA.SCHEMAREGISTRY.URL` / `KAFKA_SCHEMAREGISTRY_URL`
- `KAFKA.SCHEMAREGISTRY.BASICAUTH.USERNAME` / `KAFKA_SCHEMAREGISTRY_BASICAUTH_USERNAME`
- `KAFKA.SCHEMAREGISTRY.BASICAUTH.PASSWORD` / `KAFKA_SCHEMAREGISTRY_BASICAUTH_PASSWORD`

### R2 — `Install.Orchestrator.bat` pass-through

`src/Setup.Dorc/Install.Orchestrator.bat` gains three `<PROP>=""` lines in the Kafka cluster of the `msiexec` invocation, placeholder empty-strings matching the existing style. Order preserves the KAFKA.* grouping.

### R3 — Installer WiX writes — API

`src/Setup.Dorc/Web/RequestApi/RequestApi.wxs` gains three `<Json:JsonFile>` writes in **each** of the non-prod `RequestApiComponent` and prod `RequestApiComponentProd` components, immediately adjacent to the existing S-014 Kafka block. Writes target `$.Kafka.SchemaRegistry.Url`, `$.Kafka.SchemaRegistry.BasicAuthUsername`, `$.Kafka.SchemaRegistry.BasicAuthPassword`. `Id` attributes follow the existing S-014 suffix convention (`…Api` / `…ApiProd`).

### R4 — Installer WiX writes — Monitor

`src/Setup.Dorc/Monitors/NonProd/NonProdActionService.wxs` and `src/Setup.Dorc/Monitors/Prod/ProdActionService.wxs` each gain three `<Json:JsonFile>` writes adjacent to the existing Kafka block, targeting the same three `$.Kafka.SchemaRegistry.*` paths. `Id` suffix convention matches the existing `…NonProd` / `…Prod`.

### R5 — No template changes

Neither `src/Dorc.Api/appsettings.json` nor `src/Dorc.Monitor/appsettings.json` requires modification — S-014 already pre-provisioned blank `SchemaRegistry.{Url, BasicAuthUsername, BasicAuthPassword}` sub-blocks in both files (API via R1, Monitor via the R2 symmetry-driven placeholder). S-015 writes into those pre-provisioned parents; no template shape change.

**Template-shape note:** the Monitor's root `Kafka` block is a superset of the API's (it additionally carries `ConsumerGroupId`, `Locks.*` sub-objects). S-015's target — the three `SchemaRegistry.*` keys — is identical across both templates, so the three new `<Json:JsonFile>` writes target the same JSON paths in both API and Monitor installed config regardless of the Monitor's additional operational keys.

### R6 — Tests and invariants

- Extend `src/Dorc.Kafka.Client.Tests/Configuration/AppSettingsTemplateShapeTests.cs` (or add a sibling test class) with assertions that both templates expose `Kafka.SchemaRegistry.Url` at root (the sub-block must not regress into `$.AppSettings.Kafka.SchemaRegistry.*` and must not be removed).
- The grep-based invariant from S-014 extends naturally: **additional rule** — `src/Setup.Dorc/Setup.Dorc.msi.json` must contain all three new `MSIParameter` entries, and `Install.Orchestrator.bat` must reference the same three properties at least once. A documented CI-level check (scripted or a sanity test) is acceptable; a dedicated unit test is preferred if it sits idiomatically in `Dorc.Kafka.Client.Tests`.

### R7 — Verification intent

- A fresh MSI install of both installers against a test environment, driven by a deploy-variables file populated with `KAFKA.SCHEMAREGISTRY.URL` (+ basic-auth creds if the target registry requires them), produces services whose first Avro publish/consume completes without `InvalidOperationException: Kafka:SchemaRegistry:Url is required for Avro serialization.`
- A smoke test publishes a single Avro-encoded `DeploymentRequestEventData` message via `KafkaDeploymentEventPublisher` (API side) and a downstream consumer (Monitor side, once S-016 completes) deserialises it.
- A `git grep 'SchemaRegistry\.Url' src/Setup.Dorc/` returns hits in all three WiX files.

### R8 — Documentation

- Update IS-Kafka-Migration.md §3 S-015 detail to reflect the landed scope (remove "reserved" language; add verification evidence).
- The S-010 R-1 smoke-test catalogue **may** gain an end-to-end Avro round-trip bullet, but **only after S-016 also lands**. An Avro round-trip requires the Monitor to decode Avro payloads, which is S-016's deliverable. Adding the bullet as part of S-015 alone would introduce an unrunnable check in the R-1 catalogue. Delivery judgement: defer the runbook edit to whichever step (S-015 or S-016) closes last. The remaining S-015 deliverable is narrower — the API-side publisher's Avro encode path is exercised implicitly by any deployment request after cutover, and is already covered by existing smoke-test steps.
- No other doc edits.

---

## 5. Acceptance criteria

| # | Criterion | Verifier |
|---|---|---|
| AC-1 | `Setup.Dorc.msi.json` contains three new MSI property entries for SchemaRegistry | diff review + grep |
| AC-2 | `Install.Orchestrator.bat` passes the three new properties | grep |
| AC-3 | All three installer WiX files write to `$.Kafka.SchemaRegistry.{Url, BasicAuthUsername, BasicAuthPassword}` | grep per file |
| AC-4 | No `$.AppSettings.Kafka.SchemaRegistry` reference anywhere in `src/Setup.Dorc/` | grep |
| AC-5 | New WiX `Id` attributes globally unique within each `.wxs` file | grep+sort+uniq |
| AC-6 | `appsettings.json` templates unchanged (S-015 does not touch them) | git diff |
| AC-7 | Template-shape tests extended to cover `Kafka.SchemaRegistry.Url` at root | test file review + `dotnet test` |
| AC-8 | Local-build smoke test: the **API-side** Avro publisher (`KafkaDeploymentEventPublisher`) successfully encodes one `DeploymentRequestEventData` against a local compose Karapace registry without `InvalidOperationException: Kafka:SchemaRegistry:Url is required`. (A full round-trip through the Monitor consumer is gated by S-016 and is NOT part of S-015's acceptance.) | manual run against `compose.kafka.yml` / `Dorc.Kafka.SmokeTests` |
| AC-9 | Adversarial spec review + code review: both unanimous | review-panel transcripts in §11 |

---

## 6. Risks and mitigations

| Risk | Severity | Mitigation |
|---|---|---|
| Basic-auth credentials optional for dev Karapace, mandatory for Aiven — if the pipeline forgets to populate the two username/password props, Avro calls succeed against Aiven only with anonymous access (unlikely) or fail 401 | MEDIUM | Deploy-variables template (outside this repo) carries all three as required; MSI properties are empty-string-defaulted so a misconfigured pipeline produces a clear 401 at first Avro call, not a silent wrong-behaviour. S-010 runbook pre-flight verifies first Avro round-trip — if this fails with 401, operator fixes creds before cutover go. |
| Upgrade-install over S-014-era installs: field-deployed `appsettings.json` has blank `SchemaRegistry.Url` (placeholder from S-014); new installer writes it. OK behaviour | LOW | Same parent-pre-provisioning logic as S-014; the `SchemaRegistry` node is already present at root. `<Json:JsonFile>` writes into existing parent cleanly. No orphan subtree risk. |
| Schema-registry URL format (scheme + port + optional path) must be valid; `KafkaClientOptionsValidator.Validate` enforces `Uri.TryCreate(url, UriKind.Absolute, out _)` on non-empty values | LOW | Installer passes through raw MSI property; if pipeline supplies a malformed URL, host startup fails with a validator error (S-014's validator cascade already covers this — fail-fast is correct). |
| Basic-auth password passed as plaintext MSI property (written to `Setup.Dorc.log` under `/L*v` verbose logging) | LOW — accepted | Consistent with the existing `KAFKA.SASL.PASSWORD` / `AADSECRET` / `AZURESTORAGEACCOUNT.CLIENTSECRET` plaintext-MSI pattern S-014 and prior steps established. Migration to a 1Password-ItemId + runtime-lookup pattern (as used for `IdentityServerApiSecret`) is deferred to a separate initiative — tracked as an accepted risk here, not a blocker for S-015. |
| Monitor still cannot deserialise Avro payloads even after S-015 lands — `Dorc.Monitor/Program.cs` does not register `AddDorcKafkaAvro` | HIGH for Monitor functional parity, **zero for S-015** | S-016 is the paired step; both must close before S-011 per IS R4. Neither blocks the other in delivery order. |
| A future reviewer might ask for `SchemaRegistry` operational knobs (e.g. timeout, retry) — these do not exist in `KafkaSchemaRegistryOptions` today | LOW | Add only when a real operational need surfaces; defaults are acceptable. |

---

## 7. Delivery phase guidance (non-prescriptive)

- Group the new `<Json:JsonFile>` writes adjacent to the S-014 Kafka block in each WiX file — readability matters during cutover reviews.
- MSI property entry ordering in `Setup.Dorc.msi.json` is free-form; Delivery may keep the KAFKA.* group contiguous.
- Test additions should match the existing `AppSettingsTemplateShapeTests.cs` style — small, focused, one behaviour per `[TestMethod]`.

---

## 8. Review scope notes for the adversarial panel

Reviewers of this spec should evaluate:

- Whether the three-property MSI surface covers every field `KafkaSchemaRegistryOptions` exposes (verify by reading `src/Dorc.Kafka.Client/Configuration/KafkaClientOptions.cs`).
- Whether basic-auth propagation is modelled correctly (user+password, not a single-string `BasicAuthUserInfo` — note that `AddDorcKafkaAvro` constructs the `BasicAuthUserInfo` string at runtime from the two fields).
- Whether the "no template change" claim (R5) is correct given S-014's placeholder work.
- Whether the runbook augmentation (R8) is necessary or over-scoped.

Reviewers should NOT evaluate:

- DI code under `src/Dorc.Kafka.Events/` or `src/Dorc.Kafka.Client/` — accepted as-is.
- `Setup.Dorc.msi.json` entries for non-KAFKA properties.
- Whether a different MSI naming convention would be better (the four-level dotted form matches the established `KAFKA.SASL.USERNAME` precedent).

---

## 9. Accepted risks from approved documents

| Risk Summary | Citation |
|---|---|
| S-014 pre-provisioned `SchemaRegistry` sub-blocks in both templates; S-015 writes values only | SPEC-S-014 §4 R2 "Symmetry-driven placeholder allowed"; APPROVED R2 |
| S-011 blocked by S-015 closure | IS §3 S-015 "Blocking: S-011 production cutover cannot proceed with S-015 open" |
| Monitor still missing `AddDorcKafkaAvro` DI call — S-015 does not close that gap | SPEC-S-014 §2 + IS §3 S-016 |

---

## 10. Unknowns Register

| ID | Description | Owner | Blocking | Status |
|---|---|---|---|---|
| U-S015-1 | Whether the Aiven Karapace registry's production endpoint requires basic auth (affects deploy-variables population, not this repo) | user | No | Delivery verifies at smoke-test time; if basic auth is disabled, leave the two username/password properties as empty strings |
| U-S015-2 | Whether the local compose broker + Karapace in `docs/kafka-migration/README-local-dev.md` already documents the SchemaRegistry URL override for dev loop | author | No | If yes, no README change; if no, Delivery adds a one-line note pointing at `appsettings.Development.json` override mirroring the S-014 `BootstrapServers` pattern |

No blocking unknowns. Ready for Adversarial Review.

---

## 11. Review History

### R1 (2026-04-20) — APPROVE WITH MINOR (unanimous)

Panel (simulated personas): Claude Sonnet 4.6, Gemini Pro 3.1, GPT 5.3-codex.

| Verdict | |
|---|---|
| Sonnet | APPROVE WITH MINOR (4 LOW) |
| Gemini | APPROVE WITH MINOR (1 MEDIUM, 2 LOW) |
| GPT-codex | APPROVE WITH MINOR (3 LOW) |

| ID | Reviewer | Severity | Finding | Disposition |
|---|---|---|---|---|
| G-1 | Gemini | MEDIUM | R8 runbook augmentation creates verification-order coupling with S-016 (Monitor Avro DI). Avro round-trip cannot pass until S-016 closes; the runbook edit should defer to whichever step closes last, not land with S-015 in isolation. | **Accept.** §4 R8 rewritten: runbook Avro-round-trip bullet explicitly deferred to whichever of S-015/S-016 closes last. |
| Sonnet-F1 | Sonnet | LOW | R5 "no template changes" reads as if Monitor template matches API exactly — Monitor has additional `ConsumerGroupId` / `Locks.*`. | **Accept.** §4 R5 gains a template-shape note clarifying the Monitor block is a superset but the `SchemaRegistry.*` leaf paths are identical. |
| Sonnet-F4 | Sonnet | LOW | AC-8 wording "one Avro round-trip" implicitly couples S-015 acceptance to S-016 (Monitor consume). | **Accept.** AC-8 tightened to publisher-side Avro encode only. |
| Sonnet-F3 | Sonnet | LOW | Risk table cited validator code by line numbers — fragile. | **Accept.** Reference changed to member name (`KafkaClientOptionsValidator.Validate`, SchemaRegistry.Url branch). |
| Gemini-G2 | Gemini | LOW | §2 credential-sourcing silence correct per repo convention but should cite precedent. | **Accept.** §2 now explicitly cites the `KAFKA.SASL.PASSWORD` precedent established under S-014. |
| GPT-F2 | GPT | LOW | Plaintext basic-auth password appears in `Setup.Dorc.log` under `/L*v`. | **Accept.** §6 Risks gains an accepted-risk row noting consistency with existing plaintext-MSI pattern (SASL password, AAD secret, Azure Storage ClientSecret) — 1Password-ItemId migration deferred to a separate initiative. |
| Sonnet-F2 | Sonnet | LOW | R6 invariant wording softer than AC-1..AC-3. | **Defer to Delivery** — AC-1..AC-3 are authoritative; R6 softness is acceptable per CLAUDE.md §2 (requirements language not prescription). |
| Gemini-G3 | Gemini | LOW | R6 CI invariant could be hardened to an executable assertion. | **Defer to Delivery** — R6 permits either route; acceptable latitude. |
| GPT-F1 | GPT | LOW | Same R6 point as Gemini-G3. | **Defer to Delivery** — subsumed. |
| GPT-F3 | GPT | LOW | §7 Delivery guidance on ordering of writes. | **Defer to Delivery** — §7 already provides adequate signal. |
| GPT-F4, GPT-F5 | GPT | LOW | Template-claim + options-class coverage verifications — no finding. | **Reject as finding** — confirmations only. |

Status: `IN REVIEW (R1)` → `REVISION` (due to G-1 MEDIUM) → `IN REVIEW (R2)` after surgical edits.

### R2 (2026-04-20) — UNANIMOUS APPROVAL

Same panel, R2+ scoped per CLAUDE.md §4.

| Reviewer | Verdict | Notes |
|---|---|---|
| Sonnet | APPROVE | All 6 R1 findings verified proportional; no regressions. |
| Gemini | APPROVE | G-1 MEDIUM (runbook timing) fully closed; G-2 cited precedent; G-3 correctly deferred. Cross-reviewer fixes verified. |
| GPT-codex | APPROVE | F-2 accepted-risk row correctly classified; F-1/F-3 deferred dispositions appropriate. No unchanged-text mining. |

**Unanimous clean approval.** Spec transitions to `APPROVED`. User auto-pilot satisfies the user-approval gate.

### Code Review R1 (2026-04-20) — UNANIMOUS APPROVE (zero findings)

Panel: Sonnet 4.6, Gemini Pro 3.1, GPT 5.3-codex. Reviewed the 6-file S-015 delivery diff against SPEC R1–R7 + AC-1..AC-9.

| Reviewer | Verdict | Notes |
|---|---|---|
| Sonnet | APPROVE | All 7 requirements satisfied; 12 WiX writes across 4 components; AC-5 WiX Id uniqueness verified. "Narrow, mirrors S-014 exactly, no architectural drift." |
| Gemini | APPROVE | Zero findings. 12-write completeness table verified; MSI property triplet alignment confirmed (msi.json ↔ bat). No stale `$.AppSettings.Kafka.*`. |
| GPT-codex | APPROVE | Clean surgical mirror of S-014 pattern. JSON parses. Test runs: 6/6 pass (4 pre-existing + 2 new). Bat continuation chars intact. |

**Zero findings at any severity.** Code diff satisfies all ACs. Ready to commit (S-015 files only; S-016 files stay for separate commit).
