# Adversarial Review R1 — Triage

| Field       | Value                                                     |
|-------------|-----------------------------------------------------------|
| **Round**   | 1 of max 3                                                |
| **Date**    | 2026-08-02                                                |
| **Subject** | `HLPS`/`IS` for grid-lit-renderers (F-4) and paper-dialog-retirement (F-3b) |
| **Panel**   | 3 reviewers, diverse models — correctness / completeness / executability |
| **Verdict** | ❌ **BOTH IMPLEMENTATION SEQUENCES FAIL THE GATE.** Status returned to REVISION. |

---

## 1. Outcome

The panel found **five defects that invalidate load-bearing premises** of the
plans, three of which would have caused user-visible damage had the sequences
been executed as written. Every finding below marked ACCEPTED was independently
verified against the installed Vaadin 25.2.6 sources or the application code
before acceptance — the panel's report was not taken at face value.

Execution of F-3b S-002 is **halted** pending revision.

---

## 2. Accepted — Critical

### R1-01 · `focus-target` is live Vaadin Grid API, not a dead Polymer attribute

**Claim under review** (mine): *"`focus-target` is a `paper-dialog-behavior`
attribute; on a Vaadin component it is inert"* — recommended as a standalone
cleanup of all 59 occurrences.

**Verified false.** It is Vaadin Grid API:

- `@vaadin/grid/src/vaadin-grid-keyboard-navigation-mixin.js:622` — on Enter/F2
  the grid does `cell._content.querySelector('[focus-target]')` to choose what
  to focus when entering cell interaction mode, falling back to "first focusable
  descendant" only when absent.
- `@vaadin/grid/src/vaadin-grid-filter-column-mixin.js:47` — Vaadin's own filter
  column *sets* the attribute.
- It does **not** appear anywhere in `@polymer/paper-dialog-behavior/`.

All 59 occurrences sit inside grid header and cell renderers — exactly where the
attribute is load-bearing. Deleting them would have silently degraded keyboard
navigation and accessibility across every grid with interactive cells.

`lit-analyzer`'s `no-unknown-attribute` on `focus-target` is a **false
positive**: it does not model Grid's runtime contract.

**Action:** claim struck from the F-3b HLPS; the "standalone cleanup" removed
from the follow-up list; `focus-target` reclassified as **must be preserved**
through the F-4 renderer rewrites, which touch precisely these renderers. The
audit's F-3 finding is corrected too, since the wrong claim was published there.

**This error was also reported to the user as a quick win. It is retracted.**

### R1-02 · Mechanical `dialog-confirm` conversion produces unclosable dialogs

**Claim under review** (mine): *"Every instance already has an `@click` handler
doing the real work, so closing becomes one added line."*

**Verified false, and worse than the panel reported.** The panel found 3 of 12
with handlers; direct measurement of the button tags finds **zero of 12** carry
`@click` on the element itself. `dialog-confirm` is inert on `<vaadin-dialog>`.

Combined with the (correct) mandate to set `no-close-on-esc` and
`no-close-on-outside-click`, a mechanical conversion yields **12 dialogs with no
dismissal path whatsoever** — users trapped with no keyboard or pointer escape.
The IS's own "After" template would have produced exactly this.

**Action:** transformation table corrected — a close handler must be **created**
for every dialog, not assumed. Per-step definition of done now requires an
explicit "closes via its Close button" assertion rather than treating it as a
given.

---

## 3. Accepted — High

### R1-03 · Four `vaadin-notification` renderers missing from the inventory

Found independently by two reviewers. `components/notifications/{error,warning,
success}-notification.ts` and `deploy/notifications/successful-deploy-notification.ts`
bind `.renderer=` on `<vaadin-notification>`.

These are not incidental: `error-notification.ts:49` reads `this.errorMessage`
(a reactive `@property`) from a stable bound renderer with no
`requestContentUpdate()` — **the exact defect F-4 exists to fix, in the
application's global error toast**. `@vaadin/notification/lit` ships
`notificationRenderer`.

SC-1 as written ("grep returns zero") could never pass. A fifth binding, an
imperative `notification.renderer = …` in `bundle-editor-form.ts:480`, was also
missed.

**Action:** inventory corrected; a step added for the notification renderers;
the imperative assignment explicitly scoped out with a reason.

### R1-04 · Dependency arrays on undecorated fields never fire

`LitRendererDirective.update()` runs only when Lit re-renders the host template,
so a dependency on a non-reactive field is never compared.

Verified: `_editingValueId` is `private _editingValueId: number | undefined`
with **no decorator** in `page-variables.ts:74`,
`page-variables-value-lookup.ts:47` and `env-variables.ts:79` — and it drives
**6 of the 12** `requestContentUpdate()` calls the plan requires be deleted.
Converting those to `columnBodyRenderer(fn, [this._editingValueId])` fires
nothing; inline variable editing would stop updating, while the plan's own
constraint appeared satisfied.

**Action:** new precondition — every value in a dependency array must be
`@property`/`@state` on the host; promoting undecorated fields is in scope for
the step that depends on them.

### R1-05 · S-002 is graded backwards

Verified: **40 of 43** distinct `headerRenderer` methods reference `this.*`.
Header renderers are dominated by the in-header filter/search pattern, so S-002
is the *highest*-state group, not the lowest. "Item-only header renderers" is
also a category error — headers never receive an item.

**Action:** S-002 re-graded and re-scoped; filter headers split into their own
screen-grouped steps.

### R1-06 · The stateless/stateful split used the wrong detector

My 91/46 split scanned for `@property`/`@state` decorators, but this codebase
holds filter state in undecorated fields (`page-scripts-list.ts:68-73`,
`page-monitor-requests.ts:82-88`). Any such renderer would land in the
"item-only, empty array" bucket — a silent regression, and precisely the failure
mode the HLPS names as its top risk.

**Action:** the split is withdrawn as unreliable; the per-method audit must be a
body-read analysis, and must run before, not during, conversion.

### R1-07 · Dialog content *is* reachable from host styles

**Claim under review** (mine): content renders outside the shadow root, so
sizing must move inline.

**Verified false.** `@vaadin/dialog/src/vaadin-dialog-overlay-mixin.js:71` does
`this.owner.appendChild(root)` — the renderer root is a light-DOM child of the
`<vaadin-dialog>` element, which lives in the host's shadow root. Host `static
styles` therefore apply, as `terraform-plan-dialog.ts` already demonstrates
in-repo.

Two better mappings were missed: `<vaadin-dialog>` has first-class `width` /
`height` properties, and exports `backdrop, overlay, header, title,
header-content, content, footer` parts, so the existing CSS can be re-pointed at
`::part(overlay)` rather than re-authored.

**Action:** U-2's resolution rewritten. Sizing is a property/`::part` mapping,
not a content rewrite.

### R1-08 · Configure-then-open breaks under a renderer

The overlay only invokes the renderer when open, so content does not exist while
closed. `attached-databases.ts:214-220`, `:222-224` and
`page-permissions-list.ts:234-236` query dialog content and call methods on it
*before* opening — guaranteed null dereferences after conversion.
`page-daemons-list.ts:144` already uses the correct gated model.

**Action:** inverting configure-then-open into property bindings is now an
explicit in-scope content change, contradicting the previous "content unchanged"
exclusion.

### R1-09 · Existing tests call renderers with the current signature

`tests/pages/page-monitor-requests.test.ts:372,428` invoke
`detailsHeaderRenderer(root)` and `detailsRenderer(root, div, model)` directly.
S-008 breaks them outright — a rewrite of existing scaffolding, not the "add a
regression test" the IS describes.

### R1-10 · Bindings that self-refresh today would regress to never refreshing

The 8 inline `.bind(this)` bindings and 1 inline arrow create a new function
identity per host render, causing Vaadin to reassign the renderer — that is
their current refresh mechanism. Under the directive an empty dependency array
pins the first closure forever. My documents recorded these as stylistic
variants.

**Action:** explicit rule — any binding currently written as inline `.bind(this)`
or an inline arrow must never receive an empty dependency array.

---

## 4. Accepted — Medium

| ID | Finding | Action |
|----|---------|--------|
| R1-11 | A dependency change triggers a debounced **whole-grid** `requestContentUpdate()`, not a per-column re-render. With filter headers converted, each keystroke re-renders every visible cell and rewrites the filter field's own `value` mid-typing — a caret/IME hazard. | Blast radius documented; a keystroke-level test on one converted filter header is now required before the pattern is replicated. |
| R1-12 | `auto-width` columns (75 usages, 20 files) never recalculate on `requestContentUpdate()`. Today content never changes, so this is latent; after the migration it is live and content can clip. | Added to per-step DoD. |
| R1-13 | S-004 (combo-box) and S-006 (`innerHTML`) overlap on 8 renderers; S-004 is therefore not low risk. Side-effect category counts are unreliable — my own recount gives 11/13/4 against a stated 10/12/4, and the panel gives different figures again. | Both step gradings corrected; the side-effect audit is to be **redone** as its own step with a stated, reproducible method. |
| R1-14 | `dataProvider` grid count is 13, not 7. | Corrected. |
| R1-15 | The reference exemplar both plans mandate copying, `terraform-plan-dialog.ts:134`, has an incomplete dependency array (`[this.plan, this.error]` while the renderer branches on `this.loading`). | Named as a known-defective exemplar; fix it before copying. |
| R1-16 | `size-position` CSS inventory omitted `max-width: calc(100vw - 32px)`, `box-sizing`, `position: center` — the responsive behaviour that keeps dialogs on-screen on small viewports. | Inventory corrected; responsive width added to verification. |
| R1-17 | SC-2's `grep '@polymer/'` cannot detect `@webcomponents/shadycss`. It is 13 packages, not 12. | Check and count corrected. |
| R1-18 | SC-4's stated baseline (132 / 436 in 85 files) was stale; a fresh run gives 135 / 439 in 86 files, and `lit:type-checking` covers only `src/{components,pages}`. | Baselines re-measured and scope stated. |
| R1-19 | Side-effect category arithmetic did not add up: 10 + 12 + 4 = 26 against a stated total of 28. | Confirmed; superseded by R1-13, which redoes the audit. |
| R1-20 | S-003's "slice by directory" is undefined; dependency-array derivation has no stated method; "invert the test" is ambiguous; dark-mode "done" is subjective. | Each given an explicit, checkable definition. |

---

## 5. Downgraded / Rejected

| ID | Finding | Disposition |
|----|---------|-------------|
| R1-21 | "S-002 should land after all conversions, or dead-import deletion orphans CSS across commits." | **DOWNGRADED to LOW.** Deleting provably dead imports and dead CSS is independently correct and independently revertible; a partial revert leaves dead code deleted, which is not a broken state. |
| R1-22 | "Combo-box separation is organisational, not functional." | **REJECTED.** Superseded by R1-13, which shows 8 of the 17 combo-box renderers are `innerHTML` rewrites — the separation is more justified than the IS argued, not less. |
| R1-23 | "No effort estimates; a PM cannot plan." | **DEFERRED.** Legitimate, but the IS is a technical sequencing document; estimation belongs elsewhere. |
| R1-24 | `column._grid` may be `undefined` inside `runRenderer()` for a momentarily detached column. | **ACCEPTED as LOW** — plausible given the conditional-grid templates here, but unproven. Added to the risk table with a smoke test, not treated as a defect. |
| R1-25 | `page-users-list` is a poor reference conversion; its `user-or-group-created` event sets `composed` without `bubbles`, so its close listener is dead code. | **ACCEPTED as LOW**, and the exemplar changed to `page-sql-ports-list`. The dead listener is a pre-existing bug, logged separately rather than fixed here. |

---

## 6. What This Round Says About the Planning

Three of the five critical/high defects share a root cause: **I asserted runtime
behaviour from reading names and documentation rather than from reading the
implementation.** `focus-target` looked Polymer-ish; `dialog-confirm` was assumed
to be backed by handlers that were never checked; the overlay's DOM position was
asserted without opening the mixin.

The counts that *were* derived mechanically (dialog totals, `PaperDialogElement`
references, `size-position` blocks, the 12 `requestContentUpdate()` calls) all
survived verification intact. The failures are concentrated in claims about
behaviour, and the corrective is to verify behaviour against source or a running
app before writing it into a plan — which is what resolving U-1 did correctly,
and what the rest of the document did not.

---

## 7. Next Round

Both documents return to **REVISION**. Round 2 re-reviews after:

1. The side-effect audit is redone with a stated method (R1-13, R1-19).
2. The stateful/stateless classification is redone by body-read analysis (R1-06).
3. Every accepted correction above is applied.

No implementation step executes until round 2 passes.
