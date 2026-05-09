# HLPS: Terraform Hardening & Stock-Module Library Foundation

| Field      | Value                                |
|------------|--------------------------------------|
| **Status** | APPROVED — round-2 panel (Opus, Sonnet, Haiku): 2× APPROVE + 1× APPROVE-WITH-REVISIONS (single MEDIUM addressed: per-operation tarball keying). 2026-05-09. |
| **Author** | Agent                                |
| **Date**   | 2026-05-09                           |
| **Folder** | docs/terraform-hardening/            |
| **Branch** | claude/terraform-hardening           |

---

## 1. Problem Statement

The DOrc Terraform implementation (`Dorc.Api` → `Dorc.Monitor` → `Dorc.TerraformRunner`, plus the example project under `docs/Terraform/`) was reviewed by a 3-model adversarial panel on 2026-05-09. Two of three reviewers returned **REJECT**. The implementation has defects in three intersecting categories:

1. **Correctness defects that can produce data loss in production.** Terraform state is not platform-managed: `init` runs without backend configuration, the working directory is destroyed between plan and apply, and `apply` exit-code handling treats only `1` as failure. These combine so that successful applies can be reported green while underlying resources are silently re-created on each run.
2. **Security defects that allow privilege escalation and secret leakage.** All three RBAC checks in `TerraformController` are stubbed to `return true`. The runner command line is built by string concatenation with paths. ZIP archives from artifact sources are extracted with no entry validation, no path containment, and no size cap. PAT and bearer tokens are written to logs as part of `ScriptGroup` property dumps. The named pipe carrying secrets has no ACL.
3. **Goal-fit gap.** The user's stated end goal is "a library of default and stock terraform models for engineers to start from when building applications." The current code is a per-component fetcher-and-runner, not a library platform — there is no template catalog, no versioned module references, no parameter-schema contract, no module composition, no per-module README convention, no validation/CI pipeline, and no module-publishing channel. The single example terraform project ships a hardcoded password default and references a `sql-managed-instance` module that does not exist.

The consequence is that DOrc cannot today host engineer-authored or stock-authored Terraform without exposing those engineers — and the production estate — to silent state corruption, secret leakage, and authorization bypass.

User-confirmed context (2026-05-09): the existing Terraform implementation is **not yet in production use** (U-3 = NO), so no state-import migration is required. Stock modules will be consumed by both **reference** (default, pinned semver) and **scaffold** (escape hatch where engineers want a forkable starting point) — see SD-7/SD-14.

---

## 2. Scope

**In scope:**
- `src/Dorc.Api/Controllers/TerraformController.cs` — RBAC, request validation, error surface.
- `src/Dorc.Monitor/TerraformDispatcher.cs`, `src/Dorc.Monitor/ITerraformDispatcher.cs` — orchestration, plan/apply lifecycle, blob-storage I/O ownership.
- `src/Dorc.Monitor/TerraformSourceConfig/TerraformSourceConfigurator.cs` — secret flow into runner.
- `src/Dorc.Monitor/RunnerProcess/TerraformRunnerProcessStarter.cs` — process spawning, command-line construction.
- `src/Dorc.TerraformRunner/` (entire project) — `Program.cs`, `TerraformProcessor.cs`, `Options.cs`, `TerrafromRunnerOperations.cs` (the file name itself contains a typo we will correct as part of this work), `CodeSources/*`, `Pipes/*`.
- `src/Dorc.ApiModel/TerraformPlanApiModel.cs`, `TerraformSourceType.cs`.
- `src/dorc-web/src/components/terraform-plan-dialog.ts` — plan dialog UX.
- `docs/Terraform/` — documentation alignment with reality.
- `docs/Terraform/examples/terraform-project/` — example correctness, secret hygiene.
- **New code**: `Dorc.Terraform.Catalog` library, per CLAUDE.md naming, providing the template-catalog API. (Assembly name to be reconciled with CLAUDE.md `Dorc.<Component>.dll` pattern in IS-pre-audit.)
- **New stock modules**: at minimum `vnet`, plus a corrected `sql-database`, plus a third foundational module (decision committed in IS).
- **New tests**: `Dorc.TerraformRunner.Tests`, expansions to `Dorc.Monitor.Tests` and `Dorc.Api.Tests` covering Terraform paths.
- **CI**: `terraform validate` / `terraform fmt -check` / `tflint` workflow for stock modules, with provider-mirror configuration to mitigate supply-chain risk.

**Out of scope:**
- Terraform CLI itself (we treat the CLI as a trusted dependency).
- Non-Terraform script execution (`ScriptDispatcher`, etc.) — except where shared types (e.g., `ScriptGroup`) require minimal additive changes for secret-marking.
- Database schema changes beyond additive columns/tables for catalog metadata, plan persistence, and (if required) audit.
- Provider-side cloud authentication design (Azure subscription / AWS account model). Out of scope: choosing identities. In scope: ensuring whatever identity is in use is supplied to `terraform` securely.
- **Composition** of one stock module by another (e.g., `vnet` consumed by an `app-stack` template) — out of scope for this HLPS. Single-module templates are the baseline; composition is a follow-up effort.
- Migration of any existing in-flight Terraform plans (U-3 = NO; no migration required).
- Terraform Cloud / HCP integration. State backend is Azure Blob (U-1).
- Terratest / Kitchen-Terraform for stock modules. `terraform validate` + `tflint` is the bar at this stage.

---

## 3. Goals and Success Criteria

### 3.1 Hardening (correctness + security)

| ID    | Success Criterion |
|-------|------------------|
| SC-01 | **State is platform-owned and verifiable.** For every plan/apply, DOrc renders a managed Azure Blob remote backend configuration before `terraform init`, keyed deterministically by `(project, component, environment)`. The plan and apply phases of the same logical operation operate on the same state. The `.terraform/` cache and `.terraform.lock.hcl` (or the resolved provider+module fingerprint) survive plan→apply. State files are never deleted by DOrc as part of normal teardown. **Verifiable by**: an engineer can read the plan artefact and see the state-key string `(project)/(component)/(environment).tfstate` it targets, before confirming the apply. Tests cover: backend.tf is rendered, state key is deterministic, working dir is reconstituted on apply with identical lock-file digest. |
| SC-02 | **Auth is enforced.** `TerraformController.HasViewPermission`, `HasConfirmPermission`, `HasDeclinePermission` delegate to `IApiSecurityService` using the same environment+project RBAC the rest of the API uses. **Verifiable by**: an integration test demonstrates an authenticated user without the relevant privilege receives `403`; a privileged user receives `200`. |
| SC-03 | **Process spawning is safe.** No Terraform process invocation in DOrc passes user-influenced data through a concatenated command-line string. All process spawning uses `ProcessStartInfo.ArgumentList`. Cancellation kills the entire child process tree. **Exit-code semantics**: for `terraform apply` and `terraform init`, any non-zero exit code is failure. For `terraform plan -detailed-exitcode`, `0` = no changes, `1` = error, `2` = changes pending (success). **Verifiable by**: tests with a fake terraform CLI returning each exit code and asserting outcome. **Process lifecycle**: no terraform child processes remain on the host five seconds after a cancellation signal — verified by a test that lists processes by name post-cancel. |
| SC-04 | **Secret flow is safe.** PATs, bearer tokens, and any property whose name matches the secret pattern `(?i)(token|pat|secret|password|key|connectionstring)` are redacted in all log output and in any property serialization that crosses a logging or pipe boundary. **Verifiable by**: a unit test scans the captured log output of the secret-flow paths and asserts no plaintext secret values appear. The named pipe between Monitor and TerraformRunner has an ACL pinned to the runner's SID + LocalSystem on Windows, asserted by an integration test that opens the pipe as a third user and observes `UnauthorizedAccessException`. Plan output written to disk is treated as sensitive: stored only in a runtime working directory protected by ACL, never logged at Information level. |
| SC-05 | **State storage is access-controlled.** The state container is configured with private (no anonymous) access; an Azure RBAC role assignment scoped to the storage container is documented; SAS tokens, if used, default to short lifetime (≤ 1 hour) and are not committed to source control. **Verifiable by**: an Azure CLI (or equivalent) command in `STATE-MODEL.md` produces the expected configuration; a test against a configuration default asserts these properties. |
| SC-06 | **Archive extraction is safe.** ZIP extraction validates each entry's path against directory traversal (`Path.GetFullPath` containment), enforces an entry-count cap (default 10 000), a per-entry uncompressed-size cap (default 50 MiB) and total uncompressed-size cap (default 500 MiB), rejects symlinks (`UnixFileMode` outside Win32), rejects zero-length filenames, and refuses absolute paths or paths starting with `..`. **Verifiable by**: a test fixture with a known-bad archive (path traversal entry, oversized entry, absolute-path entry, symlink) asserts each is rejected with a typed exception and no file is written. |

### 3.2 Architecture

| ID    | Success Criterion |
|-------|------------------|
| SC-07 | **Lifecycle ownership is consolidated.** Plan/apply state-machine ownership lives in one type on the Monitor side (the lifecycle owner), named for its specific responsibility (transition of a plan from drafted → confirmed → applied; not a generic *Manager / *Lifecycle / *Service grab-bag — the IS commits to the final name before execution). Blob storage I/O lives with that type. The runner process accepts three inputs (`working directory`, `operation`, `plan-file path`) and emits (`exit code`, `log stream`, `binary plan file` for plan operations); it persists no state between invocations. **Verifiable by**: the dispatcher contains no calls to `BlobContainerClient` after the refactor; the runner's `Program.cs` parses exactly the documented inputs; a contract test asserts the runner is idempotent across invocations. |

### 3.3 Library / catalog

| ID    | Success Criterion |
|-------|------------------|
| SC-08 | **Template catalog API exists.** A new `Dorc.Terraform.Catalog` library exposes: (a) catalog manifest schema (template `name`, `version` (semver), `source` locator, `parameter schema`, `outputs`, `tags`, `description`, `category`, `requiredProviders`, `requiredTerraformVersion`); (b) `ITemplateCatalog` interface with `ListAsync()`, `GetAsync(name)`, `GetAsync(name, version)`, and at least one implementation (`GitTemplateCatalog`); (c) `IParameterValidator` validating supplied variable maps against the manifest schema; (d) read-only API endpoints `GET /api/terraform/templates`, `GET /api/terraform/templates/{name}`, `GET /api/terraform/templates/{name}/{version}` under existing auth. **Verifiable by**: contract tests against each interface; an end-to-end test creates a component with `Terraform_Template_Name=vnet`, `Terraform_Template_Version=1.0.0` and observes the resolver loading the manifest and rendering the source. |
| SC-09 | **Stock modules ship and follow a contract.** A `MODULE-CONTRACT.md` is published. At least three stock modules satisfy it: corrected `sql-database`, new `vnet`, and one additional foundational module (`storage-account` or `key-vault` — IS commits the choice). Each module ships with: a per-module `README.md` (purpose, inputs, outputs, examples, required cloud auth), `variables.tf` with `validation` blocks for all user-facing inputs, `required_version`, `required_providers`, `outputs.tf` covering downstream-consumable IDs, and `examples/basic/` with a runnable usage. **Verifiable by**: a CI step asserts each stock module conforms (file presence + `terraform validate`). |
| SC-10 | **Module CI exists and gates merge.** A workflow at `.github/workflows/terraform-modules-ci.yml` (or equivalent) runs, against every stock module and example: `terraform fmt -check`, `terraform init -backend=false`, `terraform validate`, `tflint`. The workflow is path-filtered to trigger only on changes under stock-modules and `docs/Terraform/`. Provider versions are pinned via lockfiles; tflint plugins are pinned by SHA in the workflow. **Verifiable by**: workflow file exists, runs green on the merge commit, and demonstrably fails on a deliberately broken-module test PR. |
| SC-11 | **Engineers can consume stock modules by reference and by scaffold.** *Reference*: an engineer creates a DOrc component, sets `Terraform_Template_Name=<name>` and `Terraform_Template_Version=<semver>`, and the source provider resolves the manifest's Git source pinned to the corresponding tag. *Scaffold*: an engineer invokes `POST /api/terraform/templates/{name}/{version}/scaffold` (or equivalent CLI/UI flow) which materialises a copy of the template into an engineer-owned destination, with manifest-derived `terraform.tfvars.example` and a one-line provenance comment recording the catalog name+version. The two paths are mutually exclusive at component level (referenced ≠ scaffolded once owned). **Verifiable by**: integration tests for both paths; a precedence rule test (see SC-12). |
| SC-12 | **Catalog reference and direct source are mutually exclusive at component level with explicit precedence.** When both `Terraform_Template_Name` and a direct `ScriptsLocation`/`TerraformSourceType` are present on a component, the system rejects the component at save time with a clear validation error. There is no silent precedence rule; the engineer must remove one. **Verifiable by**: API validation test producing `400` with a precise error string. |

### 3.4 Frontend & docs

| ID    | Success Criterion |
|-------|------------------|
| SC-13 | **Frontend supports plan review.** `terraform-plan-dialog` parses plan output and renders added (`+`) lines green, destroyed (`-`) lines red, and changed (`~`) lines yellow, with WCAG-AA contrast (≥ 4.5 : 1). Lines containing `(sensitive value)` or matching the secret pattern from SC-04 are masked. A clear error state is rendered when `PlanContent` is empty and an error string is present. **Verifiable by**: a component test on the dialog with sample plan output asserts class assignment per line type, mask behaviour, and error-state rendering. |
| SC-14 | **Docs match reality and define the contract.** All references in `docs/Terraform/` resolve to existing artefacts. `docs/Terraform/STATE-MODEL.md` documents state ownership, the storage account/container lifecycle, RBAC, and how SC-05 is achieved operationally. `docs/Terraform/MODULE-CONTRACT.md` defines the contract every stock module must satisfy and the **module-versioning + deprecation policy** (semver, deprecation notice in README, minimum 90-day deprecation window before removal, ownership entries). The setup example documents state, secrets, RBAC, module versioning, and the catalog reference flow. **Verifiable by**: a docs-link checker passes; STATE-MODEL.md and MODULE-CONTRACT.md exist and are referenced from the setup example. |

### 3.5 Cross-cutting

| ID    | Success Criterion |
|-------|------------------|
| SC-15 | **Naming compliance.** No new C# class introduced by this work uses a CLAUDE.md-banned grab-bag name. Existing grab-bag names in touched paths (`DirectoryHelper` at minimum) are renamed or split into cohesive types. **Verifiable by**: a manual audit table in the final adversarial-review record listing every new class and its sole responsibility. |
| SC-16 | **Tests exist where they didn't.** Each modified or new C# class has unit tests for its public behaviour. The lifecycle owner (SC-07) has an integration test exercising plan→apply round-trip against a fake terraform CLI, asserting (a) `backend.tf` is rendered, (b) the plan artefact is persisted, (c) apply re-invokes with the same plan file, (d) state-key continuity. The catalog has unit tests for the parameter validator and an end-to-end test for `GitTemplateCatalog` against a local repo fixture. **Verifiable by**: `dotnet test` runs green; coverage report shows non-trivial coverage of the new and modified types. |
| SC-17 | **Adversarial-review quality gate.** All HIGH and CRITICAL findings raised during the implementation review (across rounds 1–3) are either resolved or explicitly escalated to the user with rationale. The review cycle is capped at 3 rounds; if HIGH/CRITICAL findings remain after round 3, the residuals are recorded in `docs/terraform-hardening/REVIEW-ESCALATIONS.md` with a recommendation. **This criterion explicitly covers findings introduced by remediations during later rounds, not only those identified in round 1.** |

### 3.6 SD ↔ SC traceability

| SD | Maps to |
|----|---------|
| SD-1 (state) | SC-01, SC-05, SC-14 |
| SD-2 (RBAC) | SC-02, SC-16 |
| SD-3 (process) | SC-03, SC-16 |
| SD-4 (secrets) | SC-04, SC-16 |
| SD-5 (zip) | SC-06, SC-16 |
| SD-6 (lifecycle) | SC-07, SC-16 — **must precede SD-7** |
| SD-7 (catalog) | SC-08, SC-12, SC-16 |
| SD-8 (modules + contract) | SC-09, SC-14 |
| SD-9 (CI) | SC-10 |
| SD-10 (frontend) | SC-13 |
| SD-11 (tests) | SC-16 |
| SD-12 (naming) | SC-15 |
| SD-13 (docs) | SC-14 |
| SD-14 (consumption: reference + scaffold) | SC-11, SC-12 |

---

## 4. Constraints

- **C-01 (CLAUDE.md naming).** No grab-bag names. New classes are cohesive; namespaces follow `Dorc.[Component].[Feature]`; assemblies follow `Dorc.<Component>.dll`. The IS reconciles `Dorc.Terraform.Catalog` against this pattern.
- **C-02 (CLAUDE.md TDD).** Each step is implemented test-first. A step is not "done" until its tests are written and pass.
- **C-03 (.NET LTS).** New project (`Dorc.Terraform.Catalog`) targets the LTS already used by sibling projects. No language-extension libraries.
- **C-04 (No restricted features).** No `language-ext`, no functional libraries.
- **C-05 (Backwards compatibility).** Existing components configured with direct `Git`/`AzureArtifact`/`SharedFolder` sources continue to plan/apply without migration. The catalog is additive — pre-existing direct-source components do not require migration to catalog references. New components using catalog references use the new source-provider path; the two paths coexist but are mutually exclusive per component (SC-12).
- **C-06 (No Terraform-CLI bundling).** DOrc continues to invoke a `terraform` binary on PATH. We do not bundle terraform.
- **C-07 (No broker/infra changes outside DOrc).** Fixes are achievable inside DOrc; we do not provision Azure storage accounts as part of CI. The state-storage account is an operational dependency documented in `STATE-MODEL.md`.
- **C-08 (Sovereign cloud safety).** No hardcoded `dev.azure.com`, public-cloud Storage endpoints, or Terraform Registry URLs; cloud endpoints come from configuration with documented defaults.
- **C-09 (Adversarial review cadence).** HLPS, IS, each implementation step, and the final integrated review go through an adversarial panel of ≥ 2 sub-agents on diverse models. Max 3 rounds per gate, then escalate.

---

## 5. Proposed Solution Directions

Each direction maps to one or more success criteria via §3.6. The IS will codify the execution order; the **dependency invariant SD-6 → SD-7** is restated here because the catalog (SD-7) integrates with the lifecycle owner (SD-6) and must not be built against the existing dispatcher mess.

### SD-1: Platform-owned state (SC-01, SC-05, SC-14)
DOrc renders an Azure Blob backend configuration into the working directory before `init`. Mechanism: write a `terraform.tf` (or `_dorc_backend.tf`) file containing only the `terraform { backend "azurerm" { ... } }` block, with `key = "{project}/{component}/{environment}.tfstate"`. **User-checked-in backend blocks are forbidden** — pre-flight rejects any `*.tf` file in the source containing a `backend` declaration with a clear validation error directing the engineer to remove it. (The earlier draft proposed `_override.tf` semantics; the round-1 panel correctly identified that backend overrides via `_override.tf` are not reliable. The pre-flight rejection approach is unambiguous and trivially testable.)

The working directory is preserved between plan and apply for the same operation by uploading the entire post-`init` directory contents (including `.terraform/` and `.terraform.lock.hcl`) and the binary plan file as a single **execution-bundle** tarball blob. The blob is keyed by **plan-operation ID** (a GUID generated at plan time and persisted on the plan record) — `bundles/{planOperationId}.tar.gz` — and the tarball's SHA-256 content hash is also persisted on the plan record. On apply, the tarball is downloaded by `planOperationId`, the SHA-256 is recomputed and compared, and the bundle is extracted into a fresh local working directory. Per-operation keying eliminates the cross-operation overwrite window; hash verification eliminates partial-upload races. The state blob (`(project)/(component)/(environment).tfstate`) is separate from the execution bundle and lives forever.

State is never deleted by DOrc. State container access is private (SC-05).

### SD-2: Real RBAC (SC-02)
Replace stubbed permission checks in `TerraformController` with calls to the already-injected `IApiSecurityService` using the request's environment and project context. Add unit tests for: forbidden user, allowed user, missing environment, missing project, deleted environment.

### SD-3: Safe process spawning (SC-03)
Switch every `ProcessStartInfo` Terraform invocation to `ArgumentList`. Replace exit-code branching with terraform-correct semantics per SC-03. On cancellation, `process.Kill(entireProcessTree: true)` runs deterministically before returning. The runner-process command line in `TerraformRunnerProcessStarter` is built with proper quoting (a documented helper covered by tests if `CreateProcessAsUser` requires a single string).

### SD-4: Secret flow hardening (SC-04)
Reuse any existing DOrc convention for marking a property as secret (verified in IS-001 pre-audit; if absent, introduce a `SensitivePropertyNames` configuration applied as a redactor). Update `ScriptGroupPipeClient`, `ScriptGroupFileReader`, and any other property-serialization log site to redact properties whose name matches the configured pattern. Apply ACL to the named pipe via `PipeSecurity` pinning to runner SID + LocalSystem on Windows. Plan-show output is never emitted at Information level.

### SD-5: Safe ZIP extraction (SC-06)
Centralize ZIP extraction in a single class (`SafeZipExtractor` or a more cohesive name decided in IS — not a `*Helper`/`*Util`). Replace `ZipFile.ExtractToDirectory` calls with explicit `ZipArchive` iteration: validate entry path with `Path.GetFullPath` containment, enforce caps, reject symlinks, reject zero-length filenames, reject absolute paths.

### SD-6: Lifecycle ownership (SC-07) — **prerequisite for SD-7**
Introduce on the Monitor side a single class owning the plan→apply state machine. Working name **`TerraformPlanExecution`** (the type owns one plan's lifecycle from drafted to applied or declined) — final commit to this name in IS-006 pre-audit, conditional on the audit confirming no clash with existing `Dorc.Monitor` types. Responsibilities: receive request, instruct runner to plan, persist plan, gate on confirmation, instruct runner to apply, persist outcome. The runner reverts to a stateless terraform-shell. The dispatcher becomes a thin queue→lifecycle adapter.

**Rollback strategy**: SD-6 lands behind a feature flag (`Terraform:UseConsolidatedLifecycle`) that defaults to enabled in dev/test; production rollout flips the flag. If production reveals a defect, the flag flips back to the legacy dispatcher path. The flag is removed in a follow-up step once stable.

### SD-7: Template catalog (SC-08, SC-12) — **after SD-6**
New project `Dorc.Terraform.Catalog`. Components reference templates by setting `Terraform_Template_Name` and `Terraform_Template_Version` on the component property bag. Validation at component-save time enforces SC-12 (mutual exclusion). Source provider resolves a template reference into a Git source pinned at the tag `stock-modules/<name>/v<version>` (U-7).

### SD-8: Stock modules + module contract (SC-09, SC-14)
Create `stock-modules/` at repo root (U-6). Each module satisfies `MODULE-CONTRACT.md`. Initial set: `sql-database` (corrected), `vnet`, plus one of `key-vault`/`storage-account` (decided in IS-008 pre-audit). MODULE-CONTRACT.md defines the deprecation policy (90-day notice; deprecation noted in README and manifest) and ownership (each module's README has an `Owner` field; a MODULES.md index lists all modules and owners).

### SD-9: Module CI (SC-10)
GitHub Actions workflow with path-filter trigger for `stock-modules/**` and `docs/Terraform/**`. Steps: `fmt -check`, `init -backend=false` (offline-friendly), `validate`, `tflint` (pinned by SHA). Provider versions pinned via committed `.terraform.lock.hcl` per module; CI verifies the lockfile is up to date. Mitigates supply-chain risk identified in panel-round-1 (R-5).

### SD-10: Frontend plan diff (SC-13)
Replace the raw `<pre>` plan dump with a parsed-diff renderer per SC-13.

### SD-11: Tests (SC-16)
- New test project `Dorc.TerraformRunner.Tests`.
- New tests in `Dorc.Monitor.Tests` and `Dorc.Api.Tests` for Terraform paths.
- Module-level: `terraform validate` + `tflint` in CI is sufficient at this stage; terratest is a follow-up.

### SD-12: Naming (SC-15)
Rename `DirectoryHelper` into cohesive types (the IS commits to names after a pre-audit reads each method's true responsibility — examples: a type owning the working-directory layout and a type owning robust deletion of read-only-marked trees). Audit all touched files for grab-bag names.

### SD-13: Docs alignment (SC-14)
Rewrite `docs/Terraform/terraform-source-configuration.md` and `terraform-setup-example.md` to match implemented reality. Remove broken `sql-managed-instance` references (the module is not in scope; SD-8 ships three other modules). Add `docs/Terraform/STATE-MODEL.md`, `docs/Terraform/MODULE-CONTRACT.md`, and a top-level `docs/Terraform/MODULES.md` index.

### SD-14: Consumption — reference + scaffold (SC-11, SC-12)
**Reference (default)**: catalog reference resolved at deploy time per SD-7. **Scaffold (escape hatch)**: `POST /api/terraform/templates/{name}/{version}/scaffold` materialises a copy. The scaffolded artefact is delivered as a downloadable archive (or written to an engineer-supplied target via existing DOrc upload mechanisms — final mechanism committed in IS-014 after a UX micro-spike). The scaffold archive includes a top-of-file provenance comment recording catalog name+version; once scaffolded, the artefact is engineer-owned and outside catalog lifecycle.

---

## 6. Unknowns Register

| ID  | Description | Owner | Status | Resolution |
|-----|-------------|-------|--------|------------|
| U-1 | Which remote state backend should DOrc render? | User | **RESOLVED** | Azure Blob (matches existing `Azure.Storage.Blobs` dependency in TerraformDispatcher). |
| U-2 | Should the catalog reference live in the property bag or a dedicated table? | User | **RESOLVED** | Property bag (`Terraform_Template_Name`, `Terraform_Template_Version`) — additive, no schema migration. **Integrity rule (added round-2):** SC-12 enforces mutual exclusion with direct source types at component-save time, returning a `400` validation error if both are set. |
| U-3 | Is the existing terraform implementation in production use today? | User | **RESOLVED** | **NO** (user-confirmed 2026-05-09). No state-import migration required. |
| U-4 | What identity does the runner use to talk to Azure providers when running `terraform plan/apply`? | User | Non-blocking | Runner inherits the logged-on user identity in the existing implementation. Preserved; documented in MODULE-CONTRACT. |
| U-5 | Is there an existing DOrc convention for marking a property as "secret"? | Agent | **TO BE RESOLVED IN IS-001 PRE-AUDIT** | A grep of `IsSecret`, `Sensitive`, `Secret` in `Dorc.ApiModel` and `Dorc.Core` precedes implementation. |
| U-6 | Where should the stock-modules tree live? | User | **RESOLVED** | `stock-modules/` at repo root. CI is path-filtered to avoid triggering on unrelated PRs. |
| U-7 | What semver-tag scheme should stock modules use? | Agent | **RESOLVED** | Git tag `stock-modules/<name>/v<X.Y.Z>`. Catalog manifest uses these tag references. |
| U-8 | Does the existing repo CI already define a `terraform`-installer step? | Agent | **TO BE RESOLVED IN IS-009 PRE-AUDIT** | Inspect `.github/workflows/`. |
| U-9 | Loop-exit definition for review rounds. | User | **RESOLVED** | "Max 3 rounds per gate" (HLPS, IS, integrated final). SC-17 codifies the rule including remediations introduced during later rounds. |
| U-10 | Stock-module consumption model. | User | **RESOLVED** | Both — reference is the default, scaffold is the escape hatch (SD-14). |

---

## 7. Risks & Mitigations

Risks identified in round-1 review have been incorporated. Each risk is paired with an explicit mitigation; failure to land the mitigation is itself an in-scope success-criterion gap.

| ID  | Risk | Severity | Mitigation |
|-----|------|----------|------------|
| R-1 | **Concurrent plan/apply on the same `(project, component, environment)`.** Two engineers trigger plan simultaneously; both render `backend.tf`, both `init`, both apply against the same state. | CRITICAL | The Azure Blob `azurerm` backend uses **blob lease-based state locking** by default; DOrc relies on terraform's native locking. SD-6 lifecycle owner additionally serialises plan and apply for the same `(project, component, environment)` triple via an in-process semaphore (single-monitor) plus an Azure Blob lease on the state blob (multi-monitor — same primitive that backs terraform locking, but acquired explicitly by DOrc before invoking terraform to surface a clean user-visible error rather than a terraform-internal lock-wait timeout). Tested by an integration test simulating concurrent triggers. |
| R-2 | **Working-directory reconstitution race / partial upload between plan and apply.** | HIGH | SD-1 uploads the working directory + plan file as a **single execution-bundle tarball blob** keyed by `planOperationId` with a SHA-256 content hash recorded on the plan record. Apply downloads by `planOperationId` and verifies the SHA-256. Per-operation keying eliminates cross-operation overwrites; hash verification fails fast on partial upload. Tested by a fault-injection test that truncates the upload mid-stream and a concurrency test triggering two plans against the same triple. |
| R-3 | **Secrets-in-state.** Terraform persists provider-returned secrets to `.tfstate` in plaintext. | HIGH | SC-05 binds the state container to private access and documented RBAC. STATE-MODEL.md warns module authors to avoid materialising provider-returned secrets in outputs where avoidable; modules in SD-8 follow this rule. Long-term mitigation (out of scope, flagged in STATE-MODEL.md): switch to the `azurerm` backend's encrypted-state SAS or migrate to a Key-Vault-backed state. |
| R-4 | **`backend_override.tf` does not work for backends.** Pre-existing risk in earlier draft; resolved in revision. | — | No longer relevant: SD-1 rejects user-checked-in backend blocks at pre-flight (see SD-1 update). |
| R-5 | **Supply-chain risk from tflint/registry downloads in CI.** | MEDIUM | SD-9 pins tflint plugin SHAs; provider versions pinned in committed `.terraform.lock.hcl` per module; CI uses `init -backend=false` and verifies the lockfile is current. Future: configure a provider mirror — flagged as follow-up. |
| R-6 | **No rollback for SD-6 lifecycle refactor.** | MEDIUM | SD-6 ships behind feature flag `Terraform:UseConsolidatedLifecycle`. Default enabled in dev/test; controlled rollout in production. Flag removed once stable. |
| R-7 | **State-storage account ownership.** | MEDIUM | STATE-MODEL.md names the account/container, RBAC roles, and the on-call owner placeholder (filled by user before go-live). Configuration defaults document an explicit name; the account is operator-provisioned, not auto-created. |
| R-8 | **Hard kill of the runner mid-apply.** | LOW | Out of scope. The Azure Blob state lease will protect concurrent applies. Subsequent recovery is manual or via a future "force unlock" admin action — flagged in `STATE-MODEL.md` as a follow-up. |
| R-9 | **Stock-modules CI cost / noise on unrelated PRs.** | LOW | Path-filter trigger on `stock-modules/**` and `docs/Terraform/**` only. |

---

## 8. Stakeholders & Module Ownership

- **Engineers** building DOrc components that deploy infrastructure — primary users.
- **DOrc platform team** — owners of `Dorc.Terraform.Catalog`, `Dorc.TerraformRunner`, lifecycle owner.
- **Stock-module owners** — recorded per-module in the module's README `Owner:` field and aggregated in `docs/Terraform/MODULES.md`. Initial owners default to "DOrc platform team" until reassigned.
- **Security** — review of secret-flow and RBAC changes.
- **Operations** — owns the state-storage account; on-call for state-availability incidents.

---

## 9. Approval

This document requires adversarial-panel approval (≥ 2 sub-agents on diverse models per CLAUDE.md) before progressing to IS. Findings will be triaged Accept / Downgrade / Defer / Reject with each round capped at 3.

### 9.1 Round-1 Panel Outcome (2026-05-09)

Three reviewers (Opus, Sonnet, Haiku), all returned **APPROVE-WITH-REVISIONS**. Convergent findings accepted and applied in this revision:

- SC-03 exit-code statement reworded (was: "2 not applicable to apply" — incorrect framing).
- U-2 integrity rule added (SC-12 enforces mutual exclusion; precedence is "reject both set").
- Engineer-DX consumption story added (SD-14, SC-11) — addresses the highest-leverage goal-fit gap.
- `backend_override.tf` claim replaced with a pre-flight rejection of user backend blocks.
- U-3 confirmed by user (NO production use); state-import sub-step not required.
- State-storage security SC added (SC-05).
- Risk register expanded with R-1..R-9.
- SC-17 reworded to cover findings introduced by remediations during later rounds.
- SD-6 → SD-7 dependency stated explicitly in §5.
- SC-15 sets an absolute "no new grab-bag names" bar.
- Module ownership and deprecation policy added (SC-14, §8).
- Hardening (SD-1..SD-6) → catalog (SD-7+) ordering codified in SD-6 prerequisite note.

### 9.2 Round-2 Panel Outcome (2026-05-09)

Three reviewers (Opus, Sonnet, Haiku); 2× APPROVE, 1× APPROVE-WITH-REVISIONS. The single revision was a MEDIUM from Reviewer B: SD-1's tarball blob keying needed to include the plan-operation ID to prevent cross-operation overwrite. Applied: SD-1 and R-2 now specify per-operation keying (`bundles/{planOperationId}.tar.gz`) plus SHA-256 verification. Other reviewer notes were LOW (mechanism naming, U-4 documentation deferral) — accepted and folded into the IS pre-audits. **APPROVED — IS drafting may begin.**
