# DECISION-U-2 — `active-item-changed` semantics & row-activation gate

| Field | Value |
|---|---|
| Status | APPROVED |
| Owner | Ben Hegarty |
| Governing IS | `docs/grid-menu-a11y/IS-grid-menu-a11y.md` (APPROVED) |
| Step | S-001 |
| `@vaadin/grid` version under investigation | **25.1.2** (resolved from `src/dorc-web/package-lock.json`) |
| Source citation anchor | npm package tarball published as `@vaadin/grid@25.1.2` (sha512 integrity in `package-lock.json`); paths cited below resolve against `node_modules/@vaadin/grid/src/...` for that version. |

---

## 1. Decision

**Path A — refine the existing event-based gate.**

Keep the `active-item-changed` subscription on both monitor grids and
remove the existing pointer-gating mechanism (it protects against a
phantom problem — see Q1, Q2). Retain the existing `null`-reset guard
(see Q2). Add a small, separate keyboard handler so that **Enter** on
a focused row opens the detail panel; **Space** already opens it via
the existing `active-item-changed` path (see Q1).

**Why Path A, traceable to Q-findings**:

- **Q1 + Q5**: pointer activation, Space activation on a focused row,
  and programmatic activation are all funneled through
  `active-item-changed`. Arrow / Tab / Home / End / Page-Up / Page-Down
  do **not** funnel through this event — they only move focus. There
  is no irreducibly mouse-coupled behaviour, and there is no need to
  filter out arrow drift on the listener — it never arrives there in
  the first place.
- **Q2**: the existing `null`-reset re-entry is already correctly
  handled by the early-return on `!request` in `onRowClick`. The
  separate `_pointerActive` flag adds nothing on top.
- **Q3**: alternative events exist (`row-activate`, `cell-activate`)
  but they overlap exactly with the keyboard / pointer cases that
  already drive `active-item-changed`, **except** for Enter, which is
  intercepted by the grid's "interaction mode" handler and never
  reaches any of these events. Path A solves the gap with a small
  Enter keydown handler rather than a wholesale rewrite.
- **Q4**: the grid already has a native row-focus model (rows are
  navigable; row-focus mode is supported), so S-002 does not need to
  bolt on focus management — it can rely on the grid's existing
  navigation and add only the Enter keypath.

Path B (replace the event subscription with `cell-activate` /
`row-activate` listeners) would work but offers no functional gain
over Path A and produces a larger diff. Path C (neither adequate) is
ruled out: every HLPS §4.2 criterion is reachable with Path A.

---

## 2. Investigation findings

### Q1 — Which user gestures cause `<vaadin-grid>` to emit `active-item-changed`?

**Answer**: pointer-driven cell click (any pointer type) and Space on
a focused row or non-content cell. **Not**: arrow keys, Tab, Home,
End, PageUp, PageDown, or Enter.

**Evidence (source)**:

- `vaadin-grid-active-item-mixin.js` (25.1.2) wires the active-item
  pipeline as: `ready()` binds a `click` listener on the grid's
  scroller element and listeners for the custom events `cell-activate`
  and `row-activate`. The `_activateItem` private method reads
  `e.detail.model.item` and toggles `activeItem` (sets to the clicked
  item if not equal, sets to `null` if equal). The `activeItem`
  property is declared with `notify: true`, which is the Polymer
  mechanism that produces the public `active-item-changed` event.
  *Citation: `node_modules/@vaadin/grid/src/vaadin-grid-active-item-mixin.js` lines 26–61.*
- The `click` listener path (`_onClick`) dispatches `cell-activate`
  for any pointer click on the scroller, after calling
  `_shouldPreventCellActivationOnClick` to skip clicks on focusable
  inner content, sorter cells, label elements, etc.
  *Citation: same file lines 68–109.*
- The keyboard pipeline lives in
  `vaadin-grid-keyboard-navigation-mixin.js`. The top-level keydown
  dispatcher classifies keys into groups (`Navigation`, `Interaction`,
  `Tab`, `Space`).
  *Citation: `node_modules/@vaadin/grid/src/vaadin-grid-keyboard-navigation-mixin.js` lines 234–273.*
- Only the `Space` group dispatches `row-activate` / `cell-activate`
  (in `_onSpaceKeyDown`, lines 733–747 of the same file). The
  `Navigation` group (`_onNavigationKeyDown`, line 296) only moves
  focus; it never dispatches an activate event. The `Interaction`
  group handles `Enter`, `Escape`, `F2` — but `Enter` is interpreted
  as "enter cell editing mode", not "activate the row" (lines
  599–634).

**Reproducibility note**: this is a source-level finding, not a
runnable observation; the assertion is therefore deterministic against
`@vaadin/grid@25.1.2`.

### Q2 — Does `grid.activeItem = null` re-emit `active-item-changed`, and how is the re-entry handled today?

**Answer**: yes, the assignment re-emits `active-item-changed` with
`e.detail.value === null`. The current code paths handle the re-entry
correctly via an early-return on a falsy detail value, *before* the
`activeItem = null` reset is issued (so the second invocation enters,
sees `null`, and exits without side effects).

**Evidence (source + behavioural)**:

- The `notify: true` Polymer flag on `activeItem`
  (`vaadin-grid-active-item-mixin.js` line 37) is documented to
  publish a property-change event for *every* change, including
  programmatic assignment. The "deactivate by re-interacting" comment
  on lines 31–33 confirms this is intentional — the same channel
  reports both user gestures and programmatic changes.
- The current row-click handlers in `env-monitor.ts` and
  `page-monitor-requests.ts` perform `if (!request) return;` against
  `e.detail.value` after dereferencing, before issuing
  `grid.activeItem = null`. The re-entry that the assignment
  produces therefore enters with a `null` detail value and exits at
  the early-return without dispatching the open-detail event a second
  time.

**Reproducibility note**: a runnable confirmation in the dev
environment (cold-start, click row, observe two `active-item-changed`
events: one with the row, one with `null`) is recommended for the
S-002 implementer but is not required to defend Path A — the
property-notify behaviour is part of Polymer's documented contract,
not a timing-dependent emergent behaviour.

### Q3 — Are there alternative grid events that more cleanly express "user activated this row"?

**Answer**: yes, two — `cell-activate` and `row-activate` — but they
are **not** cleaner than `active-item-changed` for our purposes. They
are dispatched by exactly the same gestures that drive
`active-item-changed` (pointer click on a non-content cell; Space on a
focused row or cell with no inner content) and are **not** dispatched
by Enter. Switching the listener to one of them would not
distinguish keyboard from pointer activation any better than the
existing wiring does, and would lose the convenient
`e.detail.value`-as-item shape that the existing handler relies on.

**Evidence (source)**:

- `cell-activate` is dispatched from `_onClick` on cells whose
  `_shouldPreventCellActivationOnClick` returns false, and from
  `_onSpaceKeyDown` when the focused element is a non-row cell.
  `row-activate` is dispatched from `_onSpaceKeyDown` only when the
  focused element is a row. *Citations: same files as Q1 — lines
  100–108 of active-item-mixin and lines 738–746 of
  keyboard-navigation-mixin.*
- Neither event has an Enter dispatch path. Enter is consumed by
  `_onInteractionKeyDown` (lines 599–634 of the keyboard-navigation
  mixin), which switches the grid into `interacting` mode rather than
  activating the row.

### Q4 — `<vaadin-grid>`'s native keyboard / row-focus model

**Answer**: the grid has a built-in keyboard navigation model
including row-focus mode (single-Tab into the grid lands on a row in
that mode), and arrow keys move a focus indicator across rows or
cells without activating them. Space is the documented row-activation
key; Enter is reserved for cell-edit "interacting" mode. Tab on a
non-edge focus target returns control to the browser (the grid's
internal `focusexit` mechanism). The model follows the
[W3C ARIA Authoring Practices grid pattern](https://www.w3.org/WAI/ARIA/apg/patterns/grid/)
that the source explicitly cites (line 304 of the keyboard-navigation
mixin).

**Implication for S-002**: row-level Enter activation is the only
gap. The grid's native model handles arrow navigation, Space
activation, and Tab exit without further intervention.

**Evidence**: the keyboard-navigation mixin's structure shown in the
Q1 citations; the `_focusedItemIndex`, `_itemsFocusable`,
`__rowFocusMode`, and `__updateItemsFocusable` machinery referenced
across `_onNavigationKeyDown` and the focus-target handling at lines
700–730.

**Q4 supplement — consumer-observable interaction-mode signal**:

The grid manages an internal flag that records whether the user is
currently inside a cell-internal control (an input or similar). This
flag is exposed to consumer code via attribute reflection — i.e. as
a DOM-attribute presence/absence on the grid element — even though
the underlying property is marked `@private` in the JSDoc:

> The `interacting` boolean property on the keyboard-navigation
> mixin is declared with `reflectToAttribute: true`.
> *Citation: `node_modules/@vaadin/grid/src/vaadin-grid-keyboard-navigation-mixin.js` lines 87–93.*

The attribute reflection means the grid element carries the
attribute when interacting-mode is active. Consumer-side code can
therefore observe interacting-mode without depending on the private
JS property — the DOM-attribute surface is a stable, public-facing
signal in Polymer/Lit-Element land. This is the observable that
makes Path A's row-level Enter-isolation requirement satisfiable
from the consumer level.

(How exactly S-002's spec uses this signal — attribute selector,
event-target inspection, or a combination — is not prescribed here;
that is the S-002 spec author's design decision.)

### Q5 — Are there any irreducibly mouse-coupled grid behaviours that would defeat both Path A and Path B?

**Answer**: no.

The only user-input bias detectable in the source is the pointer-only
`_onClick` listener, which is one of *several* dispatch sites for
`cell-activate`. Keyboard Space dispatches the same event. The
property-notify mechanism on `activeItem` is input-agnostic
(programmatic, pointer, keyboard all funnel through the same notify).
There is no behaviour where the grid emits an activation event
*conditionally on pointer source* and suppresses the keyboard
equivalent.

The Enter-key gap is real but is not "irreducibly mouse-coupled" — it
is "consumed by interaction mode" — and is closable by a small
keydown handler at the consumer level. This is what Path A specifies.

(Q5 is a negative claim; per SPEC-S-001 §3, the defence is therefore
source-level.)

---

## 3. S-002 implications for the chosen path

This section is a forward-pointer for the S-002 JIT spec author. It
states what the spec must address; it does **not** prescribe the
spec's wording or the implementation shape.

- **Existing pointer-gate mechanism is removable.** The current
  `_pointerActive`-style flag on both monitor grids was added to
  suppress arrow-key drift, but arrow keys never reach the
  active-item channel (Q1). Removing the flag is a behaviour-neutral
  simplification for keyboard users and unblocks Space activation on
  a focused row.
- **Existing null-reset guard is sufficient.** The early-return on a
  falsy detail value already handles the `activeItem = null` re-entry
  (Q2). S-002 does not need to add a second null-reset guard.
- **Enter activation is a separate, additive concern.** S-002's spec
  must specify how the consumer surfaces a row-level Enter handler
  that opens the detail panel for the focused row. This is the only
  new keyboard wiring required.
- **Cell-internal isolation requirement (pointer side already
  satisfied).** Pointer activation is already isolated from
  cell-internal controls by the grid's
  `_shouldPreventCellActivationOnClick` check (Q1, Q3); no consumer
  work is required there. The new Enter handler S-002 introduces
  must satisfy an equivalent requirement, expressed in
  consumer-observable terms: **Enter on a focused row must not open
  the detail panel when the grid is in interacting-mode** (i.e. when
  focus is inside a cell-internal control such as a filter input or
  sort button). The Q4 supplement above identifies the
  consumer-observable signal (the grid's `interacting` reflected
  attribute) that makes this requirement satisfiable; the precise
  mechanism is the S-002 spec author's choice.
- **Touch and pen activation work via the same `click` listener** as
  mouse — the `_onClick` registration uses standard click semantics,
  which browsers synthesise from touch and pen pointer events. No
  separate pointer-type handling is required for S-002 to satisfy the
  HLPS §4.2 "mouse, touch, or pen" criterion.
- **Listener lifecycle**: any new Enter keydown listener S-002 adds
  must follow HLPS §4.2's parity rule (added in `connectedCallback`,
  removed in `disconnectedCallback`, identically on both pages).
- **Parity scope**: S-002 must apply the same set of changes to both
  `page-monitor-requests` and `env-monitor` in a single commit per
  IS S-002.

S-002's JIT spec author is responsible for translating these
requirements into an implementation plan. None of the above is a
literal API surface for S-002 to inherit — the precise event names,
handler binding shapes, and registration sites are S-002's choices
within the constraints given.

---

## 4. Rejected alternatives

### Path B — replace `active-item-changed` with `cell-activate` / `row-activate` listeners

Plausible but not preferred. Path B would migrate both consumer
handlers from the property-notify event to the lower-level activation
events. The functional outcome is roughly equivalent (Q3): the same
gestures that fire `active-item-changed` also fire `cell-activate` /
`row-activate`. The migration would lose the convenient
`e.detail.value`-as-item shape (the activate events carry
`e.detail.model` instead) and would still leave the Enter gap
unresolved — Path A's small Enter handler would still be required.

Net cost: a larger diff with no functional benefit. Rejected.

### Path C — neither adequate

Ruled out by Q5. The grid does not have any behaviour that would
prevent a Path-A solution from satisfying every HLPS §4.2 criterion.

---

## Review History

### R1 — DRAFT → REVISION

Three reviewers (citation rigour, path soundness, S-002 readiness)
returned APPROVE / APPROVE_WITH_FIXES.

| Theme | Reviewers | Disposition | Resolution |
|---|---|---|---|
| Enter-isolation: cite consumer-observable signal AND state requirement clearly without prescribing the predicate | R1-path F-01 (HIGH), R1-readiness F-01 (MED) | Accept | Q4 extended with a supplement that identifies the grid's `interacting` property as `reflectToAttribute: true` (citation: `vaadin-grid-keyboard-navigation-mixin.js` lines 87–93), making interacting-mode a panel-verifiable, consumer-observable DOM signal. §3 cell-internal isolation bullet rewritten to express the requirement in consumer-observable terms ("Enter must not fire when the grid is in interacting-mode") rather than asking S-002 to "mirror" a private grid predicate. |
| Q1 line offset (240–273 should start at 234) | R1-citation F-01 (LOW) | Accept | Updated to 234–273. |
| ARIA URL form (apg/patterns/grid vs aria-practices) | R1-citation F-02 (LOW) | No action | Same content; both URLs resolve to the W3C ARIA APG grid pattern. |
| Q2 reproducibility / responsibility | R1-path F-02 (LOW), R1-readiness F-02 (LOW) | Defer to Delivery | Per SPEC-S-001 §3 non-determinism rule the source-level claim is sufficient; S-002's spec/test plan can decide whether to add a runtime confirmation. |
| Hallucination check | R1-readiness F-03 | No action | Confirmation, not a finding. |

After this revision, status returns to `IN REVIEW` for R2. R2 reviewers
must verify R1 fixes, check for regressions in unchanged text, and
(per CLAUDE.local.md §4 Re-Review Scoping) NOT mine for new findings
on R1 text that was implicitly accepted.

### R2 — IN REVIEW → APPROVED

| Reviewer | Lens | Outcome |
|---|---|---|
| Reviewer A (Opus) | Citation rigour | **APPROVE** — Q1 line offset fix verified; new Q4 citation (`interacting` property + `reflectToAttribute: true` at lines 87–93) verified exactly against source; §3 reword introduces no contradictions. |
| Reviewer B (Sonnet) | Path soundness | **APPROVE** — F-01 gap closed; HLPS §4.2 coverage re-audited bullet-by-bullet; all criteria addressable under Path A. |
| Reviewer C (default) | S-002 readiness / abstraction | **APPROVE** — F-01 reword holds; AC #6 boundary preserved (no consumer-side identifiers introduced). |

Unanimous approval. Status transitions to `APPROVED` per
CLAUDE.local.md §2 Document Status Lifecycle. S-001 is complete; the
S-002 JIT spec may now be authored citing this APPROVED note.
