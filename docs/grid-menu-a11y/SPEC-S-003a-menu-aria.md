# SPEC-S-003a — Project-controls menu: ARIA semantics + accessible name + focus indicator

| Field | Value |
|---|---|
| Status | APPROVED — Pending user approval |
| Owner | Ben Hegarty |
| Governing IS | `docs/grid-menu-a11y/IS-grid-menu-a11y.md` (APPROVED) |
| Step | S-003a |
| Target branch | `claude/github-azure-devops-interop-vhDvn` (PR #584) |
| Files in scope | `src/dorc-web/src/components/grid-button-groups/project-controls.ts` |

---

## 1. Purpose

Give the project-controls overflow menu correct screen-reader semantics
and a visible keyboard focus indicator. After this step, an assistive-
technology user who opens the menu hears it announced as a menu (item
count announcement is screen-reader-dependent and verified by NVDA
spot-check in T6), and a sighted keyboard user (in S-003b) will see a
clear focus indicator on the currently-focused item.

S-003a delivers screen-reader and focus-visible value as a discrete
shippable improvement; it does not yet make the menu keyboard-operable
(S-003b builds on it).

## 2. Pre-execution self-audit

Before writing code:

- [ ] HLPS-grid-menu-a11y status = APPROVED (user-approved)
- [ ] IS-grid-menu-a11y status = APPROVED (user-approved)
- [ ] This SPEC status = APPROVED (user-approved or auto-pilot enabled)
- [ ] No in-flight adversarial reviews on grid-menu-a11y artifacts
- [ ] Working tree on `claude/github-azure-devops-interop-vhDvn`, no
      uncommitted tracked changes that would interleave with S-003a

If any item is unchecked, **STOP** and address it.

## 3. Branch and commit strategy

- All work lands on the existing PR branch
  `claude/github-azure-devops-interop-vhDvn`. No per-step branch.
- A **single commit** introduces all S-003a changes.
- Commit message style follows existing PR commits.
- S-003a → S-003b is a soft ordering preference per IS §3; S-003b's
  pre-execution audit will require S-003a to have landed.

## 4. Behavioural requirements

### 4.1 Trigger ↔ menu wiring

R1. The trigger button (the ellipsis vaadin-button) must declare —
    via an appropriate ARIA mechanism — that activating it opens a
    menu. The capability is required; the specific attribute name
    is the implementer's choice (e.g. `aria-haspopup="menu"` is the
    canonical form per the WAI-ARIA APG menu-button pattern).
R2. The trigger must expose its expanded/collapsed state via an
    appropriate ARIA mechanism that accurately reflects whether the
    dropdown is currently open (e.g. `aria-expanded` toggling
    between `"true"` and `"false"`).
R3. When the dropdown is open, the trigger and the dropdown must be
    programmatically associated such that a screen reader can
    announce the relationship. The association is required; the
    specific mechanism is the implementer's choice (e.g.
    `aria-controls` on the trigger referring to the dropdown's
    `id`, AND/OR `aria-labelledby` on the dropdown referring to the
    trigger's `id`).
R3a. The dropdown overlay must itself carry an accessible name
    consistent with the trigger's purpose (e.g. via `aria-labelledby`
    referring to the trigger). Without this, some assistive
    technologies announce only "menu" rather than naming the menu in
    relation to its trigger.
R3b. The trigger's existing accessible name (currently
    `"Project actions"`, supplied by `aria-label` on the
    `<vaadin-button>`) must remain stable across the open and closed
    states. The new ARIA additions in R1–R3 must not silently
    replace, suppress, or override the trigger's accessible name.
    T2 / T3 explicitly verify the announced name is unchanged.

### 4.2 Menu and item roles

R4. The dropdown overlay must expose the role `menu` (or an
    equivalent role per WAI-ARIA APG menu pattern) so that assistive
    technology announces it as a menu when opened. The dropdown
    must additionally declare its orientation as vertical
    (`aria-orientation="vertical"`) so screen readers prompt users
    with Up/Down arrow navigation rather than Left/Right (relevant
    once S-003b adds keyboard navigation).
R5. Each item rendered inside the dropdown must expose the role
    `menuitem`. The role must apply to the element that carries the
    item's accessible name, so screen readers reading the item read
    its label (not surrounding chrome). The dropdown's items must
    appear as direct, contiguous children of the menu element — no
    non-menuitem element (e.g. a separator `<div>` carrying
    `role="separator"`, or any other ARIA-significant element) may
    interrupt the menuitem sequence. The existing delete-item
    visual separator must remain CSS-only (top-border, top-margin,
    top-padding) and must NOT be migrated to a DOM separator
    element.

### 4.3 Accessible name parity

R6. Each menu item's accessible name (as exposed to assistive
    technology) must equal its visible text label. A visible label
    of "Edit Metadata" must be announced as "Edit Metadata", not as
    "edit", icon-only, or anything else. This satisfies HLPS §3
    accessible-name-equals-visible-label constraint.
R6a. Decorative icons inside menu items (and on the trigger button)
    must not contribute to the computed accessible name of their
    container. Icons must therefore be hidden from assistive
    technology by an appropriate mechanism (typically
    `aria-hidden="true"` on the icon element). T6 verifies the
    outcome — a menu item announced as "Edit Metadata" rather than
    "edit Edit Metadata" or similar concatenation.

### 4.4 Focus indicator

R7. When the dropdown is open and the currently-focused menu item
    receives focus (via the keyboard mechanism that S-003b will
    introduce, OR via direct programmatic focus testing in S-003a),
    the item must show a visible focus indicator distinct from the
    existing hover style. The indicator must satisfy WCAG 2.4.7
    Focus Visible (Level AA).
R8. The focus indicator must use a CSS mechanism that does not rely
    on the user interacting with a pointer first
    (i.e. `:focus-visible` semantics or equivalent). Testing
    requirement: focusing an item programmatically (e.g.
    `item.focus()` from DevTools console) must produce the
    indicator.
R9. Both light and dark theme variants must show a usable focus
    indicator. The indicator must remain perceivable against the
    item's background in both themes (matching the language of
    WCAG 2.4.7 Focus Visible — the criterion does not impose a
    numeric contrast ratio, only that the indicator be visible).

### 4.5 Visual presentation preserved

R10. The visual presentation of the menu under mouse-only
    interaction must remain unchanged from current behaviour:

    - the ellipsis trigger keeps its current icon and dimensions,
    - the dropdown opens at the same position relative to the
      trigger,
    - the existing hover styling on items continues to work
      identically,
    - the delete-action separator (top border + spacing) renders
      identically.

    The only visual addition introduced by S-003a is the focus
    indicator (R7); existing presentation must not be altered to
    accommodate it.

### 4.6 Out-of-scope changes that must not happen

R11. S-003a must NOT introduce keyboard handlers (those belong to
    S-003b). The trigger remains opened/closed by the existing
    `_toggle` click handler.
R12. S-003a must NOT change the menu's open/close behaviour, the
    outside-click handler, the scroll handler, or the
    `_selectAction` event-dispatch path. These are S-003b territory.
R13. S-003a must NOT migrate to `<vaadin-menu-bar>` or
    `<vaadin-context-menu>` (HLPS §3 constraint).

## 5. Test-first approach

There is no automated a11y harness (HLPS U-1 deferred); tests are
manual against a local dev server (`npm run dev`).

### T1. Trigger ARIA before open

Open a project list page in dev. Inspect the project actions
ellipsis trigger in DevTools. Confirm:
- `aria-haspopup` (or equivalent) is present and indicates a menu;
- `aria-expanded` (or equivalent) is present and reads "false"
  (or equivalent collapsed state);
- the trigger's accessible name (as shown in DevTools accessibility
  pane) is "Project actions" (R3b stability check, closed state).

### T2. Trigger ARIA after open

Click the trigger to open the menu. Re-inspect the trigger:
- `aria-expanded` now reads "true";
- the trigger ↔ dropdown association resolves: whichever R3
  mechanism the implementer chose (`aria-controls` on trigger
  pointing at an `id` on the dropdown, OR `aria-labelledby` on the
  dropdown pointing at an `id` on the trigger), the referenced id
  must exist in the document and connect the two elements;
- the trigger's accessible name remains "Project actions" — not
  replaced or extended by the menu's name (R3b stability check,
  open state).

### T3. Trigger ARIA after close

Click outside or click the trigger again to close. Re-inspect:
- `aria-expanded` reads "false" again;
- the dropdown is removed from the DOM (existing behaviour
  unchanged).

### T4. Menu role and orientation

With the dropdown open, inspect the dropdown overlay. Confirm:
- it carries `role="menu"` (or APG-equivalent);
- it declares `aria-orientation="vertical"`;
- it has an accessible name connected to the trigger (R3a) — e.g.
  via `aria-labelledby` resolving to "Project actions".

### T5. Menuitem roles and contiguity

With the dropdown open, inspect each item element. Confirm each
carries `role="menuitem"` and that the role is on the element
that exposes the accessible name verified in T6 — i.e. the
role-bearing element and the name-bearing element are the same
element, with no divergence between visible label, accessible
name, and role placement. Also confirm there are no non-menuitem
elements (separator divs, group containers carrying ARIA
roles, etc.) interrupting the menuitem sequence inside the
overlay.

### T6. Accessible-name-equals-visible-label

With the dropdown open, verify (via DevTools accessibility pane, or
NVDA spot-check) that each item's accessible name matches its
visible label exactly. Run for all 6 always-visible actions and the
conditional Delete action (deleteHidden = false in admin context).

### T7. Focus indicator (programmatic)

With the dropdown open, in DevTools console run
`document.getElementById('<dropdown id>').firstElementChild?.focus()`
(or equivalent — the goal is to focus the first menuitem
programmatically). Confirm a visible focus indicator appears on
that item, distinct from the hover style.

Note: programmatic `focus()` requires the menu items to be
focusable via `tabIndex` (e.g. `tabindex="-1"`). If the implementer
defers per-item `tabindex` to S-003b's roving-tabindex /
activedescendant work, T7 can alternatively be satisfied by
temporarily setting `tabIndex = -1` in DevTools before calling
`focus()`. The intent is to verify the indicator's CSS works; the
mechanism by which an item is made focusable is delivery-level.

### T8. Focus indicator across items

Repeat T7 for items at different indexes (first, middle, last,
delete). All should render the indicator identically.

### T9. Focus indicator in dark theme

Switch the app theme to dark mode. Repeat T7. Confirm the focus
indicator is still visible and distinguishable.

### T10. Mouse-only interaction unchanged

With the dropdown open, hover over items with the mouse. Confirm
hover styling is identical to today (no double-styling from
focus-visible and hover overlapping in unintended ways). Click
each item — the existing event/route dispatch behaviour must be
unchanged (no regression on the existing `_selectAction`).

### T11. Mouse-only outside-click close

With the dropdown open, click outside the menu and trigger.
Confirm the menu closes (existing behaviour unchanged) and the
trigger's `aria-expanded` returns to "false".

## 6. Acceptance criteria

A1. Every requirement R1–R13 is satisfied.
A2. Every manual test T1–T11 passes.
A3. The diff:
    - touches only `project-controls.ts` (and possibly its template
      and CSS within it);
    - introduces no new runtime dependencies;
    - introduces no NEW document-level event listeners (the existing
      `_outsideClickHandler` and `_scrollHandler` registrations are
      preserved unchanged — A3 prohibits additions, not retention);
    - introduces no `keydown` / `keyup` / `keypress` event
      listeners (those are S-003b's territory; absence is verifiable
      by diff inspection — a grep over the diff for those event
      names should return zero hits in this step).
A4. `npm run build` succeeds; existing lint produces no new findings
    on the file.
A5. The HLPS §4.3 mouse-path regression checklist (subset relevant
    to the project-controls menu open/close/click flows) passes.
A6. The previously-applied conflict resolution on
    `project-controls.ts` (audit action routing via `Router.go` in
    `_selectAction`) is preserved unchanged.

## 7. Verification environment

Per IS §6.1: Windows + Chromium + NVDA. NVDA is REQUIRED for T6
(accessible-name spot-check) and is recommended for T2/T3
(announcement of expanded/collapsed state). VoiceOver on macOS
Safari is an acceptable substitute if the primary environment is
unavailable.

## 8. Out of scope

- Any change to `page-monitor-requests` or `env-monitor` (S-002).
- Any keyboard behaviour beyond the focus indicator's existence
  (S-003b owns Enter/Space/arrow/Home/End/ESC/Tab handlers and
  focus management).
- Migration to `<vaadin-menu-bar>` or `<vaadin-context-menu>`
  (HLPS §3 constraint).
- Visual redesign beyond adding the focus indicator.
- Changes to the menu-action set (the 6 actions + conditional
  Delete remain unchanged).
- Removing the existing imperative DOM-building pattern (the menu
  remains body-appended overlay; only ARIA roles + names + focus
  indicator are added).

## 9. Adversarial Review

The diff is reviewed by an Adversarial code-review panel. Reviewers
must be briefed on accepted risks:

- The menu is body-appended overlay (HLPS §3 constraint preserves
  existing visual UX);
- Vaadin menu-bar / context-menu are out-of-bounds (HLPS §3);
- Keyboard interactions are explicitly NOT in this step (S-003b);
- Existing `_selectAction` event-dispatch and `Router.go`
  short-circuit must be preserved unchanged (HLPS §3 constraint).

## 10. Risk acknowledgements

| Risk | Mitigation |
|---|---|
| Adding `aria-haspopup` / `aria-controls` / `aria-expanded` to the trigger may interfere with the existing `<vaadin-button>` accessibility tree. | The trigger is a vaadin-button with an `aria-label` already; standard ARIA additions are documented as compatible. T1/T2/T3 manually verify announcement. |
| Body-appended overlay's `role="menu"` may not be programmatically associated with the trigger in some screen readers if the DOM relationship relies on `aria-controls` resolution across shadow boundaries. | T2 explicitly inspects `aria-controls` resolution; if a screen reader fails to announce the relationship, S-003a's adversarial review surfaces this and the spec may need to revise the wiring approach (e.g. `aria-labelledby` on the menu instead of `aria-controls` on the trigger). |
| Focus indicator may collide with the existing hover style when both are active simultaneously. | R7 explicitly requires distinct styling; T10 verifies mouse-hover behaviour is unchanged. |
| Dark-theme focus indicator contrast. | T9 explicitly tests dark theme; if contrast is inadequate, the indicator must be themed via CSS custom properties consistent with the existing theme tokens. |
| Bare `<div>` items carrying `role="menuitem"` produce non-standard accessible-name computation in some assistive technologies (the AT may include surrounding text from sibling `<div>`s). | The dropdown's items are constructed imperatively from a fixed structure (icon `<vaadin-icon>` + label `<span>`); R6 and R6a together require icon suppression and accessible-name parity with the visible label. T6 (with NVDA spot-check) is the empirical gate. If T6 reveals an AT-specific quirk, the implementer may need to switch the role-bearing element from `<div>` to `<button type="button">` (still satisfying R5) — that adjustment is delivery-level. |

---

## Review History

### R1 — DRAFT → REVISION

Three reviewers (clarity/falsifiability, APG conformance, HLPS
coverage) returned APPROVE_WITH_FIXES with 17 findings combined.

| Theme | Reviewers | Disposition | Resolution |
|---|---|---|---|
| Trigger's existing `aria-label` ("Project actions") accessible-name precedence with new ARIA additions must be preserved | R1-coverage F-01 (HIGH) | Accept | Added R3b requiring trigger accessible name to remain stable across open/closed states; T1 and T2 explicitly verify. |
| Vaadin icons inside menu items / trigger may leak into accessible names | R1-coverage F-02 (MED), R1-clarity (concur) | Accept | Added R6a requiring icons hidden from AT (e.g. `aria-hidden="true"`); T6 outcome check covers verification. |
| Open menu's own accessible name must be derived from the trigger | R1-coverage F-03 (MED) | Accept | Added R3a explicitly requiring the dropdown overlay to carry an accessible name connected to the trigger. T4 updated. |
| `aria-orientation="vertical"` missing — APG menu pattern requires it for vertical menus | R1-APG A-01 (MED) | Accept | R4 extended to require `aria-orientation="vertical"`; T4 updated. |
| "e.g." phrasing in R1/R2/R3 was vacuously satisfiable | R1-clarity F-01 (MED) | Accept | R1/R2/R3 reworded to require the capability; the "e.g." now appears only as informational illustration of the canonical mechanism. |
| R5/T5 role-placement vs accessible-name divergence could pass with a defect | R1-clarity F-02 (MED) | Accept | T5 strengthened to cross-reference T6 — role-bearing element and name-bearing element must be the same. |
| R9/T9 "distinguishable" was subjective | R1-clarity F-03 (MED) | Accept | R9 reworded to align with WCAG 2.4.7 language ("perceivable against the item's background"); explicitly notes the criterion does not impose a numeric ratio. |
| A3 prohibition of "no document-level event listeners" was ambiguous (could be read as forbidding existing ones) | R1-clarity F-05 (LOW) | Accept | A3 clarified — prohibits NEW document-level listeners; existing `_outsideClickHandler` / `_scrollHandler` are preserved. |
| A3 falsifiable verification of "no keyboard handlers" | R1-APG C-01 (LOW) | Accept | A3 expanded — explicitly states absence of `keydown`/`keyup`/`keypress` listeners is verifiable by diff grep. |
| Delete-item separator must remain CSS-only | R1-APG B-01 (LOW) | Accept | R5 extended — no non-menuitem element may interrupt the menuitem sequence; the existing CSS-only separator must not be migrated to a DOM element. |
| T7 tabindex caveat for programmatic focus | R1-clarity F-06, R1-coverage F-04 (LOW) | Accept | T7 extended with note explaining how to verify the focus indicator without prejudging S-003b's roving-tabindex strategy. |
| §1 "N items" promise was unverifiable for some screen readers | R1-APG C-02 (LOW) | Accept | §1 qualified — item count announcement is SR-dependent and verified via NVDA spot-check in T6. |
| Risk row for `<div>`-based ARIA accessible-name quirks | R1-clarity F-07 (LOW) | Accept | New risk row added cross-referencing R6/R6a/T6. |
| `aria-labelledby` symmetry test (R3 alternatives) | R1-clarity F-04 (LOW) | Defer to Delivery | T2 reworded to require resolution of *whichever mechanism* the implementer chose (cleaner than enumerating both in tests). |
| `:focus-visible` UA semantics across browsers | R1-coverage F-05 (LOW) | Defer to Delivery | Chromium is the verification baseline per IS §6.1; the requirement is satisfiable in baseline. |
| R9 numeric contrast threshold | R1-coverage F-06 (LOW) | Reject | Out of scope — HLPS §4.1 cites WCAG 2.4.7 only; demanding a numeric threshold is scope creep. |

After this revision, status returns to `IN REVIEW` for R2. R2
reviewers must verify R1 fixes, check for regressions, and (per
CLAUDE.local.md §4 Re-Review Scoping) NOT mine for new findings on
R1 text that was implicitly accepted.

### R2 — IN REVIEW → APPROVED

| Reviewer | Lens | Outcome |
|---|---|---|
| Reviewer A (Opus) | Clarity / falsifiability | **APPROVE** — All seven R1 findings verified resolved; no regressions detected. R3b stability checks integrate cleanly with R1/R2/R3; R6a icon-suppression coheres with R6 outcome; T7 tabindex caveat correctly delivery-level. |
| Reviewer B (Sonnet) | APG conformance | **APPROVE** — All five R1 findings verified resolved; no regressions. R3a / R3b additions internally consistent. T5 cross-reference to T6 strengthens falsifiability without contradiction. |
| Reviewer C (default) | HLPS coverage | **APPROVE** — All six R1 findings verified or correctly deferred/rejected. IS §6.2 S-003a allocations all covered; no orphaned HLPS bullets; no S-003b scope leakage. |

Unanimous approval. Status transitions to
`APPROVED — Pending user approval` per CLAUDE.local.md §2 Document
Status Lifecycle.
