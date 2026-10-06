# IS — Grid-row & Action-menu Accessibility

| Field | Value |
|---|---|
| Status | APPROVED — Pending user approval |
| Owner | Ben Hegarty |
| Governing HLPS | `docs/grid-menu-a11y/HLPS-grid-menu-a11y.md` (APPROVED) |
| Target branch | `claude/github-azure-devops-interop-vhDvn` (PR #584) |

---

## 1. Strategy

The HLPS defines two largely-independent surfaces — the project-controls
overflow menu, and the grid row-activation gate on two pages. The IS
sequences a brief discovery step (to resolve HLPS U-2) before the
row-activation work, then orders the menu work into two atomic
deliveries along an ARIA-vs-interaction seam.

Step relationships:

- S-001 (decision note) → S-002 (row activation) is a hard dependency.
- S-003a (menu ARIA + accessible name + focus-visible) → S-003b (menu
  keyboard + focus management + deleteHidden wrap) is a soft ordering
  preference, not a hard dependency: each step delivers a real,
  shippable user-facing value on its own (S-003a gives screen-reader
  users semantics they did not have before; S-003b gives keyboard-only
  users operability). They are sequenced a-then-b because the ARIA
  scaffolding establishes the anchor points (item identity, expanded
  state) that the keyboard handlers reference.
- S-002 and S-003a/b are independent of each other except for the
  cross-step coupling acknowledged in §4.

All commits land on `claude/github-azure-devops-interop-vhDvn`, in line
with the user's preference (per stored feedback) for keeping related
Copilot follow-ups on a single PR rather than fanning out to per-step
branches.

## 2. Steps

### S-001 — Resolve HLPS U-2 (active-item-changed semantics)

**What changes**
A short investigation, captured as a decision note committed to the
repository at `docs/grid-menu-a11y/DECISION-U-2.md`, into how
`<vaadin-grid>` emits `active-item-changed`: which user gestures and
internal state changes trigger it, whether the existing
`grid.activeItem = null` reset re-emits the event, and which mechanism
is most appropriate for distinguishing arrow-key navigation from
intent-to-open. The output is a written conclusion that selects one of
three design paths for S-002:

- **Path A**: refine the existing event-based gate with a stronger
  signal that distinguishes arrow drift from explicit activation.
- **Path B**: replace the event-based gate entirely with a different
  primitive (e.g. row-level keyboard handling, or a `keydown` listener
  scoped to the grid).
- **Path C ("neither adequate")**: the investigation determines that
  neither A nor B alone can satisfy HLPS §4.2. The note documents the
  gap, names what is missing (e.g. an irreducibly mouse-coupled
  Vaadin behaviour), and triggers an IS revision before S-002's JIT
  spec is submitted.

**Why**
HLPS U-2 must be resolved before the JIT spec for the row-activation
step (S-002) is APPROVED. The chosen path materially changes what
S-002's spec describes.

**Dependencies**
None.

**Deliverable**
`docs/grid-menu-a11y/DECISION-U-2.md` — a standalone reviewable
artifact carrying its own document status field
(`DRAFT → IN REVIEW → APPROVED`) per CLAUDE.local.md §2 Document
Status Lifecycle. The note is committed in its own commit, separate
from S-002's spec or code.

**Verification intent**
- The decision note cites the specific Vaadin behaviours observed
  (with citations to API docs or to runnable evidence), names the
  chosen path (A, B, or C), and explains why the other paths were
  rejected.
- The note is submitted to the Adversarial panel as a **first-class,
  standalone review submission** — independently of S-002's JIT spec
  submission. The S-002 JIT spec must cite the APPROVED decision note
  but cannot bundle the note's review with its own.
- If Path C is selected, the note's APPROVAL by the panel includes
  acknowledgement of the IS-revision requirement; S-002's JIT spec is
  blocked until the IS is revised and re-approved.
- The S-002 JIT-spec pre-execution self-audit must include
  "DECISION-U-2.md status = APPROVED" as an explicit gate item.

**Out of scope**
Any code change. S-001 produces only the decision note.

---

### S-002 — Grid row keyboard/pointer activation, parity across both pages

**What changes**
The row-activation behaviour on `page-monitor-requests` and `env-monitor`
is reworked, in a single commit, so that:

- pointer activation works for mouse, touch, and pen (not mouse-only);
- row keyboard focus and Enter/Space activation work, while arrow-key
  navigation between rows continues NOT to open the detail panel
  (including the `activeItem = null` reset re-entry case);
- focus inside cell-internal controls (column-header filter inputs,
  sort buttons) does NOT cause row activation;
- any new event listeners are added in `connectedCallback` and
  symmetrically removed in `disconnectedCallback`, identically on both
  pages (HLPS §4.2 listener-lifecycle parity bullet);
- the two pages remain at strict parity per HLPS §4.2 (same accepted
  pointer types, same activation keys, same arrow suppression, same
  listener lifecycle).

The mechanism (Path A or Path B) is selected in S-001 and described in
the JIT spec for S-002. If S-001 selects Path C, this step is blocked
pending IS revision.

**Why**
HLPS §4.2 success criteria. Resolves the four originally-resolved
Copilot threads on `_pointerActive`/`@mousedown` plus the unresolved
thread referenced in HLPS §1.

**Dependencies**
S-001 (decision note must exist and be cited in the S-002 spec).

**Verification intent**
- Manual verification of every §4.2 success criterion, plus the
  positive arrow-drift sequence in §5, on both pages.
- HLPS §4.3 mouse-path regression checklist (the items relevant to row
  click and detail panel) confirmed.
- `npm run build` and lint pass.

**Out of scope**
- Any change to `project-controls.ts` (covered by S-003).
- Any change to other pages or grids.
- Any visual change to row hover, selection, or detail panel layout.

---

### S-003a — Project-controls menu: ARIA semantics and focus indicator

**What changes**
The custom dropdown in `project-controls.ts` is reworked, in a single
commit, to give it correct screen-reader semantics and a visible focus
indicator. Specifically:

- ARIA roles on the open menu and items (`menu`, `menuitem`);
- programmatic association between trigger and open menu (e.g.
  `aria-haspopup`, `aria-controls` / `aria-labelledby`, expanded /
  collapsed state on the trigger);
- accessible names on items equal to their visible labels (HLPS §3
  constraint);
- a visible focus indicator on the currently-focused menu item,
  distinct from the existing hover style and meeting WCAG 2.4.7
  (focus-visible CSS).

Visual presentation (ellipsis trigger, dropdown position, hover styling,
delete-action separator) is preserved per HLPS §3.

**Why**
HLPS §4.1 ARIA / accessible-name / focus-visible criteria + §3
accessible-name constraint. Delivers screen-reader-perceivable menu
semantics on its own — a discrete user-facing improvement (assistive
technologies will announce "menu, N items" and item names where
previously they announced unstructured `<div>`s).

**Dependencies**
None (independent of S-001, S-002, and S-003b).

**Verification intent**
- NVDA spot-check confirming menu open/close, item-name announcement,
  and the trigger's expanded/collapsed state are conveyed.
- Visual confirmation that the focused menu item shows a distinct,
  WCAG-compliant focus indicator under both light and dark theme.
- HLPS §4.3 regression checklist for menu-related mouse paths
  (existing event/route dispatch unchanged).
- `npm run build` and lint pass.

**Out of scope**
- Keyboard interaction set, focus management, and `deleteHidden` wrap
  logic (covered by S-003b).
- Any change to `page-monitor-requests` or `env-monitor` (S-002).
- Migration to `<vaadin-menu-bar>` or `<vaadin-context-menu>` (HLPS §3
  constraint).
- Changes to visual presentation beyond the new focus indicator.

---

### S-003b — Project-controls menu: keyboard operability and focus management

**What changes**
Building on the ARIA/focus-indicator scaffolding established in S-003a,
the menu is made fully keyboard-operable in a single commit:

- keyboard interaction set: Enter/Space on the trigger to open;
  Up/Down to move between items with wrap; Home/End to jump to
  first/last; Enter/Space on a focused item to invoke; ESC to close
  and restore focus to the trigger; Tab/Shift+Tab to close and move
  focus to the element naturally next/previous in the **trigger's**
  tab order;
- focus management: focus moves into the menu on open and is restored
  to the trigger on close (where applicable per the criterion above);
- correct wrap behaviour when `deleteHidden` causes the Delete item to
  be omitted (arrow-key wrap and Home/End operate over visible items
  only);
- preservation of all existing custom-event dispatch and the
  `Router.go` audit short-circuit (HLPS §3 constraint), with payloads
  unchanged.

**Why**
HLPS §4.1 keyboard / focus-management / `deleteHidden` criteria.
Delivers full keyboard operability on top of S-003a's semantics —
together they fully satisfy HLPS §4.1.

**Dependencies**
S-003a (the ARIA roles/state advertised by S-003a are referenced by
S-003b's keyboard handlers). Independent of S-001 and S-002.

**Verification intent**
- Manual keyboard walk-through covering every keyboard / focus-management
  bullet of HLPS §4.1, in document order, using only Tab / Shift+Tab /
  arrow keys / Home / End / Enter / Space / ESC.
- Both `deleteHidden` states exercised (admin and non-admin).
- NVDA spot-check confirming the announcement updates correctly as
  focus moves between menu items via keyboard.
- HLPS §4.3 regression checklist for menu-related mouse paths.
- `npm run build` and lint pass.

**Out of scope**
- Any change to ARIA scaffolding, accessible names, or focus-visible
  styling (covered by S-003a).
- Any change to `page-monitor-requests` or `env-monitor` (S-002).
- Migration to `<vaadin-menu-bar>` or `<vaadin-context-menu>` (HLPS §3
  constraint).

---

## 3. Sequencing rationale

```
S-001 (U-2 decision note, standalone APPROVED gate)
   │
   ▼
S-002 (row activation, both pages, single commit)

S-003a (menu ARIA + accessible name + focus-visible)
   │
   ▼
S-003b (menu keyboard + focus management + deleteHidden wrap)
```

- **S-001 → S-002**: hard dependency. S-002's JIT spec cannot be
  approved while DECISION-U-2.md is not APPROVED.
- **S-003a → S-003b**: soft ordering. The keyboard handlers in S-003b
  reference the ARIA structure established in S-003a; reversing the
  order is technically possible but would force S-003b to either
  duplicate ARIA scaffolding work or to ship temporarily-inconsistent
  semantics.
- **S-002 vs S-003a/b**: independent at the IS level except for the
  cross-step coupling acknowledged in §4 (shared document-level event
  surfaces, body-appended overlay focus, branch commit ordering).
  Either may be delivered first.

## 4. Risks acknowledged at IS level

### 4.1 Step-level risks

| Risk | Mitigation |
|---|---|
| S-001 may discover that neither Path A nor Path B fully satisfies HLPS §4.2 (e.g. some grid behaviour is irreducibly mouse-coupled). | The decision note's Path C output captures this explicitly. Approval of a Path C note triggers an IS revision before S-002's JIT spec is submitted; S-002 cannot ship a partial fix. |
| S-002's parity requirement means a regression on either page blocks the entire step. The single-commit rule + 3-round adversarial-review cap leaves limited slack if multiple parity-only findings emerge. | Parity is non-negotiable per HLPS §4.2. If the cycle limit is breached, the standard escalation path applies (CLAUDE.local.md §4 Cycle Limit — escalate to user for binding decision). |
| Landing S-002 (or S-003a) on PR #584 may itself trigger new automated-review threads (Copilot, GitHub Actions checks) that block subsequent step reviews. | Each step's JIT spec acceptance criteria include "no new HIGH/CRITICAL findings on the landed step's diff before the next step's review begins". New findings are addressed inline on the same step's branch state, or explicitly deferred with rationale recorded in the step's review history. |

### 4.2 Cross-step coupling

The IS treats S-002 and S-003a/b as nominally independent. At the
implementation level, two coupling surfaces exist and the JIT spec
authors must be aware of them:

- **Shared `document`-level event surface.** `project-controls` already
  installs document-level `click` and `scroll` listeners while the
  menu is open. S-002 (under Path B) might add a `keydown` listener at
  document or grid level, and S-003b will add a `keydown` listener
  scoped to the open menu. Listener ordering and capture-phase choices
  must not allow one step's handler to swallow events the other
  handler depends on. JIT specs for both work-streams must explicitly
  address listener-scope discipline (prefer scope-as-narrow-as-possible
  to avoid coupling).
- **Branch commit ordering.** With all four steps landing on a single
  PR branch, the order in which step commits are pushed must remain
  monotonic — fix-up commits on a landed step must not be interleaved
  ahead of an unstarted step's commit. Where review feedback on a
  landed step requires post-hoc fixes, those fixes go on top of the
  most recent step's commit, not between earlier step commits.

(A third theoretically-coupling concern — focus-restore behaviour when
the menu is opened from a row that itself has keyboard focus — is
covered by S-003b's "ESC restores focus to the trigger" criterion and
does not require IS-level treatment.)

## 5. Out-of-scope (re-stated for IS reviewers)

Per HLPS §2 and §7: dorc-web a11y audit beyond these surfaces;
migration to Vaadin menu/context-menu components; touch ergonomics
beyond reachability; visual redesign; UNC backend handling; menu-bar
dependency removal (already done in commit `50ea559e`).

## 6. Verification at IS level

Each step's "Verification intent" feeds the JIT spec's Acceptance
Criteria. The IS itself is verified by Adversarial Review against the
HLPS — specifically: every HLPS §4 success criterion and every §3
constraint is attributable to an IS step's "What changes" or
"Verification intent", and every IS step maps to at least one HLPS §4
criterion or constraint.

### 6.1 Verification environment baseline

Manual verification assumes a Windows + Chromium + NVDA workstation as
the primary environment, since this is the user's primary platform.
VoiceOver on macOS Safari is an acceptable substitution for the
screen-reader spot-check items if the primary environment is
unavailable. Touch / pen activation may be exercised via Chromium
DevTools device emulation; access to a real touch device is preferable
but not required.

### 6.2 HLPS coverage

| HLPS item | Step(s) | Notes |
|---|---|---|
| §4.1 ARIA roles, trigger↔menu wiring, focus indicator | S-003a | ARIA scaffolding + accessible name + focus-visible. |
| §4.1 keyboard interactions, focus management, deleteHidden wrap | S-003b | Built on S-003a's ARIA. |
| §4.2 (all bullets including listener-lifecycle parity) | S-002 | Mechanism per S-001's APPROVED decision. |
| §4.3 mouse-path regressions (menu-related) | S-003a + S-003b | Each menu step verifies the relevant subset. |
| §4.3 mouse-path regressions (row-activation related) | S-002 | Both pages. |
| §4.3 audit-action `Router.go` short-circuit preservation | S-003b | Custom-event/route dispatch unchanged. |
| §3 — no `vaadin-menu-bar` / `vaadin-context-menu` | S-003a, S-003b | Out-of-scope statement on each step. |
| §3 — visual UX preserved | S-002 (row), S-003a, S-003b (menu) | Each step's "What changes" / "Out of scope" enforces. |
| §3 — no new runtime dependencies | S-001, S-002, S-003a, S-003b | Trivially binds all steps. |
| §3 — custom events / Router.go payload identity | S-003b | Explicit bullet under "What changes". |
| §3 — accessible name = visible label | S-003a | Explicit bullet under "What changes". |
| §6 U-1 (auto a11y harness deferred) | — | Out of scope per HLPS. |
| §6 U-2 (active-item-changed semantics) | S-001 | Standalone APPROVED decision note is the deliverable. |
| §6 U-3 (context-menu gesture) | — | Assumed No per HLPS. |

---

## Review History

### R1 — DRAFT → REVISION

Three independent reviewers (ordering/atomicity, HLPS coverage,
delivery realism) returned APPROVE_WITH_FIXES. The R1 findings and
dispositions are summarised below; full reviewer reports are available
in agent transcripts.

| Theme | Reviewers | Disposition | Resolution |
|---|---|---|---|
| S-001 review gate underspecified — own status lifecycle, failure path, decoupled review from S-002 | R1-ordering F-01, R1-coverage F-02, R1-coverage F-03, R1-delivery F-03 | Accept | DECISION-U-2.md is now a standalone reviewable artifact with its own document-status lifecycle, its own panel review gate, an explicit Path-C failure path, and an explicit pre-execution gate item in S-002's self-audit. |
| S-003 sized too large; pre-split into S-003a and S-003b | R1-ordering F-02, R1-delivery F-01, R1-coverage F-04 | Accept | Replaced with S-003a (ARIA + accessible-name + focus-visible) and S-003b (keyboard + focus mgmt + deleteHidden wrap). Independent shippable value on each. Soft a-then-b ordering. |
| §6 coverage table mis-attributed §3 constraints to S-003 only | R1-coverage F-01 | Accept | Rewrote §6 coverage as full HLPS-criteria table. §3 constraints now correctly attributed across S-002 and the menu steps where they bind. |
| S-002 missing explicit listener-lifecycle parity bullet | R1-ordering F-03 | Accept | Added bullet to S-002 "What changes". |
| Hidden cross-step coupling (shared document-event surface, branch commit ordering) | R1-delivery (Hidden Coupling) | Accept | Added §4.2 Cross-step coupling subsection. |
| PR #584 sequencing — landed steps may trigger new automated-review threads that block subsequent step reviews | R1-delivery F-05 | Accept | Added §4.1 risk row with mitigation pattern. |
| Verification environment infrastructure assumptions (NVDA, Windows, Chromium) | R1-delivery F-04 | Accept | Added §6.1 verification environment baseline. |
| Diagram dangling arrow on S-003 | R1-ordering F-04 | Accept | Updated §3 sequencing diagram for the four-step shape; arrow removed. |
| S-002 single-commit + 3-round cycle-limit slack | R1-delivery F-02 | Defer to Delivery | Parity is non-negotiable per HLPS §4.2. If the cycle limit is breached, the standard CLAUDE.local.md §4 escalation path applies. Risk row updated to reflect this. |
| S-003 split threshold concrete-ness | R1-coverage F-04 | Defer to Delivery | Superseded — pre-split into S-003a/S-003b makes the threshold question moot. |
| Path B no-new-deps reminder | R1-delivery F-06 | Defer to Delivery | Already covered transitively by HLPS §3 constraints, which the IS §6 coverage table now binds explicitly to S-001. |

After this revision, status returns to `IN REVIEW` for R2. R2 reviewers
must verify R1 fixes were applied appropriately, check for regressions
in unchanged text, and (per CLAUDE.local.md §4 Re-Review Scoping) NOT
mine for new findings on R1 text that was implicitly accepted.

### R2 — IN REVIEW → APPROVED

| Reviewer | Lens | Outcome | Notes |
|---|---|---|---|
| Reviewer A (Opus) | Ordering / atomicity | **APPROVE** | All R1 findings (F-01..F-04) adequately addressed. Post-split atomicity audit clean (S-001, S-002, S-003a, S-003b each atomic and right-sized). No regressions. |
| Reviewer B (Sonnet) | HLPS coverage | **APPROVE** | All R1 findings (F-01..F-04) confirmed fixed. §6.2 coverage table fully maps every HLPS §4 criterion and §3 constraint to a step. No orphaned criteria, no contradictions. |
| Reviewer C (default) | Delivery realism | **APPROVE** | All R1 findings (F-01..F-06 + Hidden Coupling) verified. Cycle-limit recheck: each step plausible within ≤3 rounds. Cross-step coupling section adequate. No regressions. |

Unanimous approval. Status transitions to
`APPROVED — Pending user approval` per CLAUDE.local.md §2 Document
Status Lifecycle.
