# SPEC-S-014 — Installer + `appsettings.json` template alignment for the Kafka substrate

| Field | Value |
|---|---|
| **Status** | APPROVED — user auto-pilot on 2026-04-20 satisfies user-approval gate (see memory `project_kafka_autopilot`). R2 panel: Sonnet APPROVE / Gemini APPROVE / GPT APPROVE WITH MINOR (one LOW Defer-to-Delivery) = unanimous approval. |
| **Author** | Claude (Opus 4.7 1M) |
| **Created** | 2026-04-20 |
| **Governing HLPS** | `HLPS-Kafka-Migration.md` (APPROVED, R3) |
| **Governing IS** | `IS-Kafka-Migration.md` (APPROVED through R3; R4 revision in-flight adding S-014, S-015, S-016) |
| **Step ID** | S-014 |
| **Branch** | `feat/kafka-migration` (single PR strategy — per memory `feedback_pr_strategy`) |

---

## 1. Motivation

During S-010 cutover-prep deployment of the current `feat/kafka-migration` build to ST-01 (DEPAPP03UT), the **Dorc.Api** IIS-hosted process refused to start. The captured ANCM stdout shows:

```
Unhandled exception. System.Reflection.TargetInvocationException:
  Microsoft.Extensions.Options.OptionsValidationException: Kafka:BootstrapServers is required.
    at …KafkaConnectionProvider..ctor(IOptions`1 options)
    at …AddDorcKafkaResultsStatusSubstrate…
```

The binding path required by `KafkaClientOptionsValidator` is root-level `Kafka:*` (per `KafkaClientOptions.SectionName`). Root cause:

1. **API source template** `src/Dorc.Api/appsettings.json` carries **no `Kafka` section at all** — neither placeholder nor default values. The S-009 spec's declared scope did not include installer or `appsettings.json` template work, so the API side was never extended when Kafka became unconditional in the API's DI graph (the DI code itself was added to `src/Dorc.Api/Program.cs` under the same integration-branch work, but the config surface to feed it was not).
2. **API installer** `src/Setup.Dorc/Web/RequestApi/RequestApi.wxs` emits **zero** `<Json:JsonFile>` writes for any `Kafka:*` key — see commit-history verification below.
3. **Monitor source template** `src/Dorc.Monitor/appsettings.json` nests its `Kafka` block **under `AppSettings`** (not at the root), and the **Monitor installers** `src/Setup.Dorc/Monitors/NonProd/NonProdActionService.wxs` and `…/Prod/ProdActionService.wxs` write Kafka keys to `$.AppSettings.Kafka.*`. These paths do not match the DI binding. **S-009 commit `012f3987` delivered these Monitor-side installer writes and template-nesting changes outside the spec's declared scope**, and the binding-path error shipped undetected because the S-009 spec text did not call out an installer deliverable and the review panel had no reason to check WiX ElementPaths.

S-014 is therefore two things combined: (a) **remediation** of the S-009-delivered Monitor WiX + template at the wrong nesting path, and (b) **completion** of the API-side installer + template, a deliverable that was never declared in any prior spec. Both manifest identically (`Kafka:BootstrapServers is required.` at host startup) because the DI contract is identical for both services.

The SC-3 pub/sub substrate now lives in both processes (API via `AddDorcKafkaClient` + `AddDorcKafkaAvro` + `AddDorcKafkaErrorLog` + `AddDorcKafkaResultsStatusSubstrate`; Monitor via `AddDorcKafkaDistributedLock` + `AddDorcKafkaRequestLifecycleSubstrate`, both of which transitively register `KafkaClientOptions`). Any cutover drop made from the current branch will produce a non-starting API + non-starting Monitor. S-010's AT-5..AT-8 (service-up smoke tests within the dry-run runbook) cannot pass until installed MSIs stand the services up cleanly.

This spec closes the gap surfaced during S-010 without modifying the DI contract delivered under S-006/S-007/S-009 — the DI code and Kafka section-name constants are accepted as-is; only the **templates** and **installer WiX** need to catch up. Two adjacent latent defects are deferred to reserved follow-on steps (§2 below).

---

## 2. Scope

### In scope

1. Root-level canonical `Kafka` section in the two checked-in `appsettings.json` templates (API + Monitor).
2. Installer alignment: all Kafka-related `<Json:JsonFile>` writes use the root-level path.
3. API installer gains the Kafka write set that it currently lacks, reusing whatever MSI property naming convention the Monitor installers already established (single deploy-variable surface).
4. A smoke verification step exercising a fresh install of each MSI against a local/dev environment until both services pass host-start validation.

### Explicitly out of scope — tracked via reserved follow-on step IDs with binding cutover-gate

Both of the following are reserved as new IS rows under the same R4 revision that creates S-014. They are **not blockers for S-014 closure**, but they **are blockers for S-011 production cutover**, and they are named explicitly here so that no future reader can treat the deferral as a quiet shelving:

- **S-015 — `Kafka:SchemaRegistry:Url` propagation through the installers.** The `KafkaClientOptionsValidator` does not demand the schema-registry URL at startup, so S-014's "services start" acceptance passes without it. But `AddDorcKafkaAvro` constructs its schema-registry client lazily and throws `InvalidOperationException: Kafka:SchemaRegistry:Url is required for Avro serialization.` on first Avro use (verified in `src/Dorc.Kafka.Events/DependencyInjection/KafkaAvroServiceCollectionExtensions.cs`). End-to-end Kafka traffic therefore fails until S-015 adds the URL (+ basic-auth username/password if applicable) to `Setup.Dorc.msi.json`, `Install.Orchestrator.bat`, both `appsettings.json` templates, and all three installer WiX files. **Must close before S-011.** Spec author + review round to be scheduled once S-014 merges.
- **S-016 — Monitor-side Avro DI wiring.** `src/Dorc.Monitor/Program.cs` calls only `AddDorcKafkaDistributedLock` and `AddDorcKafkaRequestLifecycleSubstrate`; neither transitively registers `AddDorcKafkaAvro`. Result: `AddDorcKafkaClient` (called transitively by the lock extension) registers the **no-op** `DefaultKafkaSerializerFactory` (verified in `src/Dorc.Kafka.Client/DependencyInjection/KafkaClientServiceCollectionExtensions.cs` + `DefaultKafkaSerializerFactory`), and the Monitor's `DeploymentRequestsKafkaConsumer` will not deserialise Avro-encoded Request-lifecycle payloads from `dorc.requests.new` / `dorc.requests.status`. This is a functional SC-3 defect, not merely a startup concern. S-016 is a DI/code fix (add the DI call to `Dorc.Monitor/Program.cs` — small surface) rather than an installer fix, and is entirely orthogonal to S-014's template/WiX work. **Must close before S-011.**

### Additional items out of scope (no follow-on ID reserved — handled at Delivery's discretion or deferred to operational need)

- Operational tuning knobs (`Kafka:ErrorLog:*`, `Kafka:Substrate:*`, `Kafka:Locks:*` beyond what the Monitor template already carries). Defaults in the options classes are acceptable; per-environment override is a follow-on if and when a real operational need surfaces.

### Explicitly not touched

- Any file under `src/Dorc.Kafka.*/` — the DI extensions and options classes are the contract this spec aligns the installer *to*. No code under those trees changes.
- The deployed files on DEPAPP03UT — field patching is a separate operational action the user will take after this spec lands.

---

## 3. Branch + commit strategy

- Work on `feat/kafka-migration` (active integration branch; memory `feedback_pr_strategy` — single PR).
- Small incremental commits preferred; one-commit-per-file if a single file is large.
- Test-first where applicable: a regression test confirming the source templates carry a root-level `Kafka` section and that the installer WiX writes target root-level paths, covered by an xml/json-shape assertion test or a checklist in the verification plan if no existing test harness covers WiX content.
- **Do not** skip commit hooks. Do not amend merged commits.

---

## 4. Requirements

### R1 — API source template carries a root-level `Kafka` section, dev-friendly default

`src/Dorc.Api/appsettings.json` must, at the JSON root (sibling of `AppSettings`, `ConnectionStrings`, `Azure`, `OpenSearchSettings`, etc.), expose a `Kafka` object with at minimum: `BootstrapServers`, `AuthMode`, `Sasl.{Mechanism,Username,Password}`, `SslCaLocation`, `SchemaRegistry.{Url,BasicAuthUsername,BasicAuthPassword}`.

**Checked-in default AuthMode: `Plaintext`.** Rationale: an OSS contributor following `docs/kafka-migration/README-local-dev.md` stands up a local Kafka broker via compose with no SASL. With `AuthMode = "Plaintext"`, a fresh clone + `dotnet run` fails validation only on `BootstrapServers` — the developer adds a single override (compose broker host:port) in `appsettings.Development.json` and proceeds. With `AuthMode = "SaslSsl"`, the validator cascade demands `Sasl.Username`, `Sasl.Password`, **and** `Sasl.Mechanism`, turning a one-line override into four. `Plaintext` matches the local-dev substrate the README promises.

**Production `SaslSsl` posture is enforced by the installer, not by the checked-in template** — see R4.

`Sasl.Mechanism` should default to `SCRAM-SHA-256` (matching the Aiven cluster), and `Sasl.{Username,Password}` to empty strings. `SchemaRegistry.*` keys are present-but-blank (S-015 populates them). All other values blank/placeholder; the installer populates per-environment.

The `Kafka` block's **shape** is the contract this spec guarantees; the default values are contract-adjacent but chosen for the local-dev ergonomics above.

### R2 — Monitor source template moves `Kafka` to root (+ symmetry-driven SchemaRegistry placeholder allowed)

`src/Dorc.Monitor/appsettings.json`: the existing `Kafka` block nested under `AppSettings` must move to the JSON root (sibling of `AppSettings`, `ConnectionStrings`, etc.). The block's pre-existing keys (`BootstrapServers`, `AuthMode`, `ConsumerGroupId`, `Sasl.*`, `SslCaLocation`, `Locks.*`) are preserved unchanged — the primary act is a parent-path relocation, not a reshape.

**Symmetry-driven placeholder allowed:** Delivery MAY add a blank `SchemaRegistry.{Url, BasicAuthUsername, BasicAuthPassword}` sub-block to the Monitor template so its shape matches the API template (R1). The sub-block is present-but-blank, has no corresponding installer write (that is strictly S-015's job), and the DI contract ignores it until populated. This is symmetry scaffolding, not S-015 feature creep — it reduces S-015's surface to installer writes only. Strict R2 readers should treat this as a permitted supplement; the DI binding remains unaffected and no key previously present is removed.

### R3 — Monitor installer writes target root-level paths (+ AuthMode literal)

`src/Setup.Dorc/Monitors/NonProd/NonProdActionService.wxs` and `src/Setup.Dorc/Monitors/Prod/ProdActionService.wxs` must have their existing Kafka `<Json:JsonFile>` elements' `ElementPath` attributes moved from `$.AppSettings.Kafka.*` to `$.Kafka.*`. Existing MSI property names (`KAFKA.BOOTSTRAPSERVERS`, `KAFKA.SASL.USERNAME`, `KAFKA.SASL.PASSWORD`, `KAFKA.SSLCA.LOCATION`) are unchanged.

**Add a new literal write per installer: `$.Kafka.AuthMode = "SaslSsl"`** — paralleling R4's API-side write. This guarantees every per-env deploy forces `SaslSsl`, regardless of whether the Monitor template default is kept at `"SaslSsl"` (current) or changes in future to match R1's API template `Plaintext` for symmetry. Symmetry is not required by this spec but the literal installer write makes the deployed posture robust against any future template-default drift.

### R4 — API installer gains a parallel Kafka write set (+ AuthMode forced to SaslSsl)

`src/Setup.Dorc/Web/RequestApi/RequestApi.wxs` must, in **both** the non-prod `RequestApiComponent` and the prod `RequestApiComponentProd` components, add `<Json:JsonFile>` writes for the same Kafka keys the Monitor installers write, targeting root-level `$.Kafka.*` paths.

**MSI property reuse.** The four KAFKA properties (`KAFKA.BOOTSTRAPSERVERS`, `KAFKA.SASL.USERNAME`, `KAFKA.SASL.PASSWORD`, `KAFKA.SSLCA.LOCATION`) are already registered in the single canonical deploy-variables file `src/Setup.Dorc/Setup.Dorc.msi.json` and passed via `src/Setup.Dorc/Install.Orchestrator.bat` to the single `Setup.Dorc.msi`. Because all DOrc installers live inside one MSI, `<Json:JsonFile Value="[KAFKA.BOOTSTRAPSERVERS]">` substitutions in `RequestApi.wxs` resolve from the same MSI property as the equivalent substitutions in the Monitor WiX files — no new MSI property registrations required. Delivery confirms this by visual inspection of `Setup.Dorc.msi.json` and does not add new entries for properties that already exist.

**Force AuthMode = "SaslSsl" in both installers.** Because R1 sets the checked-in template default to `Plaintext` for dev ergonomics, both the API installer (R4 here) **and** the Monitor installers (R3 amended) must write `$.Kafka.AuthMode = "SaslSsl"` as a **literal** value (no MSI property needed — every deployed environment is SaslSsl against Aiven). This keeps the per-env prod posture correct without reintroducing the one-line-vs-four-line dev-loop friction in R1.

`Id` attributes on the new elements must be unique within each component (suffix `Api` / `ApiProd` follows the existing convention in the file). The write elements belong in the same component body that already carries the other API `Json:JsonFile` writes (immediately after the Azure/OpenTelemetry block is a natural home, matching Monitor-side layout).

### R5 — Tests / assertions

Where a tractable test hook exists, a regression test is preferred over a one-time verification. For this spec:

- **Candidate test home identified:** `src/Dorc.Kafka.Client.Tests/Dorc.Kafka.Client.Tests.csproj` already owns `KafkaClientOptions` binding/validation tests. A file-read assertion ("both source templates expose `Kafka.BootstrapServers` at the JSON root") fits idiomatically there with no new csproj or build-graph edges. Delivery is **encouraged** to place the test there; deviating from that home is acceptable only if the Delivery engineer finds a concrete reason it does not fit.
- A WiX shape check is not trivially testable inside the .NET test suite; a grep-based CI assertion ("no `$.AppSettings.Kafka` strings in any `.wxs` under `src/Setup.Dorc/`" + "`$.Kafka.BootstrapServers` appears in `RequestApi.wxs` at least once") is acceptable as a repo-level invariant if the test harness route proves disproportionate.

The Delivery phase chooses the lightest viable mechanism consistent with the project's existing test conventions.

### R6 — Verification intent

- After a clean local build, both `Dorc.Api` and `Dorc.Monitor` start against an `appsettings.json` whose only Kafka override is a populated root-level `Kafka:BootstrapServers` (plus the SASL block in SaslSsl mode).
- A fresh MSI install of both installers against a test environment, driven by a deploy-variables file populated with `KAFKA.BOOTSTRAPSERVERS` + `KAFKA.SASL.USERNAME` + `KAFKA.SASL.PASSWORD` + `KAFKA.SSLCA.LOCATION`, produces services that complete their `IHost.StartAsync()` without raising `OptionsValidationException`.
- A `git grep '\$\.AppSettings\.Kafka'` across `src/` returns zero hits.
- A `git grep '"Kafka"' src/Dorc.Api/appsettings.json` and `src/Dorc.Monitor/appsettings.json` both return a hit at JSON-root nesting depth.

End-to-end Kafka message round-trip is **not** within S-014 verification — that is gated by the SchemaRegistry follow-on. S-014 closes when startup validation passes on fresh installs.

### R7 — Documentation

- `docs/kafka-migration/IS-Kafka-Migration.md` step table adds the S-014 row; §3 gains an S-014 detail entry; §6 gains an R4 section labelled "Implementation-Discovery Revision — S-014 added" with the same convention the existing R3 section uses.
- No other doc edits are required.
- `docs/kafka-migration/S-010-Cutover-Runbook.md` may want a one-line "pre-flight: fresh-install smoke test of API + Monitor MSIs passes startup validation" reminder but this is not blocking for S-014 closure (noted for the Delivery phase judgement).

---

## 5. Acceptance criteria

| # | Criterion | Verifier |
|---|---|---|
| AC-1 | Both source templates carry `Kafka` at JSON root | `git grep` + manual read |
| AC-2 | No WiX file under `src/Setup.Dorc/` writes to `$.AppSettings.Kafka.*` | `git grep` |
| AC-3 | `RequestApi.wxs` writes at least `BootstrapServers`, `Sasl.Username`, `Sasl.Password`, `SslCaLocation` at `$.Kafka.*` for both non-prod and prod components | `git grep` |
| AC-4 | Monitor installers write to `$.Kafka.*` not `$.AppSettings.Kafka.*`; no key removed | diff review |
| AC-5 | Local-build `Dorc.Api` + `Dorc.Monitor` pass `IHost.StartAsync()` with a root-level Kafka block containing BootstrapServers + SaslSsl creds | manual run against local compose broker per `docs/kafka-migration/README-local-dev.md` |
| AC-6 | S-010 **cutover procedure** (the run-of-show) is not modified — S-014 only makes fresh MSI installs viable as input to the procedure. S-010's **pre-flight checklist** (R-1 smoke-test catalogue) gains a one-line post-deploy check per §6 Risks (see below) — this is an augmentation, not a procedure change | runbook diff review |
| AC-7 | Adversarial Review panel unanimously approves this spec (R1, or later round after triage) | review-panel output |
| AC-8 | Adversarial Review panel unanimously approves the resulting code/WiX diff | review-panel output |

---

## 6. Risks and mitigations

| Risk | Severity | Mitigation |
|---|---|---|
| Moving the Monitor's `Kafka` block root-ward breaks a field-deployed instance that was hand-edited to match the buggy template shape; or an upgrade-install atop such an instance silently migrates its config | MEDIUM | **Mandatory** — Delivery adds a one-line post-deploy check to `docs/kafka-migration/SPEC-S-010-Cutover-Runbook.md` R-1 smoke-test catalogue: "verify `Kafka.BootstrapServers` is populated at JSON root on each installed node post-deploy; if found under `AppSettings.Kafka`, the deploy has not taken effect as expected and must be investigated before proceeding." This is not optional — the cutover cannot accept a Monitor running off stale config. |
| Upgrade-install atop a field-deployed Monitor that has `Kafka` data at `$.AppSettings.Kafka.*` leaves an **orphan** subtree at that path while also writing the new `$.Kafka.*` values | MEDIUM | `<Json:JsonFile>` writes the new path but does not remove the old. Cosmetically this produces a dual-state `appsettings.json` — the DI binding correctly reads only the root `$.Kafka`, so this is **functionally harmless**, but it is confusing to on-call operators reading the deployed file. Mitigation: the same S-010 pre-flight check above notes the dual-state possibility and advises operators that the root-level values are authoritative. A tombstone-delete for the stale path is not in S-014 scope (would require a second `<Json:JsonFile>` with an empty value or a cleanup CustomAction — evaluated at Delivery time if the cosmetic noise is deemed unacceptable). |
| An MSI property referenced by `RequestApi.wxs` but not registered in `Setup.Dorc.msi.json` causes `[KAFKA.BOOTSTRAPSERVERS]` to be written as a literal-string placeholder | LOW | Verified: the four `KAFKA.*` MSI properties are already registered in the single canonical `src/Setup.Dorc/Setup.Dorc.msi.json` (inherited from the existing Monitor installer work). Because all DOrc installers live inside one MSI, reuse is automatic — no new property registration required. Delivery must confirm visually, not author new entries. |
| SchemaRegistry URL not set → Avro serialization fails at first Kafka message, after startup succeeds | HIGH for E2E, **zero** for S-014 | Explicitly out of scope. Reserved as **S-015** with "Must close before S-011" binding gate (see §2). |
| Monitor's missing `AddDorcKafkaAvro` DI call will cause silent Avro-payload deserialisation failures on Request-lifecycle topics | HIGH for E2E, **zero** for S-014 | Explicitly out of scope. Reserved as **S-016** with "Must close before S-011" binding gate (see §2). |
| WiX Heat-harvesting / manifest regeneration needed after edits | LOW | `<Json:JsonFile>` elements are hand-authored in the existing `.wxs` files, not harvested. No regeneration required. |
| Tests for JSON template shape introduce a new test project and churn | LOW | R5 identifies `Dorc.Kafka.Client.Tests` as the idiomatic home — no new project needed. Checklist verification is the fallback only if Delivery finds that home unsuitable. |
| Tab vs space / mixed whitespace in the Monitor `appsettings.json` if the `Kafka` block is relocated by hand | LOW | Use the existing file's prevailing whitespace style (2-space indent is the visible norm in `src/Dorc.Monitor/appsettings.json`); diff review catches regressions. |

---

## 7. Delivery phase guidance (non-prescriptive)

- Do **not** remove the existing `Kafka:*` keys in the Monitor template in the move — the goal is a path-relocation, not a reshape.
- The API template's existing blocks (`AppSettings`, `Azure`, `OpenSearchSettings`, etc.) stay at their current positions; the new `Kafka` block is added as a new root-sibling in whichever alphabetical/logical position best matches the file's existing conventions.
- The WiX edits are mechanical; the Delivery phase chooses its own commit granularity.
- If a gap in test-harness coverage forces the checklist-verification route for R5, record the gap (with a one-line reason) in the S-010 runbook pre-flight checklist rather than silently accepting it.

---

## 8. Review scope notes for the adversarial panel

Reviewers of this spec should evaluate:

- Whether the scope cleanly closes the "fresh-install startup failure" failure mode without expanding to unrelated gaps.
- Whether the out-of-scope deferrals (SchemaRegistry Url, Monitor's missing `AddDorcKafkaAvro`) are correctly deferred and traceably recorded.
- Whether the MSI property reuse strategy is sound (shared property names across installers) — i.e. no hidden risk of property collision across existing deploy-variable files.
- Whether `Kafka:AuthMode` defaulting to `SaslSsl` in the API template is the right production-matching default, given the validator cascade (SaslSsl ⇒ Sasl.Username + Sasl.Password + Sasl.Mechanism all become required).

Reviewers should **NOT** evaluate:

- Whether the Kafka section-name / options-class contracts under `src/Dorc.Kafka.Client/` are correctly designed. These are accepted by S-002/S-006/S-007/S-009's prior approvals.
- Whether `SchemaRegistry` should be in scope — it's been explicitly scoped out with rationale.
- Whether the larger cutover runbook (S-010) is comprehensive — out of scope for S-014.
- Pseudocode syntax / hypothetical diffs.

---

## 9. Accepted risks from approved documents

| Risk Summary | Citation |
|---|---|
| S-009 removal of RabbitMQ before the S-010 dry-run means the installer config gap can only be discovered during the dry-run itself | IS-Kafka-Migration.md §6 R1, Finding "S-009 removes Rabbit before S-010 dry-run" — accepted; rollback tag is the mitigation, S-014 is the cutover-prep fix that S-010 surfaced |
| SignalR remains the UI transport post-cutover (SC-3 interpretation) | IS-Kafka-Migration.md §6 R3, GPT-F1 |
| S-007 retains the DirectDeploymentEventPublisher → SignalR fan-out for UI continuity | SPEC-S-007 §… (the feat/kafka-migration Program.cs explicit comment block reflects this approved design) |

---

## 10. Unknowns Register

| ID | Description | Owner | Blocking | Status |
|---|---|---|---|---|
| U-S014-1 | Whether the deploy-variables surface feeding `KAFKA.BOOTSTRAPSERVERS` et al. to the Monitor installer is the same artifact feeding the API installer | user | No | **RESOLVED** — R1 triage + GPT verification confirmed `src/Setup.Dorc/Setup.Dorc.msi.json` is the single canonical registration and `Install.Orchestrator.bat` is the single orchestration entry point; all DOrc installers share one MSI. §4 R4 captures this positively; no new property registrations required. |
| U-S014-2 | Whether a JSON-template-shape test project already exists in this repo | author | No | **RESOLVED** — R1 triage identified `src/Dorc.Kafka.Client.Tests` as the idiomatic home; Delivery placed the tests there. |

No blocking unknowns. All unknowns resolved during R1 triage / Delivery.

---

## 11. Review History

### R1 (2026-04-20) — REVISION REQUIRED

Panel: Claude Sonnet 4.6 persona, Gemini Pro 3.1 persona, GPT 5.3-codex persona (simulated — three adversarial subagents with distinct focus briefs).

| Verdict | |
|---|---|
| Sonnet | APPROVE WITH MINOR |
| Gemini | **REVISION REQUIRED** |
| GPT-codex | APPROVE WITH MINOR |

Aggregate: **REVISION REQUIRED** (Gemini's HIGH findings gate the resubmission).

| ID | Reviewer | Severity | Finding | Disposition |
|---|---|---|---|---|
| G-R4-1 | Gemini | HIGH | Monitor `AddDorcKafkaAvro` deferral has no owner / date / tracking ID; on single-PR branch this compounds invisibly. Verified Monitor does not call `AddDorcKafkaAvro`; consumer will receive null/Confluent-primitive-deserialise-error payloads. | **Accept.** §2 restructured: deferral now tracked as reserved **S-016** with "Must close before S-011" binding gate. IS R4 adds S-016 placeholder row. |
| G-R4-2 | Gemini | HIGH | §1 motivation overstates ("installer was never specified") — verified commit `012f3987` (S-009) did touch the Monitor installer and wrote to the wrong path. S-014 is S-009 remediation + S-009 omission, not pure new omission. | **Accept.** §1 rewritten: bullet 3 now names S-009 + commit `012f3987`; adds a framing paragraph that S-014 is (a) remediation of Monitor wrong-path write and (b) completion of API side never declared in any prior spec. |
| G-R4-3 | Gemini | MEDIUM | SchemaRegistry deferral lacks tracking ID; falls-through risk on one-PR branch. | **Accept.** §2 restructured: reserved as **S-015** with "Must close before S-011". IS R4 adds S-015 placeholder row. |
| G-R4-4 | Gemini | MEDIUM (overlaps S4-F3) | IS §3 S-014 detail says "No downstream step depends" yet narrative says "S-010 dry-run cannot close" — contradiction. | **Accept.** IS §3 S-014 "Dependencies" revised to name S-010 as downstream dependent explicitly. |
| G-R4-5 | Gemini | MEDIUM | Mitigation for "Monitor Kafka-block move breaks field instances" is optional ("if appropriate"). For a cutover, it must be mandatory. | **Accept.** §6 Row 1 mitigation now mandatory — Delivery adds the post-deploy root-vs-`AppSettings` check to `SPEC-S-010-Cutover-Runbook.md` R-1 smoke-test catalogue. |
| G-R4-6 | Gemini | LOW | Deploy-variables surface extension under-specified. | **Accept (minor).** §4 R4 now names `Setup.Dorc.msi.json` + `Install.Orchestrator.bat` as the single canonical surface, per GPT-F's independent verification. |
| G-R4-7 | Gemini | LOW | §7 prescribes commit ordering — violates CLAUDE.md §2 Abstraction Level. | **Accept.** §7 commit-ordering sentence removed; Delivery chooses granularity. |
| GPT-F1 | GPT-codex | MEDIUM (overlaps S4-F1) | `AuthMode = "SaslSsl"` default in API template + empty SASL creds → local-run validation fails on 3 keys, not 1. `appsettings.Development.json` is only a Logging override. | **Accept.** §4 R1 default AuthMode changed to `Plaintext` for dev ergonomics matching local compose; §4 R3/R4 **add literal `$.Kafka.AuthMode = "SaslSsl"` installer writes** to force prod posture per-env. |
| GPT-F2 | GPT-codex | LOW | Upgrade-install leaves orphan `$.AppSettings.Kafka` subtree; cosmetic dual-state file. | **Accept (minor).** §6 Risks adds a new row flagging the cosmetic dual-state and linking it to the same S-010 pre-flight check as G-R4-5. |
| GPT-F3 | GPT-codex | LOW | Name test-home candidate (`Dorc.Kafka.Client.Tests`) in R5 to pre-empt Delivery indecision. | **Accept (minor).** §4 R5 now names `Dorc.Kafka.Client.Tests` as the "encouraged" home. |
| GPT-F4 | GPT-codex | LOW | IS R4 block cites "Cycle-Limit + Fix Scope" — framing nitpick since R4 is a new-step revision not a cycle resubmission. | **Reject** — the wording invokes §4 Fix Scope Discipline correctly (minimum-effective-fix principle applies to any revision regardless of cycle semantics). No change. |
| GPT-F5 | GPT-codex | LOW | AC-6 vs §7 internal inconsistency re S-010 runbook edit. | **Accept (minor).** AC-6 rewritten to distinguish "cutover procedure unchanged" from "pre-flight checklist augmented". |
| S4-F1 | Sonnet | LOW | AuthMode default → dev-loop friction. | **Subsumed by GPT-F1.** |
| S4-F2 | Sonnet | LOW | R5 test-harness unknown. | **Defer to Delivery** — U-S014-2 correctly non-blocking. |
| S4-F3 | Sonnet | LOW | IS §3 S-014 "No downstream step depends" contradicts prose. | **Subsumed by G-R4-4.** |

All HIGH findings resolved; all MEDIUMs accepted with surgical edits; LOWs either accepted-minor or cleanly rejected with rationale. No fix-scope-discipline violations (edits proportional to findings; no implementation-level detail added).

Status transitions: `DRAFT` → `IN REVIEW (R2)` for resubmission against the same panel.

### R2 (2026-04-20) — UNANIMOUS APPROVAL

Same panel (simulated personas: Sonnet 4.6, Gemini Pro 3.1, GPT 5.3-codex). Scoped per CLAUDE.md §4 R2+ rules: verify R1 fixes, check regressions, no mining of unchanged text.

| Reviewer | Verdict | Regressions | Notes |
|---|---|---|---|
| Sonnet | APPROVE | None | All 12 R1 fix rows verified; all subsumed/deferred/rejected correctly handled. |
| Gemini | APPROVE | None | All 7 G-R4-* fixes verified proportional; live-code citations re-verified (commit `012f3987` confirmed; `Setup.Dorc.msi.json:225–239` confirmed). No scope creep. |
| GPT-codex | APPROVE WITH MINOR | None | All 5 GPT-F* fixes verified. One new finding GPT-R2-F1 (LOW): U-S014-1 Unknown is stale now that §4 R4 asserts property reuse. Disposition: **Defer to Delivery** — doc hygiene nit; does not gate R2 approval. |

**Unanimous clean approval.** Per CLAUDE.md, status transitions to `APPROVED`. Per memory `project_kafka_autopilot`, user auto-pilot satisfies the "Pending user approval" stage.

_Delivery task carry-over_: GPT-R2-F1 — update §10 Unknowns Register U-S014-1 to "RESOLVED via §4 R4 paragraph; MSI property reuse verified." This is a one-line edit to be folded into the Delivery commit rather than a separate spec revision.

---

### Code Review R1 (2026-04-20) — APPROVE WITH MINOR (unanimous, one MEDIUM accepted)

Same panel. Reviewed the unstaged diff against SPEC-S-014 R1–R7 and AC-1..AC-8.

| Reviewer | Verdict | Findings summary |
|---|---|---|
| Sonnet | APPROVE WITH MINOR | F1 LOW (Monitor SchemaRegistry sub-block added — Downgrade/Defer, symmetry improvement); F4 INFO (U-S014-1 doc hygiene). |
| Gemini | APPROVE WITH MINOR | **F-1 MEDIUM** — Monitor SchemaRegistry sub-block violates R2 literal "no key added" wording. Recommended fix Option (a): one-line spec clarification permitting symmetry-driven placeholder. |
| GPT-codex | APPROVE WITH MINOR | F-1 echoes SchemaRegistry placement, characterised as shape-parity improvement; F-5 commit hygiene flag (untracked `.claude/`, `CLAUDE.md`, `NUL`, `docs/ui-declutter/` must not be staged). |

Aggregate verdict: UNANIMOUS APPROVE WITH MINOR. All three converged on the SchemaRegistry placeholder as a net-positive shape-parity improvement; Gemini's MEDIUM was the strict-R2 literal-reading concern. Per CLAUDE.md Fix Scope Discipline, the author applied Gemini's Option (a): a single-paragraph clarification to SPEC §4 R2 explicitly permitting the blank `SchemaRegistry` sub-block on shape-symmetry grounds and disclaiming installer writes (S-015's domain). No code files changed.

### Code Review R2 (2026-04-20) — UNANIMOUS APPROVE

Same panel, scoped to fix verification per CLAUDE.md §4 R2+ rules.

| Reviewer | Verdict | Notes |
|---|---|---|
| Sonnet | APPROVE | Retry after initial timeout (CLAUDE.md §4 Reviewer Reliability — one-time retry permitted). Fix is minimal, closes F-1, preserves core relocation requirement, no delivery-level detail leaked, scope narrowly clauseed to SchemaRegistry placeholder. |
| Gemini | APPROVE | F-1 closure verified on all 5 criteria (core relocation retained; blank SchemaRegistry permitted; installer writes correctly disclaimed to S-015; no pre-existing key removed; edit proportional). No regressions. |
| GPT-codex | APPROVE | Permission narrowly scoped to three named keys, forbids installer writes, preserves R3/R4 installer logic, leaves S-015 binding gate intact. No regressions. |

**Unanimous Code Review approval.** Per CLAUDE.md §4 this is the final quality gate before squash-merge. Commit-hygiene flag (GPT-F5 R1): stage only the S-014 files by explicit path — exclude `.claude/`, `CLAUDE.md`, `NUL`, `docs/ui-declutter/` which are unrelated working-tree artefacts.
