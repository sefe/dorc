# Implementation Sequence: UI Declutter

**Status:** APPROVED  
**Created:** 2026-04-10  
**Governing Document:** HLPS-UI-Declutter.md (APPROVED)

---

## Overview

This IS breaks the HLPS into 5 atomic steps that address both problems: the projects page action button clutter (Problem 1.2) and the ellipsis icon misuse (Problem 1.1). Problem 1.2 is addressed first because it is the primary user-visible improvement and the original motivation for this work.

## U1 Resolution

**Decision:** Use `@vaadin/menu-bar` for the overflow menu.

**Rationale:** `menu-bar` natively renders a clickable button that opens a dropdown — exactly the ellipsis-button-to-dropdown pattern needed. `context-menu` is designed for right-click/target-based triggers and would require additional plumbing to achieve the same UX. Both are available in Vaadin 25; `menu-bar` is the simpler integration.

---

## Cross-Cutting Requirements

The following apply to every step and are not repeated in individual verification intents:

- **SC6:** All existing custom event dispatches from unchanged controls/columns must continue to function identically after each step.
- **SC7:** The application must build and lint successfully with no regressions after each step.
- **Row-click affordance (S-002–S-005):** Any row that gains click-to-navigate behaviour must provide a visual affordance indicating interactivity — at minimum `cursor: pointer` on hover and a subtle hover highlight. This ensures users can discover that rows are clickable. (Does not apply if the HLPS §3.1 fallback icon button is used for S-004/S-005.)

---

## Steps

### S-001: Replace projects page action buttons with overflow menu

**What changes:** The `project-controls` component and its integration in `page-projects-list`. The 6-7 individual icon buttons are replaced with a single `vaadin-menu-bar` rendering an ellipsis icon button that opens a dropdown with labeled action items.

**Why:** Addresses HLPS Problem 1.2 (cluttered projects list) and SC3, SC4, SC5. This is the primary user-facing improvement.

**Dependencies:** None.

**Verification intent:** The projects page renders one menu button per row. Clicking it shows a dropdown with all 6-7 actions (labeled with text). Delete is only visible for admins. All actions dispatch the same custom events as before.

**Status:** COMPLETE — Squash-merged to target branch.

---

### S-002: Replace ellipsis with row-click in page-monitor-requests

**What changes:** The `idRenderer` in `page-monitor-requests.ts`. The ellipsis button next to the request ID is removed. An `active-item-changed` handler on the grid navigates to the detail view when a row is clicked.

**Why:** Addresses HLPS Problem 1.1 for this view and contributes to SC1, SC2. The ID column currently renders `{id} [...]` — row-click is a cleaner interaction since there are no conflicting interactive elements in other columns (the request-controls column has its own buttons but these handle `click` events that won't conflict with `active-item-changed`).

**Dependencies:** None (independent of S-001).

**Verification intent:** Clicking any part of a row (other than the request-controls buttons) navigates to the detailed results view. The ellipsis icon no longer appears. Existing request-controls buttons (cancel, etc.) and their event dispatches continue to function identically.

---

### S-003: Replace ellipsis with row-click in env-monitor

**What changes:** The `idRenderer` in `env-monitor.ts`. Same pattern as S-002 — the ellipsis button is removed and row-click navigation is added.

**Why:** Addresses HLPS Problem 1.1 for this view. The `env-monitor` component is structurally near-identical to `page-monitor-requests` for this interaction pattern.

**Dependencies:** None (independent of S-001, S-002).

**Verification intent:** Same as S-002 but within the environment monitor tab context. Existing request-controls buttons and their event dispatches continue to function identically.

---

### S-004: Replace ellipsis in component-deployment-results log column

**What changes:** The `_logRenderer` in `component-deployment-results.ts`. The ellipsis button next to the truncated log text is replaced. The JIT spec will determine whether row-click or a non-ellipsis icon button (per HLPS §3.1 fallback) is used, based on conflict analysis with text selection and existing links in the grid.

**Why:** Addresses HLPS Problem 1.1 for this view and contributes to SC1. This is a U3 case — the ellipsis sits inside a log preview cell alongside selectable text, and the grid has links in the component name column. Per the HLPS design intent, if row-click conflicts with text selection or existing links, the fallback is a non-ellipsis icon button.

**Dependencies:** None.

**Verification intent:** The ellipsis icon (`vaadin:ellipsis-dots-h`) no longer appears (SC1). The full log can still be viewed via the replacement interaction. Text selection in the log preview and component name links remain functional. The log-viewing event dispatch is preserved.

---

### S-005: Replace ellipsis in component-previous-attempts log column

**What changes:** The `componentLogRenderer` in `component-previous-attempts.ts`. Same pattern as S-004 — the ellipsis in the log preview cell is replaced.

**Why:** Addresses HLPS Problem 1.1 for this view. Structurally similar to S-004.

**Dependencies:** Soft dependency on S-004 — independent for execution, but the interaction approach chosen in S-004 should be applied consistently here for UX coherence.

**Verification intent:** Same as S-004 — the ellipsis icon (`vaadin:ellipsis-dots-h`) no longer appears (SC1). The full log can still be viewed via the replacement interaction. Text selection and the log-viewing event dispatch are preserved.

---

## Step Summary

| Step | Component | Problem Addressed | HLPS Success Criteria |
|------|-----------|-------------------|-----------------------|
| S-001 | project-controls / page-projects-list | 1.2 (clutter) | SC3, SC4, SC5, SC6, SC7 |
| S-002 | page-monitor-requests | 1.1 (ellipsis misuse) | SC1, SC2, SC6, SC7 |
| S-003 | env-monitor | 1.1 (ellipsis misuse) | SC1, SC2, SC6, SC7 |
| S-004 | component-deployment-results | 1.1 (ellipsis misuse) | SC1, SC2*, SC6, SC7 |
| S-005 | component-previous-attempts | 1.1 (ellipsis misuse) | SC1, SC2*, SC6, SC7 |

\* **SC2 fallback note:** If the U3 fallback path is taken for S-004/S-005 (non-ellipsis icon button instead of row-click), SC2 is considered met for those steps per the design-intent fallback documented in HLPS §3.1. SC1 (no ellipsis icon for single-action navigation) continues to apply regardless.

## Ordering Rationale

All steps have no hard dependencies and can be executed in any order. The recommended order (S-001 first) prioritises the highest-impact user-visible improvement. S-002 and S-003 are grouped as they share the same pattern and can be developed in parallel. S-004 and S-005 are grouped as they share the U3 concern — it is recommended that S-004 be executed first so the approach decision (row-click vs. fallback icon) can be carried forward to S-005 for consistency, avoiding potential rework.
