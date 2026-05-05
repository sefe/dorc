# IS — Request Grid Substring-Scan Performance Fix

**Status:** IN REVIEW
**Date:** 2026-05-05
**Author:** Ben Hegarty (with Claude Opus 4.7)
**Topic slug:** `request-grid-perf`
**Governing HLPS:** [`HLPS-request-grid-perf.md`](HLPS-request-grid-perf.md) — APPROVED 2026-05-05

---

## 0. Overview

This IS decomposes the HLPS into atomic, independently valuable steps. Each step has a stable canonical ID (`S-001`, `S-002`, …); IDs never renumber even if a step is removed or deferred. JIT Specs and any per-step branches reuse the same ID.

The work targets a single PR (per project convention) on the existing branch `perf/request-grid-sargable-filters`. Each step lands as one or more commits with its `S-NNN` ID prefix in the message; the PR ships the complete sequence.

This IS is a strategic roadmap. Per `CLAUDE.local.md` §2 abstraction rules, it deliberately does **not** specify method signatures, line numbers, exact SQL DDL, literal property names, or commit-message templates. Those decisions are made in the JIT Spec for each step or during Delivery.

## 1. Step Sequence (Summary)

| ID | Title | Depends on | Behaviour-changing? | Schema-changing? |
|----|-------|-----------|---------------------|------------------|
| S-001 | Audit other `ContainsExpression` consumers against the §3 critical-instance definition | — | No | No |
| S-002 | Add supporting non-clustered index(es) on `deploy.DeploymentRequest` | — | No | **Yes** |
| S-003 | Replace `EnvironmentNameExact` substring with equality predicate | S-002 (for seek) | Yes — env-monitor query plan changes from scan to seek; row-set unchanged | No |
| S-004 | Switch Project + EnvironmentName per-field filters from substring (`Contains`) to prefix (`StartsWith`) in **both** the AND-path and the OR-path of the request-grid endpoint | S-002 (hard); S-003 (soft — adjacent code only) | Yes — substring matches narrow to prefix matches for these two fields | No |
| S-005 | Update dorc-web UI affordances in the affected components to reflect the new filter semantics | S-004 (must accompany the backend change) | Yes — visible UI text changes | No |
| S-006 | Performance verification against a representative `deploy.DeploymentRequest` dataset; capture plan + logical reads against the HLPS SC3 target | S-002, S-003, S-004 | No (verification only) | No |
| S-007 | Release notes update describing the substring → prefix tightening, the new indexes, and the affordance changes | S-004, S-005 | No (documentation only) | No |

## 2. Step Detail

Each step is described against the HLPS goal and success criteria, with explicit verification intent. None of these descriptions specify `how` — the how lives in the JIT Spec for that step.

---

### S-001 — Audit other `ContainsExpression` consumers

**What changes.** A written audit of every caller of the centralised `ContainsExpression` helper outside `RequestsStatusPersistentSource`, recording for each whether it meets the HLPS §3 definition of "critical instance" (table > ~100K rows in production *and* invoked from a polled/auto-refreshed UI surface *and* at least one filter column lacks a supporting index).

**Why.** HLPS §3 In Scope and SC6 require this audit. A critical instance found here may expand the scope of this HLPS or open a follow-up HLPS; running the audit early prevents downstream rework if something critical is uncovered.

**Dependencies.** None.

**Verification intent.** The audit deliverable is a markdown file under `docs/request-grid-perf/` listing every caller and its disposition. The Adversarial Quality Gate verifies completeness (all callers covered) and correct application of the critical-instance test. If a critical instance is found, the agent must escalate to the user before any further step proceeds.

---

### S-002 — Add supporting non-clustered index(es) on `deploy.DeploymentRequest`

**What changes.** New non-clustered index(es) on `deploy.DeploymentRequest` that support the predicates introduced in S-003 (equality on Environment) and S-004 (prefix on Project, prefix on Environment). The IS does **not** specify column count, sort order, included columns, or whether one composite index or two single-column indexes is correct — those are JIT-Spec decisions guided by HLPS U7 (per-environment skew) and the actual query patterns in the env-monitor and main-grid call sites.

The index ships through the existing SSDT database project (`src/Dorc.Database`). The JIT Spec chooses between the two patterns observed in the codebase (separate `.index.sql` file under `Schema Objects/Schemas/deploy/Tables/Indexes/`, or inline `CREATE NONCLUSTERED INDEX` in the `.table.sql`). The dominant pattern is the separate-file form; default to it unless the JIT Spec records a justified deviation.

**Why.** HLPS SC1, SC2, SC3 and §3 In Scope. The new predicates (equality and prefix) are SARGable, but they only achieve seek behaviour against a supporting index. Without S-002, S-003 and S-004 deliver less of the intended cost reduction.

**Dependencies.** None on prior IS steps. **Per-step blocking on Delivery entry: HLPS U4 and HLPS U7.** U4 (Enterprise vs Standard) determines whether `ONLINE = ON` is available; U7 (per-environment skew) informs column order. Both are non-blocking on this step's JIT Spec drafting (which may include a conditional fork) but block its entry into Delivery until resolved by the user / DBA.

**Verification intent.**
- The schema change builds clean against the SSDT project.
- Deployment to a non-production environment succeeds without an exclusive lock that would block live workload (the production deployment plan is a JIT-Spec deliverable subject to U4).
- The new index(es) are visible in `sys.indexes` post-deploy and have non-zero row count after a representative seed.
- (Plan-regression check against existing queries — including the existing `IX_Status_IsProd` continuing to be selected where appropriate — is performed in S-006, not here. S-002's local verification is bounded by what is observable without exercising the runtime query workload.)

---

### S-003 — Replace `EnvironmentNameExact` substring with equality

**What changes.** The `EnvironmentNameExact` filter branch in the request-grid endpoint stops routing through `ContainsExpression` and instead emits an equality predicate on the Environment column. The `ContainsExpression` helper itself is **not** modified — its other consumers retain current behaviour. No change to the public API contract; the filter path name (`EnvironmentNameExact`) and request shape are unchanged.

**Why.** HLPS §1 defect (2) and SC1. The `EnvironmentNameExact` path is invoked by every open env-monitor session in production; converting it from a substring scan to an equality seek is the single largest contributor to relieving CPU saturation per the HLPS analysis. HLPS U3 confirmed the sole consumer (`env-monitor.ts`) requires equality.

**Dependencies.** S-002. Without the supporting index, equality is still scan; with the index, equality becomes seek. The two steps may land in the same commit if the JIT Spec considers it cleaner.

**Verification intent.**
- The captured EF SQL for an `EnvironmentNameExact` filter contains `=` and not `LIKE '%...%'`.
- The query plan against a representative seed shows an index seek on the new (or chosen existing) index.
- All current row matches are preserved (the env-monitor consumer always passes a complete environment name; equality and substring return the same row for that input).
- Existing tests pass; new tests cover the equality predicate and the absence of substring SQL.

---

### S-004 — Switch Project + EnvironmentName per-field filters to `StartsWith`, in both AND-path and OR-path

**What changes.** The per-field filters introduced by PR #338 — Project, EnvironmentName, BuildNumber — change predicate semantics in the request-grid endpoint as follows: Project → prefix (`StartsWith`); EnvironmentName → prefix (`StartsWith`); BuildNumber → substring retained. The change applies consistently to both the AND-path (used by the global request grid) and the OR-path (used by the env-pinned monitor's shared filter input). The `ContainsExpression` helper is not modified for non-string types or for other call sites.

The IS does not specify how the predicate switch is implemented (a small targeted helper alongside `ContainsExpression`, or a parameter on the existing helper, or inlined LINQ — JIT Spec decides). It does specify that BuildNumber must continue to use substring semantics and that the path-name routing logic that distinguishes Project/Environment/BuildNumber from `EnvironmentNameExact` and from generic columns must remain coherent.

**Why.** HLPS SC2, SC5; HLPS §3 In Scope (both call paths). SARGable predicates on Project and EnvironmentName let the supporting indexes from S-002 deliver seek behaviour. BuildNumber is acknowledged in HLPS SC2 as not fully SARGable in isolation: when a Project or Environment filter is also supplied, the BuildNumber predicate operates on the row set already narrowed by the SARGable seek and the residual scan is bounded; BuildNumber-only filtering is explicitly carved out of the SC3 perf guarantee.

**Dependencies.** **S-002 (hard — required for seek behaviour).** S-003 is **soft** (adjacent code; bundling reduces churn and keeps the reviewers' mental model coherent, but S-004 can land independently of S-003 if circumstances require).

**Verification intent.**
- The captured EF SQL for Project / EnvironmentName filters contains `LIKE '@p%'` (prefix), not `LIKE '%@p%'` (substring), in **both** call paths. The OR-path is exercised by simulating the env-monitor input pattern (identical value pushed to multiple paths).
- BuildNumber filter still emits substring SQL.
- Query plan shows index seek on the new indexes for typical Project- or Environment-bearing requests.
- Behaviour matrix: representative input strings produce the documented outcomes (a project name fragment that previously matched as substring may no longer match — expected and intentional, mirrored in the affordance change in S-005).
- All existing grid filter tests pass; new tests cover prefix vs substring SQL and the AND/OR-path symmetry.

---

### S-005 — Update dorc-web UI affordances

**What changes.** Visible UI text in the dorc-web components affected by S-004 updates to communicate the new filter semantics. The components in scope are the request-grid (`page-monitor-requests.ts`) and the env-pinned monitor (`env-monitor.ts`). Affordance changes include placeholder text, label, and/or tooltip — exact wording and placement are JIT-Spec decisions guided by the existing UI patterns and the HLPS Constraint §4 requirement of a *visible* affordance, not just a release-note line.

**Why.** HLPS Constraint §4 ("no silent behaviour shift") and SC5. Users typing a project-name fragment that previously matched as substring will no longer match if the fragment is not a prefix; an affordance change tells them so before they file a "search broke" ticket.

**Dependencies.** S-004 — the backend behaviour change must accompany the affordance change in the same release; otherwise either the UI lies or the behaviour is silently changed.

**Verification intent.**
- Affordance text is updated in both components.
- Existing component tests pass; new tests assert the affordance text exists and reflects the documented semantic.
- Manual smoke check: open the components in a dev build and confirm the affordance is legible and accurate.

---

### S-006 — Performance verification

**What changes.** A measured performance verification against a representative `deploy.DeploymentRequest` dataset (matching production row counts and per-environment skew within an order of magnitude — HLPS SC3 wording). Captures: query plan (seek vs scan), logical reads, and elapsed time for (a) the env-monitor `EnvironmentNameExact` query — exercised **with** and **without** a Status filter combined, so the HLPS §8 risk of plan interaction with `IX_Status_IsProd` is directly observable, (b) a typical multi-filter main-grid query with non-empty Project + EnvironmentName + BuildNumber (AND-path), and (c) the env-monitor `detailsFilter` query exercising the OR-path. Pre-fix numbers are captured against the current `main` baseline (i.e., before any of S-002 / S-003 / S-004 lands) and post-fix numbers are captured after the three steps are integrated.

The IS does not specify the seeding mechanism, the harness, or the exact numerical ceiling — those are JIT-Spec decisions. The HLPS SC3 ceiling deferral instruction means the spec must propose a justifiable seek-driven target and the user signs off on it as part of the spec review.

**Why.** HLPS SC3. Without measured evidence the structural fix is plausible but not demonstrated; SC3 is the primary delivery-side check of fitness for purpose. Operational SC4 verification on production is owned by the ops team and is out-of-band of this step.

**Dependencies.** S-002, S-003, S-004 must be code-complete; the seed dataset can be prepared in parallel.

**Verification intent.**
- Plans for all three measured queries show index seek (where applicable per query shape).
- Logical reads materially below the observed ~370K-per-scan range, meeting the JIT-Spec ceiling.
- Report committed under `docs/request-grid-perf/` capturing the numbers and the captured plans for later reference and post-deploy comparison by the ops team.

---

### S-007 — Release notes update

**What changes.** A release-note entry for the next release describes (a) the substring → prefix tightening for Project and EnvironmentName filters in the request grid, (b) the unchanged substring behaviour for BuildNumber, (c) the new index(es) on `deploy.DeploymentRequest`, and (d) any notable affordance changes in dorc-web. The SC4 ops hand-off (asking the ops team to confirm post-deploy CPU baseline against the S-006 evidence report) is **separate** from the customer-facing release notes — it lands in the S-006 evidence document or a runbook entry, not in the release notes themselves.

The IS does not specify the exact release-notes file path — that follows the existing project convention discovered or confirmed during JIT Spec.

**Why.** HLPS Constraint §4 and SC5 — release notes are part of the no-silent-shift requirement and the SC5 behaviour-tightening hand-off.

**Dependencies.** S-004 (the behaviour being documented) and S-005 (the affordance being documented).

**Verification intent.**
- A release-notes commit exists and references all five points.
- Cross-check against this IS: every `Yes` in the "Behaviour-changing?" column of §1 is reflected in the notes.

---

## 3. Risks and Sequencing Notes

- **U4 / U7 are per-step blocking on S-002.** S-002 may enter the JIT Spec phase before they are resolved (the JIT Spec can be drafted with a conditional fork: Enterprise vs Standard). The JIT Spec for S-002 must not enter Delivery until U4 and U7 are resolved by the user / DBA, per HLPS §7.
- **S-001 is upstream.** A critical-instance audit finding on, e.g., `PropertyValuesPersistentSource` would expand scope. The agent escalates to the user immediately rather than absorbing the new work into this IS.
- **S-003 + S-004 may be bundled in delivery** (touching adjacent code) but remain conceptually distinct steps. The PR diff structure is up to Delivery; the IS does not prescribe commit ordering or count.
- **S-005 is a hard pair with S-004.** Splitting them across releases would violate Constraint §4. If the Delivery loop discovers a reason to defer S-005, S-004 must be deferred with it.
- **S-006 is the SC3 evidence step.** If the measured logical-read reduction is not material (e.g., the index choice is wrong or skew is worse than assumed), the JIT Spec for S-002 must be revisited. This is an explicit feedback loop — not a failure mode.

## 4. Out of Scope (reaffirmed from HLPS)

- General refactor of `ContainsExpression` itself.
- Changes to other paged-grid endpoints unless S-001 surfaces a critical instance.
- Operational mitigations (kill sessions, polling cadence, plan hints).
- ORM upgrade / EF version change.
- BuildNumber-only filtering performance — explicitly carved out in HLPS SC2; the residual scan when no other indexed predicate is supplied is accepted.

## 5. Status / Review Trail

| Round | Date | Status | Reviewers | Outcome |
|-------|------|--------|-----------|---------|
| —     | 2026-05-05 | DRAFT | — | Initial draft. |
| R1    | 2026-05-05 | IN REVIEW | Opus 4.7, Sonnet 4.6, Haiku 4.5 | Submitted to adversarial panel. GPT 5.4 substituted with Haiku 4.5 — out-of-band model unavailable. |
| R1    | 2026-05-05 | IN REVIEW | Opus 4.7, Sonnet 4.6, Haiku 4.5 | All three returned **APPROVE WITH MINOR FINDINGS**. No HIGH defects; five MEDIUM ambiguities accepted, two LOW improvements accepted, six LOW improvements deferred per fix-scope discipline. See §6 below. |

## 6. R1 Adversarial Review — Triage

| Finding | Reviewer | Severity | Disposition | Action / Resolution |
|---------|----------|----------|-------------|---------------------|
| F1 (Sonnet) | Sonnet 4.6 | MEDIUM | **ACCEPT** | S-002 Dependencies field now explicitly lists U4 and U7 together as per-step Delivery-blocking. |
| F1 (Opus) / F1 (Haiku) | Both | MEDIUM | **ACCEPT** | §1 summary table for S-004 annotated as `S-002 (hard); S-003 (soft — adjacent code only)`. S-004 step body matches: S-002 hard, S-003 soft. |
| F2 (Opus) | Opus 4.7 | MEDIUM | **ACCEPT** | S-002 Verification Intent rewritten to remove the "verified empirically as part of S-006" line. Plan-regression check is the responsibility of S-006 only. S-002's local verification is bounded by static / non-runtime checks. |
| F2 (Sonnet) / F4 (Haiku) | Both | MEDIUM | **ACCEPT** | S-006 step body now explicitly requires query (a) — env-monitor `EnvironmentNameExact` — to be exercised both **with** and **without** a Status filter combined, so the HLPS §8 plan-interaction risk with `IX_Status_IsProd` is directly observable. |
| F3 (Haiku) | Haiku 4.5 | MEDIUM | **ACCEPT** | S-004 Why section now references HLPS SC2's bounded-scan language for BuildNumber, including the explicit BuildNumber-only carve-out from SC3. |
| F6 (Opus) | Opus 4.7 | LOW | **ACCEPT** | S-007 What-changes section now separates the customer-facing release notes (a)–(d) from the SC4 ops hand-off, which lands in the S-006 evidence document or a runbook entry. |
| F2 (Haiku) | Haiku 4.5 | LOW | **ACCEPT** | S-006 step body now states pre-fix numbers are captured against current `main` baseline before any of S-002/S-003/S-004 land; post-fix after integration. |
| F3 (Opus) | Opus 4.7 | LOW | **DEFER** | S-002 mentions a specific filesystem path. Kept — informational and load-bearing as scope context; fix-scope discipline says minimum effective edit. |
| F4 (Opus) | Opus 4.7 | LOW | **DEFER** | S-005 names specific component files. Kept — explicit traceability outweighs strict abstraction here. |
| F5 (Opus) | Opus 4.7 | LOW | **DEFER** | Verification-intent literal SQL substrings (`=`, `LIKE 'X%'`, `LIKE '%X%'`). Appropriate at IS verification-intent level; would over-prescribe to remove. |
| F3 (Sonnet) | Sonnet 4.6 | LOW | **DEFER** | Summary table missing "Frontend-changing?" column. Stylistic, would expand surface; reader can infer from step bodies. |
| F4 (Sonnet) | Sonnet 4.6 | LOW | **DEFER** | S-001 explicit reference to "Adversarial Quality Gate". Verbose but not incorrect; kept. |
| F5 (Haiku) | Haiku 4.5 | LOW | **DEFER** | S-007 create-vs-update of release-notes file. JIT Spec resolves trivially. |
