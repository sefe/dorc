# SPEC-S-003b — Project-controls menu: keyboard operability + focus management + deleteHidden wrap

| Field | Value |
|---|---|
| Status | APPROVED — Pending user approval |
| Owner | Ben Hegarty |
| Governing IS | `docs/grid-menu-a11y/IS-grid-menu-a11y.md` (APPROVED) |
| Step | S-003b |
| Target branch | `claude/github-azure-devops-interop-vhDvn` (PR #584) |
| Files in scope | `src/dorc-web/src/components/grid-button-groups/project-controls.ts` |
| Depends on | S-003a (the ARIA scaffolding + focus indicator established by S-003a are referenced by the keyboard handlers in S-003b). |

---

## 1. Purpose

Make the project-controls overflow menu fully keyboard-operable. After
this step, a keyboard-only user can:

- open the menu from the trigger with Enter or Space,
- navigate the items with Up/Down (with wrap), Home/End,
- invoke an item with Enter or Space,
- close the menu with ESC (focus restored to trigger),
- exit the menu with Tab/Shift+Tab (focus moves to next/previous in
  the trigger's tab order),

with correct behaviour when the Delete action is hidden
(`deleteHidden=true`), and with all existing custom-event /
`Router.go` dispatch preserved.

S-003a established the ARIA scaffolding and focus indicator that
S-003b's interactions exercise; per IS §3, S-003a → S-003b is a soft
ordering preference and S-003a's commit must precede this one.

## 2. Pre-execution self-audit

Before writing code:

- [ ] HLPS-grid-menu-a11y status = APPROVED (user-approved)
- [ ] IS-grid-menu-a11y status = APPROVED (user-approved)
- [ ] SPEC-S-003a status = APPROVED (user-approved or auto-pilot)
      AND its commit has landed on the target branch
- [ ] This SPEC status = APPROVED (user-approved or auto-pilot)
- [ ] No in-flight adversarial reviews on grid-menu-a11y artifacts
- [ ] Working tree on `claude/github-azure-devops-interop-vhDvn`,
      no uncommitted tracked changes that would interleave with
      S-003b

If any item is unchecked, **STOP** and address it.

## 3. Branch and commit strategy

- All work lands on the existing PR branch.
- A **single commit** introduces all S-003b changes.
- Commit ordering: S-003b's commit must land after S-003a's per
  IS §4.2 monotonic ordering rule.
- Commit message style follows existing PR commits.

## 4. Behavioural requirements

The following describes what the changed code must do, in
consumer-observable terms. Identifiers, exact key-handling
mechanisms, exact focus-management primitives, etc., are the
delivery agent's choice within these constraints.

### 4.1 Opening the menu from the trigger

R1. With keyboard focus on the trigger, pressing **Enter** opens the
    menu and moves keyboard focus to the first menu item.
R2. With keyboard focus on the trigger, pressing **Space** opens the
    menu and moves keyboard focus to the first menu item.
R3. The trigger's `aria-expanded` (per S-003a R2) updates correctly
    when keyboard interaction opens the menu, and updates back to
    `"false"` (or equivalent collapsed state) whenever the menu
    closes — regardless of which mechanism caused the close (ESC
    per R10, Tab/Shift+Tab per R11/R12, item invocation per R8/R9,
    or outside-click per the existing `_outsideClickHandler`). The
    state must always reflect the actual open/closed state of the
    dropdown.

### 4.2 Navigating items with the keyboard

R4. With focus on a menu item, pressing **ArrowDown** moves focus to
    the next item. Wraps from the last item to the first.
R5. With focus on a menu item, pressing **ArrowUp** moves focus to
    the previous item. Wraps from the first item to the last.
R6. Pressing **Home** moves focus to the first item; pressing
    **End** moves focus to the last item. Both must work from any
    starting item.
R7. The currently-focused item shows the visible focus indicator
    established by S-003a. Hover and focus styling do not collide
    (i.e. moving focus via keyboard does not erase existing hover
    styling on a different item, and vice versa).

### 4.3 Invoking an item

R8. Pressing **Enter** on a focused item invokes the item's action,
    producing the same outcome as a mouse click on that item.
    Specifically: the existing `_selectAction` path runs, the
    appropriate custom event is dispatched (or `Router.go` is
    called for the audit action), and the menu closes.
R9. Pressing **Space** on a focused item produces the same outcome
    as Enter (R8).
R9a. The keyboard-invoke path (R8/R9) must produce **exactly the
    same** dispatch behaviour as the mouse-click path: the same
    single custom event with identical detail payload (or the same
    single `Router.go` call for the audit action). It must NOT
    additionally dispatch a synthetic click, fire any other custom
    event, or otherwise diverge in observable behaviour from the
    mouse path. T8 / T14 verify mouse-vs-keyboard event-stream
    parity.
R9b. After invoking an item with Enter or Space, focus must not be
    silently left on the menu's body-appended overlay (which is
    being removed) or on `<body>`. The action that R8/R9 invokes
    typically destination-focuses (e.g. opens a dialog or routes to
    a new page); but if the action does NOT relocate focus,
    keyboard focus should be restored to the trigger (matching the
    ESC close-and-restore semantics of R10).

### 4.4 Closing the menu

R10. Pressing **ESC** with the menu open closes the menu and
     restores keyboard focus to the trigger.
R11. Pressing **Tab** with the menu open closes the menu and moves
     focus to the element that would naturally follow the trigger
     in document tab order (i.e. as if focus had been on the
     trigger and Tab had been pressed there). The destination must
     be **strictly different from the trigger** when a forward
     focusable sibling exists in the document; the trigger is an
     acceptable fallback only when no such sibling exists. Focus
     must NOT be left on the `<body>` element or in the
     body-appended overlay's DOM position.
R12. Pressing **Shift+Tab** with the menu open closes the menu and
     moves focus to the element that would naturally precede the
     trigger in document tab order. Symmetric to R11: the
     destination must be strictly different from the trigger when a
     backward focusable sibling exists; the trigger is an
     acceptable fallback only when no such sibling exists.

### 4.5 deleteHidden wrap behaviour

R13. When `deleteHidden=true`, the visible item set comprises the
    six always-visible actions (Edit Metadata, Project Access,
    Environments, Components, Reference Data, Audit) and the Delete
    item is excluded entirely from the focus cycle. ArrowDown /
    ArrowUp wrap and Home / End must operate over those six visible
    items only — pressing End from any other item must land on the
    last visible item (Audit), not on a hidden Delete item. When
    `deleteHidden=false`, the visible set is the same six plus
    Delete, and End lands on Delete; both cases are precisely
    enumerated to remove off-by-one ambiguity.

### 4.6 Preservation of existing dispatch

R14. The existing custom-event dispatch from `_selectAction` for
    the six always-visible actions (`open-project-metadata`,
    `open-access-control`, `open-project-envs`,
    `open-project-components`, `open-project-ref-data`,
    `delete-project`) and the `Router.go` short-circuit for
    `open-project-audit-data` must be preserved unchanged with
    identical detail payloads. The keyboard invoke path (R8/R9)
    routes through the same dispatch as the mouse-click path.
R15. The mouse-click invoke path (clicking an item with the mouse)
    must continue to work unchanged. R8/R9 add a keyboard path
    *alongside* the mouse path; they do not replace it.

### 4.7 Out-of-scope changes that must not happen

R16. S-003b must NOT modify the ARIA roles, accessible names, or
    focus indicator established by S-003a. If a S-003a property
    needs adjustment, that is a S-003a revision under the standard
    process.
R17. S-003b must NOT migrate to `<vaadin-menu-bar>` or
    `<vaadin-context-menu>` (HLPS §3 constraint).
R18. S-003b must NOT introduce keyboard handlers that fire when the
    menu is closed. All keyboard handling for menu navigation
    activates only while the menu is open; the trigger's own Enter/
    Space handling for opening (R1, R2) is the sole exception, and
    is scoped to the trigger element.

## 5. Test-first approach

Manual tests against `npm run dev`. NVDA spot-check is recommended
for items that involve announcement (R3, R10) but not strictly
required for items that test focus location.

### T1. Trigger opens with Enter

Tab to the trigger. Press Enter. Confirm:
- the menu opens,
- keyboard focus is on the first menu item (visible focus
  indicator from S-003a R7),
- the trigger's `aria-expanded` reads "true".

### T2. Trigger opens with Space

Close the menu (e.g. via ESC). Tab to the trigger. Press Space.
Confirm same outcome as T1.

### T3. ArrowDown navigation with wrap

After T1 (focus on first item), press ArrowDown repeatedly. Confirm
focus advances through every item and wraps from the last item to
the first.

### T4. ArrowUp navigation with wrap

Press ArrowUp from any item. Confirm focus moves to the previous
item, wrapping from first to last.

### T5. Home / End

From any item, press End — focus lands on the last visible item.
Press Home — focus lands on the first item.

### T6. Enter invokes (event-dispatch path)

Open the menu. Navigate to a non-audit, non-delete item (e.g. "Edit
Metadata"). Press Enter. Confirm:
- the corresponding custom event fires (verify in DevTools console
  with `monitorEvents(document.body, 'open-project-metadata')` or
  via the existing parent listener),
- the menu closes,
- focus is restored to the trigger (per R10's spirit — the menu's
  invocation completion is also a close-and-restore moment;
  practically the trigger may already lose focus depending on the
  outer dialog the event opens, but verify focus is not left on
  `<body>`).

### T7. Space invokes

Repeat T6 with Space. Same outcome.

### T8. Enter invokes audit action via Router.go (event-stream parity)

Open the menu. Navigate to the Audit item. Before pressing Enter,
attach a transient observer in the DevTools console:
`monitorEvents(document.body, 'open-project-audit-data')` (or use
an explicit listener that logs occurrences). Press Enter. Confirm:
- `Router.go('/projects/audit?projectId=…')` runs (the audit page
  loads),
- the previously-applied conflict resolution (audit short-circuit)
  continues to function — i.e. NO `open-project-audit-data` custom
  event fires alongside the navigation. The keyboard path must
  produce exactly the same single dispatch as the mouse path,
  per R9a.

### T9. ESC closes and restores focus

Open the menu. Navigate to any item. Press ESC. Confirm:
- the menu closes,
- keyboard focus is restored to the trigger,
- the trigger's `aria-expanded` reads "false".

### T10. Tab closes and moves focus forward

Open the menu (focus on first item). Press Tab. Confirm:
- the menu closes,
- focus has moved to the element that would naturally follow the
  trigger in document tab order (NOT to `<body>`, NOT to the
  body-appended overlay's DOM position),
- focus is **not** the trigger itself — when a forward focusable
  sibling exists in the document (which is the typical case in a
  project list with a list of action triggers across rows), R11
  requires destination to advance past the trigger. The trigger as
  destination is acceptable only when no forward sibling exists at
  all (rare).

### T11. Shift+Tab closes and moves focus backward

Open the menu. Press Shift+Tab. Confirm:
- the menu closes,
- focus has moved to the element that would naturally precede the
  trigger in document tab order,
- focus is **not** the trigger itself when a backward focusable
  sibling exists (symmetric to T10).

### T12. deleteHidden wrap behaviour

In a context where `deleteHidden=true` (non-admin user), open the
menu. Confirm:
- the menu has only the 6 always-visible items,
- ArrowDown from the last visible item wraps to the first,
- ArrowUp from the first item wraps to the last (visible) item,
- End lands on the last visible item, NOT on a hidden Delete.

### T13. deleteHidden wrap behaviour with delete visible

In an admin context, open the menu. Confirm Delete is the last
item, and End / wrap behaviour includes it.

### T14. Mouse-click invoke unchanged

With the menu open, click each item with the mouse. Confirm
existing event/route dispatch behaviour is unchanged. Repeat for
the audit action and Delete (admin context).

### T15. Outside-click close unchanged

With the menu open via keyboard (T1), click outside the menu and
trigger with the mouse. Confirm the menu closes (existing
behaviour); focus restoration may differ from ESC (mouse close
typically does not restore focus to a trigger), and that is
acceptable provided the menu closes cleanly.

### T16. Menu closes do not re-open spuriously

Open the menu, close it (ESC). Press Enter while focus is on the
trigger. Confirm the menu re-opens (R1) and re-closes correctly,
i.e. the closing logic does not leave residual document-level
listeners that interfere with subsequent opens.

### T17. S-003a wiring untouched

Inspect the trigger and dropdown ARIA properties (the same
properties verified in S-003a's T1 / T2 / T4 / T5). Confirm none
have been altered or removed by S-003b — `aria-haspopup`,
`aria-expanded`, the trigger ↔ menu association, `role="menu"`,
`aria-orientation="vertical"`, `role="menuitem"`, the focus
indicator CSS, and the `aria-hidden` icon suppression all remain
in place exactly as S-003a established them. R16 enforces this;
T17 makes it diff-auditable.

## 6. Acceptance criteria

A1. Every requirement R1–R18 is satisfied.
A2. Every manual test T1–T17 passes.
A3. The diff:
    - touches only `project-controls.ts` (and its template / CSS
      within);
    - does NOT modify the ARIA roles or focus-indicator wiring
      established by S-003a;
    - introduces no new runtime dependencies.
A4. `npm run build` succeeds; existing lint produces no new
    findings on the file.
A5. The HLPS §4.3 mouse-path regression checklist for the menu
    (open via click, click outside, scroll while open, click
    items) passes.
A6. The previously-applied conflict resolution on
    `project-controls.ts` (audit action via `Router.go` in
    `_selectAction`) is preserved unchanged. T8 explicitly
    verifies.
A7. No new document-level keyboard listener that fires while the
    menu is closed (per R18). Existing document-level click and
    scroll listeners (used by outside-click and scroll-to-close)
    may remain or be refactored, but their lifecycle and
    behaviour must not change in a way that affects users.

## 7. Verification environment

Per IS §6.1: Windows + Chromium + NVDA. NVDA is recommended for
T1/T2/T9 (announcement of menu open/close + trigger expanded
state). Tests must be performed in both light and dark theme to
ensure the focus indicator from S-003a remains operative under both.

## 8. Out of scope

- Any change to `page-monitor-requests` or `env-monitor` (S-002).
- Any change to ARIA scaffolding or accessible-name wiring
  (S-003a).
- Migration to `<vaadin-menu-bar>` or `<vaadin-context-menu>`
  (HLPS §3 constraint).
- Visual presentation changes beyond what is required to honour
  S-003a's focus indicator (which is itself S-003a's responsibility,
  not S-003b's).
- Changes to the menu-action set or the `_selectAction` dispatch
  shape.
- Implementing a typeahead / first-letter shortcut. The HLPS §4.1
  success criteria do not require it; if the delivery agent
  considers it a small natural addition, it remains out of scope
  for S-003b — a separate IS revision would govern adding it.

## 9. Adversarial Review

The diff is reviewed by an Adversarial code-review panel. Reviewers
must be briefed on accepted risks:

- Body-appended overlay is preserved (HLPS §3);
- Vaadin menu-bar / context-menu out-of-bounds (HLPS §3);
- S-003a wiring is fixed; S-003b only adds keyboard handling and
  focus management;
- Existing `_selectAction` event-dispatch and `Router.go`
  short-circuit must be preserved unchanged (HLPS §3 constraint);
- The `prefers-reduced-motion` consideration deferred per HLPS
  R1-a11y F-05 only applies if S-003b introduces transitions;
  if the delivery agent introduces any transitions, those must
  respect `prefers-reduced-motion`.

## 10. Risk acknowledgements

| Risk | Mitigation |
|---|---|
| Tab/Shift+Tab close-and-move-focus (R11/R12) is the most APG-conformant pattern but is non-trivial to implement correctly when the menu is in a body-appended overlay (focus must be moved to a DOM position outside the overlay, computed from the trigger's tab order). | The implementation may use a sentinel-element technique (move focus to the trigger before letting the browser's default Tab handling proceed) or compute the next focusable element manually. T10/T11 explicitly verify the user-visible outcome regardless of mechanism. |
| Focus restoration on close (R10) requires keeping a reference to the trigger element while the menu is open. If the trigger is removed from the DOM (e.g. by the parent re-rendering the grid row) before the menu closes, focus restoration becomes a no-op. | Acceptable degradation — the user has effectively navigated away from the row, so leaving focus where it lands is reasonable. Document the case in the commit message rather than complicating the close logic. |
| Keydown handlers attached at document level while the menu is open could interact with S-002's host-level Enter handler (per IS §4.2 cross-step coupling). | S-003b's keydown handlers are scoped to the menu-open window: registered when the menu opens, removed when it closes. They will not interfere with S-002 outside that window. While the menu is open, no project-controls instance is on a monitor grid, so the cross-coupling is bounded to environments where both surfaces could simultaneously be present. |
| Multiple project-controls instances on the same page (one per row in the grid) each install document-level listeners while their menu is open. If a user opens menu A, then quickly clicks menu B's trigger before menu A's listeners are removed, a brief race could leave both menus open or leave stale listeners. | The existing imperative dropdown machinery already enforces a single-menu invariant: opening a new menu causes any other open menu to close (the existing `_outsideClickHandler` plus the per-instance `_uid` removal pattern). S-003b's keyboard-listener registration must run through the same single-menu close path — opening menu B while menu A is open must close A (and tear down A's keydown listener) before B's keydown listener is installed. The mitigation pattern: register keydown on open, remove on close, scoped per-instance via `_uid`, AND removed in `disconnectedCallback` for safety. |

---

## Review History

### R1 — DRAFT → REVISION

Three reviewers (clarity/falsifiability, APG conformance, HLPS
coverage) returned APPROVE_WITH_FIXES with 8 substantive findings
combined. All MEDIUM; none CRITICAL or HIGH.

| Theme | Reviewers | Disposition | Resolution |
|---|---|---|---|
| R3 covered `aria-expanded` open but not close — needed backing requirement | R1-APG F-01 (MED) | Accept | R3 extended to require `aria-expanded` updates correctly on every close mechanism (ESC, Tab/Shift+Tab, item invocation, outside-click). |
| R8/R9 silent on focus restoration after keyboard invoke | R1-clarity F-01 (MED) | Accept | Added R9b — keyboard invoke must not leave focus on overlay or `<body>`; if the invoked action does not relocate focus, it must be restored to the trigger (matching ESC semantics in R10). |
| R8/R9 silent on event-stream parity vs mouse path (risk: synthetic click + custom event firing simultaneously) | R1-coverage F-01 (MED) | Accept | Added R9a — keyboard invoke must produce exactly the same single dispatch as mouse click; T8 strengthened with `monitorEvents` observer to verify no spurious `open-project-audit-data` event during Router.go path. |
| R11/R12 didn't require destination ≠ trigger when siblings exist | R1-clarity F-02 (MED) | Accept | R11/R12 reworded to require strictly different destination from trigger when forward/backward sibling exists; trigger is fallback only when no sibling exists. T10/T11 strengthened correspondingly. |
| R13 didn't enumerate the visible set when `deleteHidden=true` | R1-APG F-04 (MED) | Accept | R13 reworded to explicitly enumerate the six visible items in the `deleteHidden=true` case and the seven in the `false` case, removing off-by-one ambiguity. |
| Risk row 3 mitigation underspecified — should bind into existing single-menu close path | R1-coverage F-02 (MED) | Accept | Risk row 3 reworded to require S-003b's keydown registration to run through the same single-menu close path as the existing click/scroll listeners; the `_uid` invariant is reused, with `disconnectedCallback` cleanup as the safety net. |
| R16 (no S-003a wiring change) had no positive verification test | R1-clarity orphan (LOW) | Accept | New T17 added — diff-auditable check that S-003a's ARIA, focus indicator, and icon suppression remain unchanged. |
| R7 hover-vs-focus collision mechanism unspecified | R1-clarity F-03 (LOW) | Defer to Delivery | The outcome requirement (no collision) is correctly delivery-level; the implementer chooses the mechanism. |
| Per-instance scoping cleanup in `disconnectedCallback` mechanism | R1-clarity F-04 (LOW) | Defer to Delivery | §10 row 4 already binds the implementer to the per-instance scoping pattern; mechanism is delivery-level. |

After this revision, status returns to `IN REVIEW` for R2. R2
reviewers must verify R1 fixes, check for regressions, and (per
CLAUDE.local.md §4 Re-Review Scoping) NOT mine for new findings on
R1 text that was implicitly accepted.

### R2 — IN REVIEW → APPROVED

| Reviewer | Lens | Outcome |
|---|---|---|
| Reviewer A (Opus) | Clarity / falsifiability | **APPROVE** — All R1 findings (F-01..F-04, orphan T17) verified resolved; no regressions. Spotted minor stale "T1–T16" wording in A2; corrected during R2 to "T1–T17". |
| Reviewer B (Sonnet) | APG conformance | **APPROVE** — Both MED findings (F-01 aria-expanded close, F-04 R13 enumeration) verified watertight; APG keyboard-interaction re-audit clean. |
| Reviewer C (default) | HLPS coverage / cross-step coupling | **APPROVE** — Both MED findings verified; risk row 3 adequately binds S-003b into the existing `_uid` single-menu invariant; forward/reverse traceability for R9a/R9b preserved. |

Unanimous approval. Status transitions to
`APPROVED — Pending user approval` per CLAUDE.local.md §2 Document
Status Lifecycle.
