# HLPS — Grid-row & Action-menu Accessibility

| Field | Value |
|---|---|
| Status | APPROVED — Pending user approval |
| Owner | Ben Hegarty |
| Origin | PR #584 (Copilot review) — unresolved threads on `project-controls.ts`, `page-monitor-requests.ts`, `env-monitor.ts` |

---

## 1. Problem Statement

Two recently-introduced interaction patterns in `dorc-web` exclude users who do
not navigate with a mouse:

1. **Project actions overflow menu** (`project-controls.ts`): the per-row
   actions dropdown is rendered as raw `<div>` elements appended to
   `document.body`. It has no menu/menuitem ARIA roles, no keyboard activation
   (cannot be opened via Enter/Space, no arrow-key navigation across items, no
   ESC to close), no focus management (focus is not moved into the menu, not
   trapped, and not restored to the trigger on close), and no screen-reader
   semantics. The trigger `<vaadin-button>` does carry a `title`/`aria-label`,
   so the entry point is announced — the failure starts as soon as the menu
   opens.

2. **Grid row → details panel** (`page-monitor-requests.ts`,
   `env-monitor.ts`): row activation has been gated on `@mousedown` setting a
   `_pointerActive` flag, with `onRowClick` early-returning when the flag is
   false. This was introduced to suppress detail-panel pop-ups during keyboard
   arrow-key navigation through the grid, but the gate is too strict: it also
   blocks legitimate keyboard activation (Enter/Space) and is unreliable on
   pointer types where `mousedown` is not synthesised consistently
   (pen, some touch contexts).

The user-visible effect in each case: a primary feature path is operable only
with a mouse.

## 2. Scope

In scope:

- `src/dorc-web/src/components/grid-button-groups/project-controls.ts` —
  full keyboard/screen-reader/focus accessibility for the overflow menu.
- `src/dorc-web/src/pages/page-monitor-requests.ts` — keyboard-accessible
  request-detail activation, retaining suppression of arrow-key drift.
- `src/dorc-web/src/components/environment-tabs/env-monitor.ts` — same
  keyboard-accessible deployment-detail activation, same retention of
  arrow-key drift suppression.

Out of scope:

- A wider `dorc-web` accessibility audit. Other surfaces (dialogs, forms,
  navigation) are not addressed here and may have unrelated defects.
- Replacing the `project-controls` dropdown with `<vaadin-menu-bar>` or
  `<vaadin-context-menu>`. Ruled out: prior experience with `vaadin-menu-bar`
  has been unreliable in this codebase. The menu is to remain a custom
  implementation, made accessible.
- Touch-device ergonomics beyond making the same activation surfaces
  reachable. Long-press / context gestures are not in scope.

## 3. Constraints

- No reintroduction of `@vaadin/menu-bar` or migration to
  `<vaadin-context-menu>`.
- Visual presentation of the overflow menu (ellipsis trigger, dropdown
  position, hover styling, delete-action separator) must be preserved.
- The grid row-click → detail-panel flow must remain visually identical for
  mouse users; arrow-key navigation across rows must continue to NOT pop the
  detail panel (the original reason the `_pointerActive` gate was added).
- No new runtime dependencies.
- Existing custom events emitted by `project-controls.ts`
  (`open-project-metadata`, `open-access-control`, `open-project-envs`,
  `open-project-components`, `open-project-ref-data`, `delete-project`) and
  the `Router.go` short-circuit for `open-project-audit-data` must continue
  to fire with identical detail payloads.
- Each menu item must expose a stable accessible name equal to its visible
  label — labels must not be moved into icon-only / tooltip-only forms.

## 4. Success Criteria

A change is successful when, on the affected surfaces:

1. **Project overflow menu**
   - Trigger button is reachable via Tab and announces its role/label.
   - Enter or Space on the trigger opens the menu and moves focus to the
     first menu item.
   - Up/Down arrow keys move focus between menu items, with focus wrapping
     at the ends.
   - Home/End move focus to the first/last item.
   - Enter or Space on a focused menu item invokes the same action as a
     mouse click (event dispatched or `Router.go` called identically).
   - ESC closes the menu and restores focus to the trigger.
   - Tab (or Shift+Tab) from inside the menu closes it and moves focus to
     the next (or previous) focusable element in the **trigger's** tab
     order — i.e. the focus destination is determined as if focus had been
     on the trigger and Tab/Shift+Tab were pressed there. Focus must NOT
     be left on `<body>` or in the body-appended overlay's DOM position.
   - Click outside the menu still closes it (existing behaviour).
   - Trigger advertises that it opens a menu (e.g. via `aria-haspopup`)
     and the open menu is programmatically associated with its trigger
     (e.g. via `aria-controls` / `aria-labelledby`) so screen readers can
     announce the trigger→menu relationship and the trigger's
     expanded/collapsed state.
   - Menu and items expose appropriate ARIA roles (`menu`, `menuitem`).
   - The currently-focused menu item exhibits a visible focus indicator
     meeting WCAG 2.4.7, distinct from the existing hover styling.
   - When `deleteHidden` is true, the menu omits the Delete item entirely;
     arrow-key wrap and Home/End operate over the visible items only.

2. **Grid row activation (both `page-monitor-requests` and `env-monitor`)**
   - A grid row can receive keyboard focus (with a visible focus
     indicator) and is reachable via the grid's standard keyboard
     navigation.
   - Clicking a row (mouse, touch, or pen) opens the detail panel — same
     as today for mouse, fixed for touch/pen.
   - Pressing Enter or Space on a focused row opens the detail panel.
     Activation must fire only at the row level — pressing Enter inside a
     focusable cell-internal control (e.g. column-header filter inputs,
     sort-button activation) must NOT trigger the detail panel.
   - Arrow-key navigation between rows does NOT open the detail panel,
     including when the grid programmatically resets `activeItem` to
     `null` after the panel is closed.
   - **Parity** between `page-monitor-requests` and `env-monitor`:
     - same set of pointer types accepted (mouse, touch, pen);
     - same keyboard activation keys (Enter, Space);
     - same arrow-key suppression behaviour;
     - same lifecycle for the row-activation gate (added in
       `connectedCallback`, removed in `disconnectedCallback`).

3. **No regressions**
   - All existing mouse interactions still work unchanged. Specifically:
     - row click → detail panel opens (both pages);
     - ellipsis-button click → overflow menu opens;
     - menu-item click → corresponding event/route fires (six existing
       events plus the audit-action `Router.go` short-circuit);
     - click outside the open menu closes it;
     - document scroll while the menu is open closes it.
   - The previously-resolved Copilot row-activation threads (the four
     comments concerning `_pointerActive`, `@mousedown` gating, and
     keyboard accessibility on `page-monitor-requests` and `env-monitor`)
     do not regress.
   - The previously-applied conflict resolution on `project-controls.ts`
     (audit action routing via `Router.go`) is preserved.

## 5. Verification Intent

- Manual keyboard walk-through of each surface in a real browser, using only
  Tab / Shift+Tab / arrow keys / Enter / Space / ESC, verifying each success
  criterion.
- Explicit arrow-drift regression sequence: with a row focused, press the
  arrow key to navigate across at least five rows confirming the detail
  panel stays closed; immediately follow with Enter/Space on a focused row
  confirming it opens. This must be performed on both pages.
- Manual screen-reader spot-check (NVDA on Windows is sufficient) confirming
  menu open/close and item focus are announced.
- Manual touch spot-check on the row-activation flow via browser device
  emulation (or real device if available).
- Existing dorc-web build (`npm run build`) and lint must pass.

There is no automated a11y test harness in `dorc-web` today. Adding one is
out of scope for this HLPS — see Unknowns Register entry U-1.

## 6. Unknowns Register

| ID | Description | Owner | Blocking? |
|----|-------------|-------|-----------|
| U-1 | Should automated a11y testing (axe-core / @testing-library/jest-dom) be added as part of this work, or deferred? | User | No — defer by default; manual verification suffices for this HLPS. |
| U-2 | Vaadin `<vaadin-grid>` may emit `active-item-changed` from internal focus changes (not just keyboard arrows). The exact set of triggers must be confirmed, including the re-entry case where the existing handlers reset `grid.activeItem = null` (which itself fires the event with `e.detail.value === null`). The investigation must determine whether the row-activation step can refine the existing event-based gate or must replace it with a different mechanism (e.g. a `keydown` listener at grid level, or row-level focus + Enter/Space handling). | Agent | **Must be resolved before the JIT spec for the row-activation step is APPROVED.** Not blocking the HLPS itself. |
| U-3 | Whether the overflow menu should also be reachable via a right-click / context-menu gesture on the row. | User | No — assume **No** unless specified. |

No blocking unknowns. The HLPS is ready to proceed to Adversarial Review on
user approval.

## 7. Out-of-scope clarifications

- **Project-controls visual redesign** — not in scope. Same ellipsis trigger,
  same dropdown positioning, same colours. Only the underlying DOM/ARIA/event
  wiring changes.
- **`add-edit-project.ts` UNC handling** — already addressed as a side fix in
  PR #584 (commit `50ea559e`); not part of this plan.
- **`@vaadin/menu-bar` removal** — already addressed in commit `50ea559e`; not
  part of this plan.

---

## Review History

### R1 — DRAFT → REVISION

Three independent reviewers (web-a11y lens, process/scope lens,
verification/falsifiability lens) returned APPROVE_WITH_FIXES. The R1
findings and dispositions are summarised below; full reviewer reports are
available in agent transcripts.

| Theme | Reviewers | Disposition | Resolution |
|---|---|---|---|
| Trigger↔menu ARIA wiring missing (`aria-haspopup`, `aria-controls`) | R1-a11y F-01 | Accept | Added bullet to §4.1. |
| Focus-visible indicator for menu items (WCAG 2.4.7) | R1-a11y F-02 | Accept | Added bullet to §4.1. |
| Accessible-name guarantee for menu items | R1-a11y F-03 | Accept | Added constraint to §3. |
| Tab-out-of-menu must follow APG (focus to "after trigger") | R1-a11y F-06, R1-process F-02, R1-verify F-2 | Accept | Restated §4.1 Tab/Shift+Tab criterion. |
| Row keyboard focus mechanism unspecified | R1-a11y F-04, R1-verify F-1 | Accept | Added focusable-row bullet to §4.2. |
| Cell-internal controls must not swallow row Enter/Space | R1-a11y F-04 | Accept | Added clarification to §4.2 Enter/Space bullet. |
| `active-item-changed` reset-to-null re-entry | R1-a11y F-08 | Accept | Folded into §4.2 arrow-key bullet and U-2. |
| U-2 must be resolved before row-activation JIT spec is approved | R1-process F-01, R1-verify F-8, R1-a11y F-08 | Accept | Tightened U-2 wording. |
| Behaviour parity needs enumerated observables | R1-verify F-3 | Accept | Replaced single-line parity statement with enumerated list. |
| "No regressions" needs enumerated mouse paths | R1-verify F-4 | Accept | Enumerated mouse paths in §4.3. |
| `deleteHidden=true` wrap behaviour | R1-verify F-5 | Accept | Added bullet to §4.1. |
| Anchor "no regressions" by thread concern, not line number | R1-a11y F-07 | Accept | Replaced line-number references with concern-based reference in §4.3. |
| Positive arrow-drift verification sequence | R1-verify F-7 | Accept | Added explicit sequence to §5. |
| `prefers-reduced-motion` | R1-a11y F-05 | Defer to Delivery | The current dropdown has no transitions; the JIT spec for the menu step must apply this only if transitions are added. |
| Shift+Tab symmetry; ARIA-role DOM compatibility; NVDA mode; keydown-handler cleanup; event-payload cross-reference | R1-a11y F-06, R1-process F-03/F-04/Risk-A, R1-verify F-6 | Defer to Delivery | Implementation-level concerns a competent implementer resolves naturally; not HLPS-shape concerns. |
| Daemon-controls branch-context risk | R1-process Risk-C | Reject | Reviewer misidentified the current branch. The PR is on `claude/github-azure-devops-interop-vhDvn`, not the daemon-modernisation branch; the alleged shared-infrastructure overlap does not exist. |

After this revision, status returns to `IN REVIEW` for R2. R2 reviewers
must verify R1 fixes were applied appropriately, check for regressions in
unchanged text, and (per CLAUDE.local.md §4 Re-Review Scoping) NOT mine
for new findings on R1 text that was implicitly accepted.

### R2 — IN REVIEW → APPROVED

| Reviewer | Lens | Outcome | Notes |
|---|---|---|---|
| Reviewer A (Opus) | Web a11y | **APPROVE** | All R1 findings (F-01..F-08) verified as adequately addressed; no regressions; no new findings. |
| Reviewer B (Sonnet) | Process / scope | Rate-limited | Anthropic account quota exhausted before agent produced any output. |
| Reviewer C (default) | Verification / falsifiability | Rate-limited | Anthropic account quota exhausted before agent produced any output. |

Per CLAUDE.local.md §4 Reviewer Reliability protocol, two panel members
failed to produce a response. Substitution is unavailable because the
quota exhaustion is account-wide, not model-specific. Per the same
section, the user has authority to provide a binding decision when
panel reliability fails, and on `2026-04-27` the user issued a binding
override to proceed on the basis of:

1. The R1 process/scope and verification reviewers had already
   APPROVE_WITH_FIXES; their findings are itemised in the R1 Review
   History above and each was either accepted (and applied) or
   explicitly deferred / rejected with rationale.
2. The R2 web-a11y reviewer (the most domain-relevant lens for this
   work) verified that all R1 findings — including those raised by the
   other two reviewers — were adequately addressed, with no regressions
   or new contradictions detected.
3. None of the deferred or rejected R1 findings affect HLPS
   correctness; they are either implementation-level concerns for JIT
   Specs/Delivery or based on a misread of the current branch.

Status transitions to `APPROVED — Pending user approval` per
CLAUDE.local.md §2 Document Status Lifecycle.
