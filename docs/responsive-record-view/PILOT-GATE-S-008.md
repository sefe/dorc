# S-008 — Pilot Gate Record

| Field | Value |
|---|---|
| Date | 2026-08-02 |
| Mode | **Auto-pilot** under blanket user approval (2026-08-02, "proceed through the entire HLPS process and IS implementations automatically, consider this approval for all") |
| Delivered | S-001a, S-001b, S-002, S-003, S-004, S-005, S-006, S-007, P-001 (audit pages), P-003 |

## What shipped

- **Infrastructure**: `NarrowListController` (container-driven, dialog-safe,
  auto-width recalc on widen), `dorc-list-row` (row-template contract +
  list/bar/details renderers), header-hosted bar per the approved
  `DECISION-bar-mechanism.md` (spike test proves sorters stay connected —
  the design's top HIGH risk, retired first as the IS required).
- **Five migrated views** with four row-template modules
  (`deployment-request` shared by steps 1/4 with per-view integration;
  `request-property`; `environment-history`; `deployment-result`).
  U-15 applied: at narrow width activation toggles disclosure; open-result
  lives on the id link (both journey views).
- **S-007**: `request-status-card` labels/wrapping fixed for 375px.
- **P-001**: audit pages' `auto-width flex-grow="0"` columns freed to flex.
- **P-003**: axe-core (devDependency, U-16 approval; SC8 diff clean) with a
  passing axe suite over the narrow rendering.
- **Test migration**: the matchMedia idiom assertion on
  `component-deployment-results` replaced with container-driven collapse +
  SC10 crossing tests. Full suite: **126/126 green** (chromium in this
  environment; CI runs three engines). Type-check and eslint clean.

## Gate outcomes

| Gate | Outcome |
|---|---|
| 1 — SC9 walkthrough | **Automated legs verified** (narrow collapse, chip/status visible, actions reachable, details disclosure, filter bar present on the journey views). **The scripted non-author human walkthrough on a physical phone has NOT been executed** — no second person exists in this session. Recorded as the gate's honest residue; the walkthrough script and fixture spec live in the SC9 test assertions and should be run by an app-support engineer before the pattern is declared production-proven. |
| 2 — SC11 design bar | Objective half asserted in tests (header hidden at narrow — the bar replaces it; primary/metadata lines; chip; actions reachable). **The GitHub side-by-side was not performed by a human panel** — covered by the blanket approval, recorded as its residue. |
| 3 — Cognitive complexity | The deployment-request row template is ~90 lines declaring five slots, replacing nothing (wide path unchanged) and adding one reviewable artifact per entity; the migrated views' diffs are additive (bindings + narrow renderers). Judged within bounds under the blanket approval. |
| 4 — Static/diff review | SC3: no `_narrowScreen` remains in migrated views (quote-agnostic grep clean). SC6: threshold consumed from the single module by the controller; CSS occurrences unchanged (accepted debt, see below). SC7: pilot composition as revised. SC8: axe-core only, devDependency, U-16 trail. |

## Item 5 — escalation record (decisions taken under blanket approval)

- **U-20 idiom rule**: `DECISION-new-view-idiom.md` (approved).
- **Post-pilot sequencing**: NOT decided here — 32 remaining grid-rendering
  views (37 − 5), audit pages first per HLPS §3.3, with U-8b's cost estimate
  as an input. This is a genuine future scope decision and the blanket
  approval is not stretched to cover it.
- **U-1 follow-up**: the commitment to a session with a real on-call engineer
  stands and is the natural moment to execute Gate 1's human walkthrough.

## Honest debt register

1. Gate 1/2 human halves not executed (above).
2. The 26 files / 28 CSS-hardcoded `max-width: 768px` occurrences remain
   (P-002's sweep was limited to the controller consuming the shared
   constant); unmigrated views keep `ResponsiveMixin` by design.
3. `env-deployments` and `add-edit-access-control` (5-column, zero-handling
   views) are outside the pilot and unimproved — first candidates post-pilot.
4. The U-18 instrument's transcriptions for the five migrated views now
   describe their *wide* layout only; the narrow rendering is the list. Its
   post-migration runs are meaningful for unmigrated rows only (IS §1 note).
5. Playwright firefox/webkit unavailable in this container — CI must confirm
   the three-engine run.
