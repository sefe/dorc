# SPEC — S-005 — dorc-web UI Affordance Updates

**Status:** APPROVED — auto-pilot grant 2026-05-05 (text-only change; 2-model panel review consolidated with delivery)
**Date:** 2026-05-05
**Step ID:** S-005
**Author:** Ben Hegarty (with Claude Opus 4.7)
**Topic slug:** `request-grid-perf`
**Governing docs:**
- [`HLPS-request-grid-perf.md`](HLPS-request-grid-perf.md) — APPROVED
- [`IS-request-grid-perf.md`](IS-request-grid-perf.md) — APPROVED
- [`SPEC-S-004-startswith-perfield-filters.md`](SPEC-S-004-startswith-perfield-filters.md) — APPROVED

---

## 1. Purpose

S-004 changes Project and EnvironmentName grid filters from substring to prefix matching. Per HLPS Constraint §4 ("no silent shift") and HLPS SC5, that change must be reflected in a visible UI affordance. S-005 updates the placeholder text in the affected dorc-web components so users see the new semantic without having to discover it experimentally.

## 2. Requirements

### R1 — Filter input placeholder text reflects the new predicate semantics
The filter input components in the request-grid pages use placeholder text that conveys the per-field predicate semantic:

- **Project** filter input: placeholder communicates prefix semantic (e.g., "Project starts with...").
- **EnvironmentName** filter input: placeholder communicates prefix semantic (e.g., "Environment starts with...").
- **BuildNumber** filter input: placeholder communicates substring semantic (e.g., "Build contains..."). The semantic is unchanged from before S-004 but adding "contains" is consistent with the prefix wording on the other two and removes ambiguity for users who would otherwise assume all filters share the same semantic.
- **env-monitor `detailsFilter`** input: placeholder communicates the heterogeneous OR semantic (e.g., "Project starts with / Build contains..."). Acceptable simplifications include "Project / Build" with a tooltip — the JIT author chooses the clearest concise form within the input width budget.

The exact wording is the JIT author's choice provided each placeholder unambiguously communicates its semantic. The English imperative or label form ("starts with...", "contains...") is preferred over technical jargon ("LIKE 'X%'").

### R2 — Files in scope
Only the two components flagged by HLPS §1 / §3 / IS §2 S-005:

- `src/dorc-web/src/pages/page-monitor-requests.ts` — Project / Environment / Build grid filter inputs.
- `src/dorc-web/src/components/environment-tabs/env-monitor.ts` — `detailsFilter` input.

No other components are modified. No JS / TS logic changes — this is text-only.

### R3 — No backend or contract change
The filter `path` constants (`'Project'`, `'EnvironmentName'`, `'BuildNumber'`, `'EnvironmentNameExact'`) are unchanged. No new TypeScript types, no API client regeneration, no `dorc-api` model changes.

### R4 — Test coverage
Existing component tests (e.g., `page-monitor-requests.test.ts` from PR #338) continue to pass. New tests are not required because S-005 is a textual change to placeholder strings; if the JIT author elects to add a snapshot or DOM-text assertion confirming the new placeholder, that is acceptable but not mandated.

### R5 — Build cleanly
`npm run build` and `npm run lint` for `src/dorc-web` complete without new errors or warnings.

## 3. Out of scope

- Backend predicate changes — owned by S-004.
- Release notes — owned by S-007.
- Filter input layout / styling beyond the placeholder text.
- Internationalisation / translation — DOrc's current frontend uses English only; this spec follows that convention.
- Tooltip implementation if the JIT author chooses one — the spec does not mandate a particular Vaadin component pattern; the requirement is the visible affordance, not the mechanism.
- Adding placeholder text to filter inputs that S-004 did not change semantics for (Id, Username, Status, Components, Components in env-monitor).

## 4. Acceptance Criteria

S-005 is "done" when **all** hold:
1. The four placeholder texts (Project, Environment, Build in page-monitor-requests; Details in env-monitor) are updated per R1.
2. R2 satisfied — only the two listed files modified.
3. Existing tests pass; build is clean per R5.
4. Adversarial Quality Gate has approved the diff.

## 5. Risks

- **Placeholder wording subjectivity.** The exact phrasing is a JIT-author choice. Reviewers may re-litigate wording at the gate; that's acceptable — the gate's role is to catch genuinely confusing affordances.
- **Localisation not addressed.** If DOrc adds i18n later, this hard-coded English will need extracting. That's out of scope for the incident fix.

## 6. Status / Review Trail

| Round | Date | Status | Reviewers | Outcome |
|-------|------|--------|-----------|---------|
| —     | 2026-05-05 | DRAFT | — | Initial draft. |
