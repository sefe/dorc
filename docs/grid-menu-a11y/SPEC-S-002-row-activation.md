# SPEC-S-002 — Grid row keyboard/pointer activation, parity across both pages

| Field | Value |
|---|---|
| Status | APPROVED — Pending user approval |
| Owner | Ben Hegarty |
| Governing IS | `docs/grid-menu-a11y/IS-grid-menu-a11y.md` (APPROVED) |
| Governing decision | `docs/grid-menu-a11y/DECISION-U-2.md` (APPROVED) |
| Step | S-002 |
| Target branch | `claude/github-azure-devops-interop-vhDvn` (PR #584) |
| Files in scope | `src/dorc-web/src/pages/page-monitor-requests.ts`, `src/dorc-web/src/components/environment-tabs/env-monitor.ts` |

---

## 1. Purpose

Rework row activation on the two monitor grid pages so that:

- pointer activation works for mouse, touch, and pen;
- keyboard users can activate a focused row with Enter or Space and
  receive the same outcome as a mouse click;
- arrow-key navigation between rows continues NOT to open the detail
  panel, including under the existing `activeItem = null` reset
  re-entry case;
- focus inside cell-internal controls (column-header filter inputs,
  sort buttons) does NOT cause row activation by either pointer or
  keyboard;
- the two pages remain at strict parity — same accepted pointer
  types, same activation keys, same arrow suppression, same listener
  lifecycle.

Per DECISION-U-2.md, this is delivered via Path A: keep the
`active-item-changed` subscription, drop the existing pointer-gating
mechanism, retain the existing null-reset guard, and add a small
row-level Enter handler isolated from cell-internal interacting-mode.

## 2. Pre-execution self-audit

Before writing code for this step, the delivery agent must verify all of:

- [ ] HLPS-grid-menu-a11y status = APPROVED (user-approved)
- [ ] IS-grid-menu-a11y status = APPROVED (user-approved)
- [ ] DECISION-U-2 status = APPROVED
- [ ] This SPEC status = APPROVED (user-approved or auto-pilot enabled)
- [ ] No in-flight adversarial reviews on grid-menu-a11y artifacts
- [ ] Working tree is on `claude/github-azure-devops-interop-vhDvn` and
      clean (no uncommitted tracked changes)

If any item is unchecked, **STOP** and address it before proceeding.

## 3. Branch and commit strategy

- All work lands on the existing PR branch
  `claude/github-azure-devops-interop-vhDvn`. No per-step branch.
- A **single commit** introduces all changes for both files. The
  parity requirement is non-negotiable per HLPS §4.2 and IS §4.1; one
  page must not land without the other.
- Commit message style follows existing PR commits (e.g.
  `PR #584: …`). Wording at the delivery agent's discretion provided
  it identifies the step and the parity scope.
- Push to the remote follows the same pattern as prior PR #584
  commits.

## 4. Behavioural requirements

This section describes what the changed code must do, in
consumer-observable terms. Identifiers, exact event names handled,
listener registration sites, and similar implementation detail are
the delivery agent's choice within these constraints.

### 4.1 Pointer activation

R1. Clicking a row with mouse, touch, or pen opens the detail panel
    for the row clicked (same outcome as today's mouse-only path).
R2. Clicking on a focusable cell-internal control (a column-header
    filter input or a sort button) does NOT open the detail panel —
    the click is consumed by the inner control. (This is already
    enforced by the grid's own logic; no consumer work required, but
    must not regress.)
R3. After the detail panel is opened by a row click, the page must
    reset the grid so that a subsequent click on the *same* row
    re-opens the detail panel. (This already works today via the
    null-reset; the spec records the requirement so the delivery
    agent does not accidentally drop it.)

### 4.2 Keyboard activation

R4. With keyboard focus on a grid row, pressing **Space** opens the
    detail panel for the focused row.
R5. With keyboard focus on a grid row, pressing **Enter** opens the
    detail panel for the focused row.
R6. With keyboard focus inside a cell-internal control (filter input,
    sort button, etc.) — i.e. when the grid is in its built-in
    interacting-mode — pressing Enter must NOT open the detail panel.
    The Enter keystroke belongs to the inner control in that
    context.
R7. Pressing arrow keys, Home, End, PageUp, PageDown, or Tab while
    moving focus between rows does NOT open the detail panel.

### 4.3 Re-entry / null-reset

R8. When the page resets the grid's active item to clear the
    activation state, the resulting active-item-changed re-entry
    must NOT cause the detail panel to be opened a second time. (The
    existing early-return on a falsy detail value satisfies this; it
    must be retained.)

### 4.4 Listener lifecycle

R9. Any new event listener added by S-002 must be registered in
    `connectedCallback` and removed in `disconnectedCallback`,
    consistent with existing lifecycle discipline on these
    components.
R10. Both pages must register and unregister exactly the same set of
    listeners in the same lifecycle hooks. Asymmetry between the two
    pages constitutes a parity defect.

### 4.5 Parity scope

R11. Every behaviour change introduced by S-002 must apply identically
    to both files. Parity is asserted on the **S-002 diff surface**,
    not on pre-existing template-bound handlers or other code outside
    the diff. The S-002 diff must not differ between files in any of:

    - which keys are handled by S-002-introduced listeners,
    - which pointer types are accepted (must remain identical to
      today, i.e. all three),
    - which lifecycle hooks register or remove S-002-introduced
      listeners,
    - which cell-internal-isolation signal is used to suppress Enter,
    - the user-facing outcome on activation.

    Parity is verified at the level of **observable behaviour and
    listener-set identity**, not character-for-character diff
    equivalence. Two functionally-identical implementations with
    minor stylistic differences (arrow function vs named method,
    differing local variable names) are acceptable; two
    implementations with differing event flow shapes are not.

R12. The single-commit rule means partial landings are a parity
    defect. If a parity-only finding emerges in adversarial review
    that cannot be addressed in the same commit, the standard
    cycle-limit escalation applies (CLAUDE.local.md §4); per IS §4.1
    this is the agreed path, not splitting the step.

### 4.6 Mandatory removals

R13. The existing pointer-gate mechanism — comprising the
    `_pointerActive` instance field, the `@mousedown` template
    binding that sets it, and the `if (!_pointerActive) return`
    early-return in the row-click handler — must be removed in its
    entirety from BOTH files. Per DECISION-U-2 §3, the gate protects
    against a phantom problem (arrow keys never reach
    `active-item-changed`) and its presence blocks Space activation
    on a focused row. Retaining any part of the gate on either file
    constitutes a regression against HLPS §4.2 and a parity defect
    against R11.

### 4.7 Configuration assumptions

R14. Both grids must present row-level focus reachability via the
    grid's native row-focus mode for keyboard activation to land on
    a row (see DECISION-U-2 Q4). If either grid is currently
    configured cell-focus by default and Space activation does not
    open the detail panel under T6, this is a pre-existing
    configuration gap rather than a S-002 defect; the finding must
    be escalated to the user (per CLAUDE.local.md §4 cycle-limit
    rule) for a binding decision on whether S-002 should expand to
    address it. S-002 must not silently switch the grid's focus mode
    without explicit user authorisation.

## 5. Test-first approach

The delivery agent must define and exercise the following manual
test cases before declaring the step complete. There is no automated
a11y test harness in `dorc-web` (HLPS U-1 deferred); the tests are
manual against a local dev server.

Each test must be performed on **both** pages
(`/monitor/requests`-equivalent and the env-specific monitor view
inside an environment tab).

### T1. Mouse activation

Click a row with the mouse. Detail panel opens. Close it. Click the
**same** row again. Detail panel opens again. Close it. Click a
*different* row. Detail panel opens for the new row.

### T2. Touch activation (DevTools touch emulation)

Enable Chromium DevTools "touch" emulation. Tap a row. Detail panel
opens. Close. Tap the same row. Detail panel opens.

### T3. Pen activation

If a stylus / pen device is available, tap a row with the pen.
Detail panel opens. If unavailable, T3 is satisfied transitively
by T2: per DECISION-U-2 §3, both touch and pen activation route
through the browser-synthesised `click` event on the same listener
path; T2's coverage of the touch-synthesised click is sufficient
evidence for the pen path under the same code surface. The
delivery agent must document the substitution choice in the
verification record.

### T4. Cell-internal pointer interaction

Click inside a column-header filter input. Type a character. Detail
panel does NOT open at any point. Click on a sort button in a column
header. The column sort changes; detail panel does NOT open.

### T5. Keyboard navigation does not activate

Tab into the grid; row focus indicator appears. Press ArrowDown five
times. Detail panel does NOT open at any point. Press ArrowUp two
times. Detail panel still has not opened. Press End. Detail panel
still has not opened.

### T6. Space activates focused row

After T5, with a row focused, press Space. Detail panel opens for
the focused row. Close it.

### T7. Enter activates focused row

After T6 (panel closed), with a row focused, press Enter. Detail
panel opens for the focused row. Close it.

### T8. Enter inside cell-internal control does NOT activate row

Tab into the grid. Move focus into a column-header filter input
(via Tab or arrow keys per the grid's internal navigation). Press
Enter. Detail panel does NOT open. The filter input behaves
normally.

### T8a. Transition out of interacting-mode does NOT leave gate stuck

Continuing from T8: while focus is inside the filter input, exit
interacting-mode (e.g. press Escape, or Tab/arrow back out to row
focus). Then with row focus restored, press Enter. Detail panel
opens for the focused row. This verifies the interacting-mode
signal is read at keystroke time, not latched stale.

### T9. Re-entry / null-reset

After T6 or T7, immediately re-trigger the same activation key on
the same row. Detail panel opens again (the null-reset between the
two activations must not have left the gate stuck).

### T10. Parity comparison

T1–T9 (and T8a) must already have been executed on **both** pages
per the preamble. T10 is a comparison step: confirm that every
outcome on the second page matches the corresponding outcome on the
first page exactly. Any divergence is a parity defect under R11.

## 6. Acceptance criteria

The step is acceptable when **all** of:

A1. Every requirement R1–R14 is satisfied on both files.
A2. Every manual test T1–T10 (including T8a) passes on both pages.
A3. The two files' **S-002 diffs**, when read side-by-side, show
    parity in:
    - the set of S-002-introduced listener registrations added in
      `connectedCallback`,
    - the set of S-002-introduced listener removals in
      `disconnectedCallback`,
    - the activation-handler logic shape,
    - the cell-internal-isolation predicate,
    - the *complete* removal of the pointer-gate mechanism per R13
      (i.e. neither file retains any of: `_pointerActive`,
      `@mousedown` binding, the early-return guard).
    Parity is asserted on observable behaviour and listener-set
    identity (per R11), not on character-for-character diff
    equivalence. Pre-existing template bindings outside the S-002
    diff are out of scope for parity assessment.
    Any asymmetry must be explicitly justified in the commit message
    or rejected as a parity defect.
A4. `npm run build` succeeds; existing lint produces no new findings
    on the two files.
A5. The HLPS §4.3 mouse-path regression checklist (subset relevant to
    row activation) passes:
    - row click → detail panel opens (both pages);
    - clicking outside the row click area while panel is open does
      not interfere;
    - existing event/route dispatch from the row click is unchanged.
A6. The single-commit rule (R12) is honoured: one commit covers both
    files; no fix-up commits between this step's commit and the next
    step's commit (per IS §4.2 monotonic ordering).

## 7. Verification environment

Per IS §6.1: Windows + Chromium + (optionally) NVDA. NVDA is not
strictly required for S-002 since the success criteria are
pointer/keyboard, not screen-reader; however, an NVDA spot-check on
T6/T7 (Space/Enter activation announcement) is a good practice.
Local dev server runs via `npm run dev` (or equivalent).

## 8. Out of scope

- Any change to `project-controls.ts` (covered by S-003a / S-003b).
- Any change to other grids or pages.
- Any visual change to row hover, selection styling, or detail panel
  layout.
- Migration to `cell-activate` / `row-activate` listeners — this is
  Path B, ruled out per DECISION-U-2.md §4.
- Changes to the grid's data provider, column model, or filter
  inputs.
- A new automated a11y test harness — HLPS U-1 deferred.
- Any change to the existing `_shouldPreventCellActivationOnClick`
  behaviour — this is internal grid logic.

## 9. Adversarial Review

The diff produced by S-002 is reviewed by an Adversarial panel as a
code review. Per CLAUDE.md §4 review scope rules for code:

- Reviewers evaluate ONLY the diff between this branch state and
  `main` (or its merge-base); pre-existing issues are out of scope.
- Reviewers must be briefed on accepted risks from this approved
  spec and DECISION-U-2 (specifically: Path B not chosen; parity
  lock is non-negotiable; touch/pen via standard click; the
  `interacting` reflected attribute is the consumer-observable
  signal of choice; pre-existing template-bound handlers outside
  the S-002 diff are out of scope for parity assessment per R11/A3;
  R13 mandates total removal of the pointer-gate mechanism).
- Severity calibration per CLAUDE.local.md.

## 10. Risk acknowledgements

| Risk | Mitigation |
|---|---|
| The `interacting` attribute is documented `@private` in JSDoc but is `reflectToAttribute: true`. A future Vaadin major version could remove the reflection, breaking the consumer-side observation. | The DECISION-U-2 version anchor pins this against `@vaadin/grid 25.1.2`. A future Vaadin upgrade flips this risk and requires re-investigation; that's a separate IS revision, not a S-002 concern. |
| Vaadin's grid-internal keyboard model dispatches `row-activate` on Space only when the focused element IS a row; if rows are not in row-focus mode, Space dispatches `cell-activate` instead — and the row-activation path may behave differently. | T6 (Space) explicitly tests this on both pages. If either page's grid configuration places focus on a cell rather than a row by default and T6 fails as a result, this is a pre-existing configuration gap outside S-002 scope per R14; the finding must be escalated to the user for a binding decision (per CLAUDE.local.md §4 cycle-limit rule), not silently accepted, and not silently switched without authorisation. |
| Incomplete removal of the `_pointerActive` pointer-gate between the two files — e.g. instance field removed but the `@mousedown` binding retained, or removed on one page only. | R13 enumerates all three pieces of the gate (instance field, template binding, guard) as a single mandatory removal applying identically to both files. A3 includes a positive parity check for the absence of all three pieces in both files. Reviewers are briefed on this dimension (see §9). |

---

## Review History

### R1 — DRAFT → REVISION

Three reviewers (clarity/falsifiability, parity discipline,
decision traceability) returned APPROVE_WITH_FIXES.

| Theme | Reviewers | Disposition | Resolution |
|---|---|---|---|
| Parity scope must distinguish behavioural vs implementation parity AND scope to S-002-introduced changes vs pre-existing template surface | R1-clarity F-01, R1-parity F-01 (HIGH) | Accept | R11 reworded to explicitly scope to "the S-002 diff surface, not pre-existing template-bound handlers"; observable behaviour + listener-set identity is the parity dimension, not character-for-character equivalence. A3 updated to mirror. |
| Pointer-gate removal must be a numbered requirement | R1-traceability F-01, R1-parity F-02 | Accept | New R13 in §4.6 enumerates the three pieces of the gate (`_pointerActive` field, `@mousedown` binding, early-return guard) as a single mandatory removal applying identically to both files. A3 includes a positive absence check. |
| Row-focus mode confirmation must be a numbered requirement | R1-traceability F-02 | Accept | New R14 in §4.7 records the configuration assumption, names T6 as the empirical check, and routes any failure to the cycle-limit escalation path rather than silent acceptance or unauthorised mode switching. |
| Transition test for sticky interacting-mode | R1-clarity F-02 | Accept | New T8a inserted after T8 — exit interacting-mode then verify Enter on a focused row opens the panel. |
| Risk row 2 (Space + cell-focus) must include remediation path | R1-parity F-03 | Accept | Risk row 2 mitigation extended with explicit escalation route per R14. |
| Missing risk for incomplete pointer-gate removal between files | R1-parity (missing risk) | Accept | New §10 risk row added cross-referencing R13 and A3. |
| T10 reframed as parity comparison rather than rerun | R1-parity F-04 | Accept | T10 now states explicitly that T1–T9 (and T8a) are executed on both pages per the preamble, and T10 is a comparison step asserting outcome parity. |
| T3 pen substitution policy | R1-clarity F-03 | Accept | T3 now specifies that absence of pen hardware is satisfied transitively by T2 (touch synthesises the same `click` per DECISION-U-2 §3); substitution must be documented. |
| A4 build/lint wording | R1-clarity F-04 | Accept | A4 reworded — `npm run build` succeeds; lint produces no new findings on the two files. |
| R3 traceability cross-reference | R1-traceability F-03 | Defer | Optional improvement; the §6 reverse traceability suffices. |

After this revision, status returns to `IN REVIEW` for R2. R2 reviewers
must verify R1 fixes, check for regressions, and (per CLAUDE.local.md
§4 Re-Review Scoping) NOT mine for new findings on R1 text that was
implicitly accepted.

### R2 — IN REVIEW → APPROVED

| Reviewer | Lens | Outcome |
|---|---|---|
| Reviewer A (Opus) | Clarity / falsifiability | **APPROVE** — All four R1 findings verified resolved; R13/R14/T8a integrate cleanly; R11 reword preserves HLPS §4.2 parity scope; no regressions. |
| Reviewer B (Sonnet) | Parity discipline | **APPROVE** — All five R1 findings (incl. missing-risk) verified; R13 + A3 makes parity-of-removal fully diff-auditable; new content internally consistent. |
| Reviewer C (default) | Decision traceability | **APPROVE** — R13 closes the pointer-gate-removal coverage gap; R14 closes the row-focus-mode coverage gap; full forward and reverse traceability preserved; no orphaned sources. |

Unanimous approval. Status transitions to
`APPROVED — Pending user approval` per CLAUDE.local.md §2 Document
Status Lifecycle.
| A new Enter handler at document or grid level could swallow Enter in unexpected contexts (e.g. a future cross-page modal). | R6 + R9 (interacting-mode isolation, narrow listener scope) plus the lifecycle parity rule constrain the listener's reach. The S-003a/b work installs no document-level keydown listeners, so cross-step coupling per IS §4.2 is bounded. |
| Parity drift between commits — page A reviewer-fix lands but page B equivalent forgotten. | A3 explicitly mandates side-by-side diff parity; reviewers are briefed to flag asymmetry. R12 forbids partial landings. |
