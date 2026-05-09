# IS: Terraform Hardening & Stock-Module Library Foundation

| Field      | Value                                |
|------------|--------------------------------------|
| **Status** | APPROVED — round-2 panel (Opus, Sonnet, Haiku): 3× APPROVE clean. 2026-05-09. |
| **Author** | Agent                                |
| **Date**   | 2026-05-09                           |
| **HLPS**   | docs/terraform-hardening/HLPS-terraform-hardening.md (APPROVED 2026-05-09) |
| **Branch** | claude/terraform-hardening           |

---

## Pre-audit findings (resolves U-5, U-8; informs every step)

- **Test idiom**: MSTest 4.0.1 + NSubstitute 5.3.0. No xUnit, no Moq, no FluentAssertions.
- **Target framework**: `net8.0` for runtime projects with `ImplicitUsings` + `Nullable` enabled. New project `Dorc.Terraform.Catalog` matches.
- **Logger surface**: production code logs through `IRunnerLogger` (custom interface over `Microsoft.Extensions.Logging.ILogger`). Tests verify log content by **NSubstituting `IRunnerLogger` and using `Received()` + argument-capture** — this is the repo idiom and is the mandated mechanism for any test that asserts on log content (S-004 specifically).
- **Existing CI** (`.github/workflows/`): `release.yml`, `release-publish.yml`, `README.md` only. No terraform installer step. **U-8 RESOLVED — green-field.**
- **Existing secret-marking convention**: zero hits on `IsSecret`/`IsSensitive`/`SecretProperty`/`SensitiveProperty` in `Dorc.ApiModel` and `Dorc.Core`. **U-5 RESOLVED — must introduce.** Decision: configuration-driven `SensitivePropertyNamePatterns` (regex list, default `(?i)(token|pat|secret|password|key|connectionstring)`), applied as a redactor at every property-serialization log boundary.
- **Test projects already exist**: `Dorc.Api.Tests`, `Dorc.Monitor.Tests`, `Dorc.Core.Tests`, `Dorc.Monitor.IntegrationTests`. We add `Dorc.TerraformRunner.Tests` and `Dorc.Terraform.Catalog.Tests`, plus expand the existing three.
- **Module choice for the third stock module**: **storage-account** (committed; no JIT-spec deferral). Rationale: foundational; pairs naturally with `vnet`; demonstrates the composition seam without bringing composition into HLPS scope.

---

## Step ordering principle

1. **Foundations** (S-001..S-002): test scaffolding, secret-redaction primitive, safe-zip primitive — used by later steps.
2. **Hardening of the existing pipeline** (S-003..S-005, S-006a..d): process spawning, secret flow, RBAC, then the consolidated state + lifecycle work split into four sub-steps.
3. **Library platform** (S-007..S-008, S-009..S-011): catalog API, source-provider integration, mutual-exclusion validation, stock modules. **Cannot start before S-006d**.
4. **Cross-cutting polish** (S-012..S-016): docs, CI, frontend, naming audit + dead-code + coverage + flag removal.

A step is "done" only when: tests are written and pass; an adversarial sub-step review (single-reviewer for normal steps, full panel for steps marked `*PANEL*`) records no unresolved HIGH/CRITICAL findings; the documented success criterion is verifiable.

---

## Steps

### S-001 — Secret-redaction primitive + new test project

**SC**: SC-04 (foundation), SC-16 (foundation).

**Outcome**: A `Redactor` class (final cohesion-first name in JIT spec) exists in `Dorc.TerraformRunner` (placement: keep within an existing assembly until a second consumer appears; promote to a shared library only when justified). Reads `SensitivePropertyNamePatterns` from configuration with the default supplied; exposes `RedactProperties(IDictionary<string,string>) → IDictionary<string,string>` and `RedactJson(string) → string`.

A new test project `Dorc.TerraformRunner.Tests` is created (MSTest+NSubstitute idiom, matching `Dorc.Monitor.Tests`).

**Tests** (test-first, drive the API): redacts well-known secret property names; passes-through non-secret names; respects custom configured patterns; idempotent; round-trip on a JSON property bag.

**Adversarial review**: single reviewer (Sonnet).

---

### S-002 — Safe ZIP extraction primitive

**SC**: SC-06.

**Outcome**: A single class owns ZIP extraction. Validates per-entry path containment via `Path.GetFullPath` canonicalisation, enforces caps (10 000 entries / 50 MiB per entry / 500 MiB total — all configurable), rejects symlinks, rejects zero-length entry names, rejects absolute paths and `..` paths. Throws a typed `UnsafeArchiveException` (final name in JIT spec) carrying a machine-readable reason. **Replaces both `ZipFile.ExtractToDirectory` call sites in `AzureArtifactCodeSourceProvider`.**

**Pre-audit (cohesion-first)**: name candidates `BoundedZipExtraction`, `ProtectedZipExtraction`, `SafeArchiveReader` — final committed in JIT spec after the cohesion test. *Not* `*Helper`/`*Util`.

**Tests** (test-first): fixture archives — path-traversal entry, oversized entry (per-entry and total), absolute-path entry, symlink, zero-length name, normal archive. Each rejected fixture asserts the typed exception and that no file is written (rollback-safe).

**Adversarial review**: single reviewer (Sonnet — security focus).

---

### S-003 — Safe process spawning (`ArgumentList` + correct exit-code semantics)

**SC**: SC-03.

**Outcome**:
- Every `ProcessStartInfo` invocation in `Dorc.TerraformRunner` and `Dorc.Monitor.RunnerProcess` uses `ArgumentList`. Where Win32 `CreateProcessAsUser` requires a single command-line string, a tested `Win32CommandLineQuoting` helper (final cohesion-first name in JIT spec) round-trips a known-bad set of inputs.
- `RunTerraformCommandAsync` becomes `RunTerraformCommandAsync(TerraformCommand command, …)` with `TerraformCommand` an enum (`Init`, `PlanDetailedExitCode`, `Apply`, `Show`). Exit-code interpretation per command per HLPS SC-03.
- Cancellation invokes `process.Kill(entireProcessTree: true)` deterministically before returning.

**Test mechanism (committed in this IS, not deferred)**: a new project `Dorc.TerraformRunner.Tests.FakeCli` is added to the solution, producing a stub console application (`fake-terraform.exe`). The stub reads the first argument and either exits with a configured code, or sleeps for cancellation tests. Tests start the system under test with `PATH` modified locally (within the test's `Process` environment block, not process-globally) so that `terraform` resolves to the stub. This is parallel-safe because each test creates its own stub-bin directory (`Path.GetTempFileName()`-derived) and the modified `PATH` is scoped to the spawned child process via `ProcessStartInfo.Environment`.

**Tests** (test-first):
- Each `TerraformCommand` value: stub returns each of `0`, `1`, `2`; assert outcome per HLPS SC-03 semantics.
- Argument-list test: inject path with spaces, quotes, ampersands; stub echoes its arguments to a temp file; assert literal value is preserved.
- Cancellation test: long-running stub; cancel after 200 ms; assert no `fake-terraform` process by name within 5 s.

**Adversarial review**: single reviewer (Sonnet).

---

### S-004 — Secret-flow redaction at log sites + reworded warning

**SC**: SC-04 (log-redaction half).

**Note (revised round 2)**: Named-pipe ACL hardening originally scheduled here has been **moved to S-006d**, since S-006d redraws the dispatcher↔runner boundary and may relocate the pipe-creation site. Co-locating reduces churn. S-004 retains the in-process log-redaction work.

**Outcome**:
- `ScriptGroupPipeClient.cs` and `ScriptGroupFileReader.cs` — every property-serialization log line goes through the S-001 redactor.
- `TerraformSourceConfigurator.cs` — the warning string "PAT token not found in properties. Expected property: Terraform_Git_PAT" is reworded to not leak the property name.
- The plan-show output written by `TerraformProcessor` is no longer logged at Information; it is written only to the working directory.

**Test mechanism**: `IRunnerLogger` is NSubstituted; tests use `Received().Information(Arg.Is<string>(s => …))` argument capture to assert no plaintext secret value appears in any log call.

**Tests** (test-first):
- Pipe client: substitute logger; invoke; assert no captured log argument contains a known secret value.
- File reader: same shape.
- Plan-output test: spawn fake terraform with output containing a known sensitive marker; assert the body is written to disk but no Information-level log line carries it.

**Adversarial review**: single reviewer (Sonnet).

---

### S-005 — RBAC enforcement in `TerraformController`

**SC**: SC-02.

**Outcome**: `HasViewPermission`, `HasConfirmPermission`, `HasDeclinePermission` delegate to `IApiSecurityService` using the request's environment + project context (matching the rest of `Dorc.Api`).

**Pre-audit**: read `IApiSecurityService` to identify the right environment+project-scoped check method; pick the existing helper rather than inventing one.

**Tests** (test-first, NSubstitute the security service): forbidden user → 403; allowed user → 200; missing/deleted environment → 404; mixed role.

**Adversarial review**: single reviewer (Sonnet).

---

### S-006a — State backend rendering + pre-flight rejection of user backend blocks

**SC**: SC-01 (backend half), SC-05.

**Outcome**: Before `terraform init`, DOrc renders `_dorc_backend.tf` with the Azure Blob `azurerm` backend block, `key = "{project}/{component}/{environment}.tfstate"`. A pre-flight pass over the source rejects any `*.tf` file containing a `terraform { backend ... }` declaration, returning a clear validation error citing the offending file.

**State container security (SC-05)**: configuration defaults set: container access = `private` (no anonymous); SAS-lifetime cap = ≤ 1 hour where SAS is used; the state-storage account/container/RBAC-role-assignment is documented in S-012's STATE-MODEL.md (deliverable on S-012, configuration default is on this step).

**Tests** (test-first):
- Backend.tf renderer: deterministic output for given `(project, component, environment)`.
- Pre-flight scanner: a fixture `*.tf` file containing a backend block triggers rejection with a precise error string; a clean fixture does not.
- Configuration default: assert `Terraform:State:Container:AccessLevel == "private"` is the default and that any `Terraform:State:Sas:LifetimeMinutes` value > 60 is rejected at startup.

**Adversarial review**: single reviewer (Opus).

---

### S-006b — Execution-bundle tarball + SHA-256 round-trip

**SC**: SC-01 (continuity half), R-2 mitigation.

**Outcome**: Post-`init` working directory + `.terraform.lock.hcl` + binary plan file are packaged as `bundles/{planOperationId}.tar.gz`. SHA-256 is computed over the tarball and persisted on the plan record. Apply downloads by `planOperationId`, recomputes SHA-256, compares; mismatch fails fast. The `planOperationId` is a GUID generated by the lifecycle owner at plan time.

**Pre-audit**: confirm the persistence layer for the plan record (likely `Dorc.PersistentData`) — additive column or an existing JSON property bag. Decision committed in JIT spec.

**Tests** (test-first):
- Bundle round-trip: pack a fixture working directory, hash, unpack to a fresh path, compare directory tree byte-for-byte.
- Hash mismatch: corrupt one byte of the tarball; unpack call asserts a typed `BundleIntegrityException`.
- Per-operation key: two parallel packs to the same blob container with different `planOperationId` produce two different blob names; both succeed; neither overwrites the other.

**Adversarial review**: single reviewer (Sonnet — security/integrity focus).

---

### S-006c — Concurrency: in-process semaphore + Azure Blob lease on state blob

**SC**: SC-01 (concurrency invariant), R-1 mitigation.

**Outcome**: For each `(project, component, environment)` triple, the lifecycle owner serialises plan and apply via:
- An in-process `SemaphoreSlim` keyed by the triple (single-monitor case), and
- An explicit Azure Blob lease (15-second duration with auto-renewal) on the state blob, acquired by the lifecycle owner before invoking terraform (multi-monitor case).

If lease acquisition fails (lease held), the system surfaces a clean user-visible error (`409 Conflict` or equivalent) with the contending operation's identifier, rather than relying on terraform's internal lock-wait timeout.

**Test mechanism (committed)**: concurrency test uses a `CountdownEvent` latch. Both tasks register a fake terraform CLI that signals the latch on entry and waits on a release signal. The test starts both tasks, waits for both to be "in flight" (latch count = 0), then releases. This guarantees the loser path is exercised. Without the latch, the test is a flake — explicit countermeasure for round-1 review finding.

**Tests** (test-first):
- Single-monitor: two `Task.WhenAll` plan triggers on the same triple — one acquires semaphore, the other receives `409` after `latch` orchestration.
- Multi-monitor (simulated): NSubstitute the blob-lease client; one acquisition succeeds, the second receives `LeaseAlreadyPresent` and produces `409`.
- Lease auto-renewal under a long-running plan does not expire mid-flight.

**Adversarial review**: single reviewer (Sonnet — concurrency).

---

### S-006d — Stateless runner + dispatcher thinning + lifecycle owner + named-pipe ACL + feature flag `*PANEL*`

**SC**: SC-07, SC-04 (pipe-ACL half).

**This is the architectural cut. `*PANEL*` (full 3-model adversarial review).**

**Outcome**:
- A new class on the Monitor side owns the plan→confirm→apply state machine. Working name **`TerraformPlanExecution`** (one execution; final commit in JIT spec after the cohesion test).
- `TerraformDispatcher` becomes a thin queue → lifecycle adapter; no `BlobContainerClient` calls remain.
- The runner takes (working directory, operation, plan-file path) and emits (exit code, log stream, binary plan file). It persists no state between invocations.
- The named pipe between Monitor and TerraformRunner (whichever class owns its creation post-refactor) is created with `PipeSecurity` granting access only to the runner's SID + LocalSystem on Windows.
- The work lands behind feature flag `Terraform:UseConsolidatedLifecycle` (default `true` in dev/test; can be set `false` for production rollback). The legacy code path is preserved behind the flag and exercised by tests.

**Pre-audit**:
- Read `TerraformDispatcher.cs` and `TerraformProcessor.cs` end-to-end to map current responsibilities to the new boundary.
- Identify the persistence site for `planOperationId` + SHA-256 (probably `Dorc.PersistentData`).
- Confirm `PipeSecurity` API surface on `NamedPipeServerStream` (`PipeAccessRule`, `PipeAccessRights`, NetworkSid resolution).

**Feature-flag testability (committed)**: tests inject `Terraform:UseConsolidatedLifecycle = false` into a test `IConfiguration`; NSubstitute on the legacy dispatcher branch asserts the legacy code path is invoked. With `= true`, the new lifecycle path is invoked. Both paths covered.

**Tests** (test-first):
- Lifecycle owner contract: plan→apply round-trip against the fake terraform CLI; bundle uploaded; plan record holds GUID + SHA-256; apply downloads, hash verifies; state-key continuity confirmed.
- Runner statelessness contract: invoking the runner twice with the same inputs produces identical outputs (modulo logs).
- Dispatcher thinning: `TerraformDispatcher` post-refactor contains zero `BlobContainerClient` references (verified by a code-search test or a strict assembly-graph test).
- Named-pipe ACL: open the pipe under a non-runner user / non-LocalSystem identity; assert `UnauthorizedAccessException`. (Windows-conditional, matches the `RunnerProcessCancellationTests` idiom.)
- Feature flag: both `true` and `false` paths covered.

**Adversarial review**: full panel (Opus, Sonnet, Haiku) — same charters as the original review.

---

### S-007 — `Dorc.Terraform.Catalog` library + API endpoints (merged) `*PANEL*-LIGHT*`

**SC**: SC-08, SC-11 (scaffold half).

**Depends on**: S-006d.

**Outcome (library half)**:
- New project `Dorc.Terraform.Catalog`, target `net8.0`.
- Types:
  - `TerraformTemplateManifest` (immutable record).
  - `ITemplateCatalog` — `ListAsync`, `GetAsync(name)`, `GetAsync(name, version)`.
  - `GitTemplateCatalog` — reads `manifests/<name>-<version>.yaml` files from the configured Git repo at `main` (one file per template/version; aligns with stock-modules tag scheme `stock-modules/<name>/v<version>`).
  - `IParameterValidator` — validates supplied `Dictionary<string,string>` against the manifest's parameter schema.
- Manifest JSON Schema is generated using **NJsonSchema** (already common in .NET; pre-audit confirms version availability) and committed to `docs/Terraform/MANIFEST-SCHEMA.json` for IDE/CI tooling.
- YAML parser: `YamlDotNet` (verified present in repo dependency graph during pre-audit; flagged for confirmation).

**Outcome (API endpoints half)**:
- `GET /api/terraform/templates` → list of manifests.
- `GET /api/terraform/templates/{name}` → latest manifest.
- `GET /api/terraform/templates/{name}/{version}` → exact version.
- `POST /api/terraform/templates/{name}/{version}/scaffold` → returns a tarball stream of the template source pinned at that version (assembled by the catalog with a `provenance.txt` recording catalog name+version). Producer side uses standard `System.IO.Compression` tar; **does not depend on S-002** (S-002 is consumer-side).
- All endpoints require existing auth.

**Tests** (test-first):
- `IParameterValidator`: required missing → fail; type mismatch → fail; valid → pass; allowed-values respected; regex respected.
- `GitTemplateCatalog` against a local repo fixture.
- Manifest schema round-trip: a sample manifest validates against the schema.
- API contract: each endpoint, including auth checks; scaffold tarball is deterministic given `(name, version)`.

**Adversarial review**: light panel (Opus + Haiku — design judgement + DX).

---

### S-008 — Source-provider integration with catalog references

**SC**: SC-11 (reference half).

**Depends on**: S-007.

**Outcome**: `TerraformCodeSourceProviderFactory` (final post-refactor name re-evaluated in JIT spec) recognises a fourth source mode: catalog reference (`Terraform_Template_Name` + `Terraform_Template_Version` set on the component property bag). A new `CatalogReferenceCodeSourceProvider` resolves the manifest via `ITemplateCatalog`, then delegates fetching to `GitCodeSourceProvider` pinned to the manifest's source tag.

**Tests** (test-first): factory selects catalog provider when template name is set; integration: end-to-end resolution of `(name, version)` into a Git source against a local repo fixture.

**Adversarial review**: single reviewer (Opus — boundary placement).

---

### S-009 — Component-save mutual-exclusion validation

**SC**: SC-12.

**Depends on**: S-007.

**Outcome**: When both `Terraform_Template_Name` and a direct `ScriptsLocation`/`TerraformSourceType` are present on a component, the system rejects the component at save time with `400` and a precise error string.

**Pre-audit (committed location)**: validation lives in **`Dorc.Api`** at the component-save controller boundary (the layer that already exposes the surface for component-save), implemented as a request-validator. `Dorc.PersistentData` is *not* the placement target — keeping the validator in the API surface preserves layering.

**Tests** (test-first, in `Dorc.Api.Tests`): both modes set → 400 with expected error string; only catalog-ref → 200; only direct → 200; neither → existing default-validation rules apply.

**Adversarial review**: single reviewer (Opus).

---

### S-010 — Stock modules: `vnet` + corrected `sql-database` + `storage-account`

**SC**: SC-09.

**Note on TDD discipline**: Per CLAUDE.md C-02, this work follows test-first where it can. Terraform HCL has no meaningful pre-implementation test mechanism; `terraform validate` and `tflint` are by-construction post-implementation verifications. **This is an acknowledged exception to C-02 for HCL artefacts only.** C# code introduced in adjacent steps remains test-first.

**Outcome**: three modules under `stock-modules/` at repo root:
- `stock-modules/vnet/` — Azure vnet + subnets + NSGs; `validation` blocks for CIDRs/address space/subnet count; outputs `vnet_id`, `subnet_ids`, `nsg_ids`.
- `stock-modules/sql-database/` — corrected from the existing example: hardcoded password default removed; `validation` blocks for server name, db name, edition allow-list; output adds `sql_server_id`.
- `stock-modules/storage-account/` — Azure storage account; `validation` for name (3–24 lowercase alnum), redundancy allow-list; outputs `storage_account_id`, `primary_blob_endpoint`.

Each module ships: `main.tf`, `variables.tf`, `outputs.tf`, `versions.tf` (`required_version` + `required_providers` pinned), `README.md` (per MODULE-CONTRACT, including `Owner:` field — initial owner: "DOrc platform team"), `examples/basic/`, manifest entry under `manifests/`.

**Tests**: `terraform fmt -check` + `terraform validate` (in S-013 CI) + `tflint` (in S-013 CI). Per-module unit-of-work test: synthetic minimal `terraform.tfvars` validates against the manifest's parameter schema (covered by S-007 `IParameterValidator` tests using each module's manifest as a fixture).

**Adversarial review**: single reviewer (Haiku — DX/template focus); reviews all three modules together as one diff.

---

### S-011 — Frontend plan diff

**SC**: SC-13.

**Outcome**: `terraform-plan-dialog.ts` parses plan output line-by-line: lines starting with `+` rendered green, `-` red, `~` yellow; WCAG-AA contrast (≥ 4.5 : 1); sensitive-value pattern (`(sensitive value)` literal + the SC-04 secret pattern) masks the value. Empty `PlanContent` renders an error state with the error string from the plan record.

**Tests** (test-first): component-level tests on the dialog with known plan inputs assert class assignment per line, mask behaviour, and error-state rendering.

**Adversarial review**: single reviewer (Haiku — DX).

---

### S-012 — Documentation: STATE-MODEL, MODULE-CONTRACT, MODULES index, setup-example rewrite

**SC**: SC-14, R-3 mitigation half.

**Outcome**:
- `docs/Terraform/STATE-MODEL.md` — state ownership, container provisioning, RBAC-role assignment example, encryption/SAS guidance, secrets-in-state caveat (R-3) including a runbook reference for `terraform force-unlock` (R-8).
- `docs/Terraform/MODULE-CONTRACT.md` — required files + content; **explicitly prohibits `sensitive = false` on outputs that carry provider-returned secrets** (R-3 enforcement); versioning (semver); deprecation policy (90-day notice, README banner, manifest `Deprecated` flag); publishing convention (Git tag `stock-modules/<name>/v<X.Y.Z>`).
- `docs/Terraform/MODULES.md` — index of all stock modules with name, version, owner, status, category.
- `docs/Terraform/terraform-source-configuration.md` — rewritten to match implemented reality.
- `docs/Terraform/terraform-setup-example.md` — rewritten with state-backend setup, secret-property setup, catalog-reference example, scaffold example.
- `docs/Terraform/examples/terraform-project/` — minimal, references `stock-modules/sql-database`.

**Adversarial review**: single reviewer (Haiku — readability).

---

### S-013 — Module CI workflow + provider mirror strategy

**SC**: SC-10, R-3 enforcement, R-5 mitigation.

**Outcome**: `.github/workflows/terraform-modules-ci.yml` runs on PRs touching `stock-modules/**` or `docs/Terraform/**`:
- Set up Terraform CLI (pinned version).
- For each stock module:
  - `terraform fmt -check`
  - `terraform init -backend=false`, configured to use a **committed plugin mirror directory** at `stock-modules/.terraform-mirror/` (provider binaries + lockfile committed and CI-verified). Mirror is configured via `TF_CLI_ARGS_init=-plugin-dir=...` (exact mechanism in JIT spec — `network-mirror` configuration in `.terraformrc` is the alternative; one is chosen, the other rejected).
  - `terraform validate`
  - `tflint` with plugin SHAs pinned in `.tflint.hcl`.
- A custom check (script or tflint custom rule, decided in JIT spec) enforces "no `sensitive = false` on outputs whose names match the SC-04 secret pattern" — R-3 enforcement.
- Verifies each module's `.terraform.lock.hcl` is committed and current.
- Workflow blocks merge on failure.

**Pre-audit**: `setup-terraform` action version; tflint plugin pinning by SHA; provider mirror layout.

**Tests**: a deliberately broken module branch (path-traversal, missing required field, `sensitive = false` on a known-secret output) — pushed for one-shot verification; deleted post-verification. Each broken category has a recorded outcome in `docs/terraform-hardening/CI-VERIFICATION-LOG.md`.

**Adversarial review**: single reviewer (Sonnet — CI security).

---

### S-014 — File-rename + dead-code + DI cleanup (no architectural changes)

**SC**: SC-15 (partial), housekeeping.

**Note on revised round 2**: this step is now strictly bounded — **no naming-audit content; no new-class naming compliance** (those land in S-015 in a single audit pass).

**Outcome**:
- `TerrafromRunnerOperations.cs` renamed to `TerraformRunnerOperations.cs`; the enum `TerrafromRunnerOperations` similarly; all callers update.
- `DirectoryHelper.cs` is split into two cohesive types named for their responsibility (final names committed in JIT spec after reading current method set). The grab-bag `Helper` name is removed.
- Dead `TerraformExecutionResult` private classes (declared in two files, used in neither — verified by a grep at JIT-spec time) removed.
- `TerraformPlanApiModel.BlobUrl` — verified unused by all callers in JIT pre-audit; removed if confirmed.
- The double `ConfigurationBuilder` reads inside `TerraformDispatcher.Dispatch` are consolidated; configuration is injected via DI.

**Tests**: existing tests must continue to pass (compilation barrier); new tests for any new cohesive types.

**Adversarial review**: single reviewer (Opus — judgement on `DirectoryHelper` split).

---

### S-015 — Naming audit + coverage report + flag removal + final docs alignment

**SC**: SC-15, SC-16, SC-17.

**Outcome**:
- `docs/terraform-hardening/NAMING-AUDIT.md` lists every new C# class introduced by S-001..S-014, its sole responsibility (one sentence, no "and"), and pass/fail. Failures are renamed in-place. Existing grab-bag names in touched paths are reassessed.
- `dotnet test --collect:"XPlat Code Coverage"` runs across all test projects; coverage report committed to `docs/terraform-hardening/COVERAGE-REPORT.md` (or attached as a build artefact, decision in JIT spec). SC-16's "non-trivial coverage" gate is verified by this report.
- **Feature flag removal exit criterion (committed)**: `Terraform:UseConsolidatedLifecycle` is **removed in this step** unconditionally. If the user requires staged production rollout outside this branch, the IS is updated *now* (before S-001 executes) to defer flag removal to a named follow-up; otherwise removal is mandatory at S-015. This closes the round-1 review's open fork.
- Final pass on `docs/terraform-hardening/REVIEW-ESCALATIONS.md` — populated by the final-review rounds; this step closes it out.

**Adversarial review**: single reviewer (Opus — judgement).

---

### S-016 — Final integrated adversarial review (max 3 rounds, per HLPS SC-17)

**Outcome**: full 3-model panel (Opus, Sonnet, Haiku) reviews the entire diff. Triage Accept/Downgrade/Defer/Reject. Max 3 rounds; residual HIGH/CRITICAL findings recorded in `docs/terraform-hardening/REVIEW-ESCALATIONS.md`. Per HLPS SC-17, findings introduced by remediations during later rounds count toward the cap.

---

## Step → SC coverage table (post-revision)

| SC | Step(s) |
|----|---------|
| SC-01 | S-006a (backend), S-006b (continuity), S-006c (concurrency invariant) |
| SC-02 | S-005 |
| SC-03 | S-003 |
| SC-04 | S-001 (foundation), S-004 (log redaction), S-006d (pipe ACL) |
| SC-05 | S-006a (config defaults), S-012 (docs) |
| SC-06 | S-002 |
| SC-07 | S-006d |
| SC-08 | S-007 |
| SC-09 | S-010 |
| SC-10 | S-013 |
| SC-11 | S-007 (scaffold), S-008 (reference) |
| SC-12 | S-009 |
| SC-13 | S-011 |
| SC-14 | S-012, S-014 (final docs alignment) |
| SC-15 | S-014 (housekeeping renames), S-015 (audit) |
| SC-16 | every step's tests (foundation in S-001), S-015 (coverage report) |
| SC-17 | S-015 (close-out), S-016 (final review) |

Every SC is covered by at least one step.

---

## Risk → step mapping (post-revision)

| HLPS Risk | Mitigated by step(s) |
|-----------|----------------------|
| R-1 (concurrent plan/apply) | S-006c |
| R-2 (bundle race) | S-006b |
| R-3 (secrets in state) | S-012 (MODULE-CONTRACT prohibition), S-013 (CI enforcement) |
| R-5 (CI supply chain) | S-013 (provider mirror, pinned plugin SHAs, lockfile verification) |
| R-6 (rollback) | S-006d (feature flag introduction), S-015 (controlled removal) |
| R-7 (state-account ownership) | S-012 (STATE-MODEL.md) |
| R-8 (hard kill mid-apply) | S-012 (force-unlock runbook reference) |
| R-9 (CI noise) | S-013 (path-filter) |

R-4 was retired during HLPS round-2.

---

## Approval

This document requires adversarial-panel approval before S-001 may execute. Findings triaged Accept/Downgrade/Defer/Reject; max 3 rounds.

### Round-1 panel outcome (2026-05-09)

Three reviewers (Opus, Sonnet, Haiku); 3× APPROVE-WITH-REVISIONS. Convergent + accepted findings applied in this revision:
- **Split S-007** into S-006a..d; `*PANEL*` retained only on S-006d (architectural cut).
- **Moved named-pipe ACL** from S-004 into S-006d (co-located with the boundary that may relocate it).
- **Merged S-008+S-009** of the round-1 numbering into a single S-007 (catalog library + endpoints).
- **Bounded S-006/S-014** to file/enum rename + dead-code + `DirectoryHelper` split + DI cleanup; **moved naming-audit work to S-015** as a single audit pass (no duplication).
- **Committed S-013 module choice** to `storage-account` (not deferred).
- **Specified S-003 fake-CLI mechanism**: stub project + scoped PATH for parallel safety.
- **Specified S-004 log-capture**: NSubstitute `IRunnerLogger` with argument capture.
- **Specified S-006c concurrency latch** to eliminate flake risk.
- **Specified S-006d feature-flag testability**: configuration injection + branch verification.
- **Specified S-013 provider-mirror strategy**: committed plugin mirror directory; pinned plugin SHAs.
- **Added MODULE-CONTRACT prohibition** on `sensitive = false` outputs (R-3) + CI enforcement.
- **Acknowledged S-010 TDD exception** for HCL artefacts.
- **Concrete S-015 flag-removal exit criterion**: removed unconditionally; user opt-out must update IS before S-001.
- **Added SC-16 coverage-report deliverable** in S-015.
- **Added explicit SC-05 configuration defaults** in S-006a.

### Round-2 panel charter

Round-2 reviewers should focus on (a) whether round-1 findings were addressed substantively, (b) whether revisions introduced new defects, (c) whether the split of S-007 into S-006a..d is itself the right granularity, (d) whether the SC coverage table is now complete. Approval-or-revision; reject only on genuine new defects.
