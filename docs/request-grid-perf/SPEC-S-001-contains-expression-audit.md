# SPEC — S-001 — `ContainsExpression` Consumer Audit

**Status:** IN REVIEW
**Date:** 2026-05-05
**Step ID:** S-001
**Author:** Ben Hegarty (with Claude Opus 4.7)
**Topic slug:** `request-grid-perf`
**Governing docs:**
- [`HLPS-request-grid-perf.md`](HLPS-request-grid-perf.md) — APPROVED 2026-05-05
- [`IS-request-grid-perf.md`](IS-request-grid-perf.md) — APPROVED 2026-05-05

---

## 1. Purpose

S-001 produces a written audit of every caller of `DataPagerExtension.ContainsExpression` (or any equivalent helper that emits a string `Contains` LINQ expression against a database column) outside of `RequestsStatusPersistentSource.GetRequestStatusesByPage`, classifying each against the HLPS §3 "critical instance" definition. The audit deliverable lives in this docs folder and is itself the success criterion for HLPS SC6.

The audit's purpose is twofold:
1. **Scope guard.** A critical instance found here may need to be folded into this HLPS or trigger a follow-up HLPS, before substantial code changes for S-002–S-007 land.
2. **Evidence.** A negative audit (no critical instances) is itself useful evidence — it documents that the request-status grid was the unique acute case and other consumers can be left as-is.

This spec is requirements-only. It does not prescribe the agent's workflow, the exact wording of audit findings, or how individual consumers are evaluated beyond the critical-instance test itself.

## 2. Requirements

### R1 — Comprehensive caller enumeration
The audit must enumerate **every** caller of `ContainsExpression` in the codebase outside the request-status grid path. The enumeration is reproducible: the audit document records the exact search the agent performed (e.g., a grep pattern) and the set of files / line references found, so a subsequent reviewer can re-run the search and confirm completeness.

### R2 — Per-caller critical-instance evaluation
For each enumerated caller, the audit records:
- The file and surrounding context (which paged-data endpoint, which UI surface consumes it).
- The underlying database table being filtered.
- An assessment against each of the three critical-instance conditions from HLPS §3:
  1. Does the underlying table exceed ~100K rows in production? (If unknown, the audit must say so and mark this condition as "indeterminate".)
  2. Is the filter invoked from a polled or auto-refreshed UI surface?
  3. Does at least one filter column lack a supporting index? (Cross-checked against the SSDT database project.)
- A final disposition: **Critical** (all three true), **Non-critical** (one or more false), or **Indeterminate** (one or more conditions cannot be answered without DBA input).

### R3 — Escalation on critical findings
If **any** caller is classified as Critical, S-001 is **not complete** until the agent has notified the user with a written summary of the finding and a recommendation: either (a) expand this HLPS's scope to include the critical caller, or (b) open a follow-up HLPS targeting the critical caller separately. The user makes the call. S-002 and onward must not proceed until this is resolved.

If **no** caller is Critical, the audit document records the negative result and S-001 is complete.

### R4 — Indeterminate findings are surfaced, not silenced
If any critical-instance condition is "indeterminate" because it requires DBA input (typically R1's row-count condition), the audit records the open question and tags it as a follow-up for the user / DBA. An indeterminate finding does **not** by itself block S-001 from being marked complete — the audit can land with explicit indeterminate items recorded, provided the user accepts the residual risk.

### R5 — Deliverable location and naming
The audit document is a single markdown file under `docs/request-grid-perf/`. The exact filename is unconstrained beyond being descriptive (e.g., `AUDIT-S-001-contains-expression-consumers.md`). The file follows the project's existing markdown style (header with status, date, governing docs).

### R6 — Branch and commit
S-001 lands as one or more commits on the existing branch `perf/request-grid-sargable-filters`. Per project convention there is no separate per-step branch. The commit message references the step ID (`S-001`).

### R7 — Verification (Adversarial Quality Gate)
The audit is reviewed by the Adversarial Quality Gate against:
- **Completeness** — every caller present in the codebase is covered (the panel may verify this independently with its own grep).
- **Correctness** — the critical-instance test is applied consistently per R2.
- **Surface** — Critical findings are escalated per R3; Indeterminate findings are surfaced per R4.

The audit does not need to pass a code-correctness gate (it produces no code).

## 3. Out of scope

- Any code change. S-001 is documentation only.
- Auditing callers of substring-`Contains` expressions written inline in LINQ (i.e., not via the `ContainsExpression` helper). Those are out-of-scope of this HLPS; if any such call site is observed during the audit it is recorded as an out-of-scope footnote, not evaluated against the critical-instance test.
- Auditing consumers in test projects (`*Tests*`, `*.Tests.*`). Tests do not represent production load.
- Performance measurement of any audited consumer. The critical-instance test is structural; perf measurement is owned by S-006 and is scoped to the request-status grid only.
- Proposing fixes for any audited consumer. The audit produces a list and dispositions; remediation is out of S-001's scope by design.

## 4. Acceptance Criteria

S-001 is "done" when **all** of the following hold:
1. The audit document exists at `docs/request-grid-perf/<descriptive-name>.md`, committed on the work branch.
2. R1's reproducible enumeration is recorded in the document.
3. R2's per-caller table is filled in, with explicit dispositions, for every enumerated caller.
4. If any caller is Critical, R3's escalation has occurred and the user has signed off on the resolution path.
5. If any condition is Indeterminate, R4 has been observed (the indeterminate items are surfaced).
6. The Adversarial Quality Gate (R7) has approved the audit.

## 5. Risks & Open Questions

- **Hidden non-helper substring consumers.** The audit deliberately scopes to `ContainsExpression` callers (per HLPS §3 wording). If hot-path substring scans exist that don't go through the helper (e.g., inline `entity.Col.Contains(value)` in a LINQ query), they will be missed. The out-of-scope note in §3 records this as an explicit non-goal of S-001; if the request-status fix proves insufficient post-deploy, a broader audit becomes a follow-up.
- **Production row-count availability.** Condition (1) of the critical-instance test depends on production row counts. If the agent cannot obtain these (e.g., no DBA channel during this session), the audit documents indeterminate findings and the user decides whether to resolve before continuing or accept the residual risk.

## 6. Status / Review Trail

| Round | Date | Status | Reviewers | Outcome |
|-------|------|--------|-----------|---------|
| —     | 2026-05-05 | DRAFT | — | Initial draft. |
| R1    | 2026-05-05 | IN REVIEW | Opus 4.7, Sonnet 4.6 | Submitted to a 2-model panel — audit-only, no code change, lower risk profile per `CLAUDE.local.md` panel-sizing guidance. GPT 5.4 substituted with Sonnet 4.6 — out-of-band model unavailable. |
