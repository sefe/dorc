# HLPS — Responsive Record View (DORC-owned grid presentation layer)

| Field | Value |
|---|---|
| Status | **REVISION** — R2 panel returned 3/3 REVISE; this version implements the R2 triage (§13) as a single coherent rewrite |
| Date | 2026-08-02 |
| Owner | Ben Hegarty |
| Topic slug | `responsive-record-view` |
| Origin | User observation: "we're about to move past the capabilities of vaadin… we need something more specific to DORC so we can render better in both large screens and small phone screens" |
| Component | `dorc-web` (frontend only) |
| Direction | **RESOLVED** — Vaadin Grid retained at every width; at narrow width the grid renders GitHub-style single-column list rows from a per-view row template (§3.4). U-1, U-2, U-8a, U-17, U-18 resolved. Remaining open unknowns are per-step or user-decision items routed in §7. |

> **Counting method (binding for every figure in this document).** Column counts
> are **opening tags only**: `grep -o '<vaadin-grid-column\|<vaadin-grid-sort-column'`.
> Any figure added later must state its derivation inline or it is inadmissible.

---

## 1. Problem Statement

`dorc-web` presents most of its data through Vaadin Grid. As of `origin/main`
(`9be20dd`), **61 files import `@vaadin/grid`, of which 37 actually render one**
(the other 24 import types or side effects only). Those 37 files declare **180
columns** — 116 `<vaadin-grid-column>` and 64 `<vaadin-grid-sort-column>` — every
one written as markup inside a `render()` method.

### 1.1 The mobile symptom — measured

The current responsive mechanism is `src/dorc-web/src/helpers/responsive-mixin.ts`
— a mixin exposing a reactive `_narrowScreen` boolean driven by
`matchMedia('(max-width: 768px)')`. **25 classes** apply it, and each
independently decides which columns to suppress via
`?hidden="${this._narrowScreen}"`.

**The symptom is established by measurement, not inference**
(`MEASURE-U-18-narrow-overflow.md`): **7 of 10 measured views require horizontal
scrolling at 375px**, including both grid views on the on-call triage journey —
`page-monitor-requests` overflows by 139px and `env-monitor` by 79px
(conservative figures; harness header fidelity biases them low). The worst view,
`page-env-history`, overflows by 389px with only 4 columns showing. Overflow
does not track column count: a 4-column view is the worst measured, a 5-column
view the mildest, and the 2-column view fits.

The measurement also establishes **why** (MEASURE §4): partly authored width
policy (three hardcoded widths cause 94% of `page-env-history`'s excess — fixable
cleanup), but irreducibly **content demand**: the journey views' surviving
columns need 454–514px of content width, and forcing them into 375px with
deliberately narrow explicit widths truncates **~54% of the Details cell** — the
field an engineer identifies a request by. At phone width, identity content
needs the full row, which no multi-column layout can give it.

The second failure is structural and independent of the numbers:

- **The policy is opt-in, so coverage gaps are guaranteed.** `page-servers-audit.ts`
  and `page-databases-audit.ts` carry zero narrow-screen handling — the two
  worst-rendering views in the codebase, precisely *because* nothing obliged
  their authors to opt in. `env-deployments.ts` and `add-edit-access-control.ts`
  are the same.
- **The policy is unreviewable.** No artifact answers "which fields matter on a
  phone" for any DORC entity; the decision is distributed across 180 column
  declarations.
- **The breakpoint is forked.** `components/dorc-app.ts` imports
  `NARROW_BREAKPOINT` but reimplements the `matchMedia` subscription
  (`:207,221,320-322,380`); **26 files** (28 occurrences) hardcode
  `@media (max-width: 768px)` in component CSS.

### 1.2 Scope survey: which Vaadin Grid capabilities DORC actually uses

*(Retained for what it legitimately establishes: a bound on the feature surface
the wide path must keep working, which sizes SC2. It is **not** evidence about
Vaadin's ceiling — low usage of a capability is equally consistent with "never
needed" and "worked around" — and the build-on-Vaadin decision rests on §9.2's
replacement-cost argument, not on this table.)*

| Vaadin Grid capability | Occurrences in `src/dorc-web/src` |
|---|---|
| Tree grid (`item-has-children`) | 0 |
| Row selection (`selectedItems`) | 0 |
| Frozen columns | 1 (`page-projects-audit.ts:211`) |
| Row details (`rowDetailsRenderer`) | 1 (`page-projects-audit.ts:201`) |
| Lazy `dataProvider` | 13 files |
| `resizable` | 164 |
| `auto-width` | 75 |
| Multi-sort | 29 |
| Column reordering | 25 |

Theme-layer note: `registerStyles` overrides are confined to one 110-line module
(`router/style-registrations.ts`), whose own header records that grid cell
styling migrated out to `cellPartNameGenerator` + `::part()` (**31 references
across 11 files**). Wide-path cell styling for migrated views must keep working
(SC2); note the mechanism styles shadow cells and cannot style content inside
slotted `vaadin-grid-cell-content` — relevant to the chip design in §3.4.

Neither prior grid work programme was blocked by a Vaadin capability limit:
`docs/request-grid-perf/` addressed non-SARGable SQL; `docs/grid-menu-a11y/`
resolved *in favour of* Vaadin's `active-item-changed`.

---

## 2. Goal

Make DORC's record views usable on both large screens and phones by declaring
each view's field priority **once, in a reviewable artifact**, and rendering it
appropriately at every width — the ordinary multi-column Vaadin Grid on
desktop, and a **GitHub-style single-column list** (same grid element, §3.4) at
phone width, polished to the standard of GitHub's mobile issue list rather than
the bare chevron-on-a-table of today's `page-projects-audit`.

The target users are **application support and on-call engineers** (U-1); the
target situation is incident triage away from a desk. This is a foundation laid
ahead of demand — no current phone usage or telemetry exists — and §8 carries
that risk explicitly.

---

## 3. Scope

### 3.1 In Scope

- **The §3.4 row-template contract and its supporting infrastructure** — the
  normative deliverable. (An earlier full column-descriptor model is
  **superseded**; §3.4 absorbs its obligations. See §13 R2c-F3.)
- **A narrow-mode mechanism** per §3.4: threshold detection (container-based for
  dialog-hosted views), column collapse, and the list-row rendering path.
- **A filter/sort/controls bar** for narrow width (§3.4) covering: the
  `headerRenderer` filter inputs (53 across 16 files codebase-wide; 10 in the
  two journey views), the sort surface of declaratively-sorted views (step 6
  carries 7 `<vaadin-grid-sort-column>`s), and **non-filter interactive header
  content** — `page-monitor-requests`/`env-monitor` embed a live
  `<connection-status-indicator>`, an auto-refresh toggle and a manual-refresh
  button inside the Id column's `headerRenderer` (`page-monitor-requests.ts:711-754`);
  these must remain reachable at narrow width (SC11).
- **Row actions at narrow width** (§3.4 actions slot): integration with the
  `grid-button-groups/` components used by the pilot (`request-controls`,
  `edit-comments-controls` are the grid-coupled ones — see U-7), and U-2's
  interactive cell renderers (in-cell checkboxes, edit/save/cancel cycles).
- **`page-monitor-result` narrow layout** — a journey step, not a grid
  migration: the page composing `request-status-card` + `<vaadin-details>` +
  `component-deployment-results` (`page-monitor-result.ts:387-416`) must be
  usable at 375×667 for SC9 to pass. This bullet is that work's scope anchor.
- **Preservation of the lazy-loading contract** (13 files, 23 `clearCache`
  sites) at both widths — trivially satisfied at narrow width because the
  narrow rendering *is* the same grid (U-11 dissolved), but SC4 still verifies
  it.
- **Test migration.** Existing suites assert the idiom this work removes
  (`responsive-grids.test.ts:100,171` on two pilot views;
  `grid-column-hiding.test.ts`; `responsive-mixin.test.ts`). Coverage removed
  from a migrated view is re-established against the new mechanism.
- **Breakpoint consolidation**: the `dorc-app.ts` fork, and a stated
  disposition for the 26 files / 28 CSS-hardcoded occurrences.
- **An idiom decision record for new views** (U-20).
- **A documented per-entity field-priority decision** (row template) per
  migrated view.
- **Width-attribute cleanup on the worst-authored views** (`page-env-history`'s
  three hardcoded widths; the audit pages' `auto-width flex-grow="0"` columns)
  — independent of the list work and worth ~hundreds of px at every width
  (MEASURE §4a).

### 3.2 Out of Scope

- Removing `@vaadin/grid`.
- Migrating all 37 grid-rendering views; the pilot proves the pattern.
- Replacing any other Vaadin component.
- Backend or API changes.
- A visual redesign of desktop width.
- A third-party headless table library (§9.2).
- Offline / PWA / native-app concerns. *(Ordinary mobile network flakiness is a
  §8 risk, not a scope item.)*

### 3.3 Target journey and pilot set

U-1 resolved to: no current phone usage, no telemetry, deliberate foundation for
application support and on-call engineers. The target journey: *a deployment
has failed and I am not at my desk.*

| Step | View | Columns *(derived per head-of-doc method)* | Role | Hard case |
|---|---|---|---|---|
| 1 | `pages/page-monitor-requests.ts` | 7 (3 hidden → 4) | Migration target | Lazy `dataProvider`; 5 `headerRenderer`s carrying all filtering **plus live connection/refresh controls**; in-cell `.id-btn` activation |
| 2 | `pages/page-monitor-result.ts` | 0 — renders no grid | **Journey step only** | Page/card layout at 375px (owned by §3.1's `page-monitor-result` bullet) |
| 3 | `components/component-deployment-results.ts` | 5 (2 hidden → 3) | Migration target | `.items` + `all-rows-visible` (non-virtualised); hosted in `<vaadin-details>`; log/plan action buttons |
| 4 | `components/environment-tabs/env-monitor.ts` | 7 (3 hidden → 4) | Migration target | `@active-item-changed` row activation **that opens the result page** (`env-monitor.ts:620-636`) — the direct collision with §3.4's activation-opens-disclosure default (U-15) |
| 5 | `components/make-like-production.ts` | 3 (0 hidden → 3), **one grid** | Migration target | **Genuinely dialog-hosted** via `dialogRenderer` (`make-like-production-dialog.ts:91-96,126`) — container-width measurement; applies no `ResponsiveMixin` today |
| 6 | `pages/page-env-history.ts` | 9 (5 hidden → 4) | Migration target | **7 declarative `<vaadin-grid-sort-column>`** — the dominant codebase idiom, zero present in steps 1/3/4; `edit-comments-controls` row actions; worst measured overflow (+389px) |

Notes carried from the panel rounds:

- Steps 1 and 4 are near-duplicates (verified identical columns/renderers).
  Step 4 is retained because it carries the **activation-semantics risk that is
  still live under §3.4** (its row activation opens a page, not a disclosure —
  U-15), not merely because it uses a different API.
- Every grid-bearing pilot member has a per-row actions column that is *not*
  hidden at narrow width today — the actions problem is exposed by
  construction (R2c), and §3.4 now answers it.
- **On excluding the audit pages**: they are the genuinely worst
  remaining-on-phone views and are lazy `dataProvider` views, so the exclusion
  is not convenience — it rests on the persona argument alone (no on-call
  engineer reads a server audit log on a phone at 02:00). They inherit the
  width-attribute cleanup (§3.1 last bullet) and are first in line post-pilot.

### 3.4 The row-template contract (normative)

The chosen direction (U-17): **one `<vaadin-grid>` element, two presentations.**
Wide width is today's multi-column grid, unchanged. At narrow width the grid
collapses to a single column rendering GitHub-mobile-style list rows. No second
rendering engine; Vaadin's virtualiser, `dataProvider` contract, and grid
keyboard/ARIA model apply at both widths.

**Artifact shape** *(R2c-F3)*. No wrapper element is built. The deliverables are:

1. a **shared list-row template helper** (Lit template function + styles) that
   renders a row template's slots into the narrow column's cell renderer;
2. a **per-view row-template module** — the reviewable artifact SC3 asserts:
   one exported declaration per view naming what fills each slot;
3. a **shared narrow-mode controller** (Lit `ReactiveController`) owning
   threshold detection (container-width via `ResizeObserver` for dialog-hosted
   views), column collapse/restore, and bar wiring. Migrated views drop
   `ResponsiveMixin`; the controller replaces it *(R2c-F5)*.

Naming per CLAUDE.md pending U-6; defaults there.

**Slots** *(expanded per R2b/R2c; optionality per R2c-F6)*:

| Slot | Required | Content | Pilot mapping (U-8a resolution) |
|---|---|---|---|
| **primary** | yes | The record's identity, full row width | `idRenderer` + `detailsRenderer` widest line (steps 1/4); component name (3); property name (5); env name + date (6) |
| **metadata** | no | Muted secondary fields inline | timings/user (1/4); timings (3); value (5); updated-by/type (6) |
| **chip** | no | Status as a visible chip. **New work driven by the item model** (e.g. `item.Status`), *not* `cellPartNameGenerator` — no pilot view uses part-name generation, and `::part()` cannot style slotted cell content *(R2b-F6)*. Part-name generation remains a separate wide-path obligation. | Status (1/3/4); none (5/6 — slot unused) |
| **actions** | yes where the view has them | Per-row actions as a **trailing overflow menu reusing the `grid-menu-a11y`-hardened menu pattern**; a view's template may promote at most one action inline *(R2c-F1)* | `request-controls` (1/4); log/plan buttons (3); override button (5); `edit-comments-controls` incl. its edit/save/cancel cycle (6) |
| **disclosure** | yes | Row activation opens row details carrying the remaining fields | all |

**The bar** *(R2b-F4, R2c-F2, R2c-F7)*. At narrow width, filtering, sorting and
view-level controls live in a bar. **Default mechanism: the bar is hosted in
the narrow column's `headerRenderer`** — Vaadin shows the header row when any
column has a `headerRenderer`, and sorters/filters inside it remain
**connected**, which matters because hiding a column removes its cell content
from the DOM and disconnected sorters fall out of `_getActiveSorters()`
(`vaadin-grid-column-mixin.js:527-540`; `vaadin-grid-sort-mixin.js:170`) — the
naive "hide everything, float a div above" design silently drops sort from the
`dataProvider` query, including declarative defaults like the audit views'
`direction="desc"`. The bar must cover: filter inputs; a sort control for
declaratively-sorted views (step 6 has sorters and no filters); and a
**view-level controls region** for non-filter header content (connection
status, auto-refresh). Fallback if the header-hosted design fails in the IS
spike: controller-managed `sortOrders` applied at the `dataProvider` level.

**Threshold** *(R2c-F4)*. The collapse fires at the existing
`NARROW_BREAKPOINT = 768px` of **container** width. Derivation: the worst
measured as-written content demand is 764px (`page-env-history`, MEASURE §3);
a single threshold at 768 guarantees no migrated pilot view renders an
overflowing table. The 375–768px band deliberately gets the list — a single
discontinuity, no per-view thresholds, no tablet tier (U-4's default,
restated). The collapse is **unconditional** for migrated views, including
`component-deployment-results` which fits at 375px — idiom consistency over
per-view exceptions *(R2c-F14)*.

**Styling notes.** List-row text wraps only where the template says so —
Vaadin's default is `nowrap` + ellipsis, so the template opts in explicitly
(`wrap-cell-content` theme or equivalent) *(R2b-F11)*. On crossing back to wide,
`auto-width` columns do not recalculate automatically; the controller triggers
recalculation *(R2b-F11, SC10)*.

**Accessibility posture** *(R2b-F12)*. The narrow rendering **retains Vaadin's
grid/table ARIA semantics** — it is announced as a one-column table with row
count and position, which preserves the announcements SC5 needs; the row
template's *internal* semantics (chip labelling, disclosure `aria-expanded` —
free from Vaadin's row-details implementation, `vaadin-grid-a11y-mixin.js:104-116`
— actions menu ARIA) are the new obligations, decomposed in SC5.

**Design-quality gate.** "Polished" is adjudicated at the pilot review:
side-by-side against GitHub's mobile issue/PR list, owner + panel, with
authority to fail the pilot on presentation quality alone. SC11 carries the
objective half against the named part/attribute contract the template helper
exposes.

---

## 4. Constraints

- **No regression in grid accessibility.** The `docs/grid-menu-a11y/` outcomes
  are binding. The narrow rendering **inherits Vaadin's grid roles and keyboard
  model** (it is the same element); what it does *not* inherit are the row
  template's internal semantics — chip, disclosure content, actions menu, bar —
  which SC5 decomposes *(corrected per R2a-F5)*. The pilot exercises the
  row-activation half of `grid-menu-a11y`; the overflow-menu half is exercised
  via the actions slot's reuse of that menu pattern.
- **Container-width measurement, not viewport.** Step 5 is dialog-hosted. (An
  earlier "13 dialog-hosted grids" count is withdrawn — not reproducible; at
  least one genuine case exists and is in the pilot.)
- **Cognitive complexity must not increase** (CLAUDE.md). Adjudication: a
  side-by-side of one migrated view's row-template module against its
  pre-migration `render()`, judged by the panel at the pilot gate, with
  authority to fail the pilot.
- **Naming per CLAUDE.md** — no grab-bag names. U-6 carries defaults.
- **Virtualisation preserved at both widths** — satisfied by construction
  (same grid element), verified by SC4.
- **Incremental and reversible.** Migrated and unmigrated views coexist
  indefinitely; no flag-day.
- **Vaadin fixed** at `^25.2.6`.
- **Existing test tooling.** Vitest + Playwright real browsers
  (`vitest.config.ts:22-29`); no new test framework — U-16 (runtime a11y
  assertions) is the open exception requiring a user decision.

---

## 5. Success Criteria

| ID | Criterion | Measurement |
|---|---|---|
| **SC1** | At 375px container width, a migrated view requires no horizontal scrolling **of the grid's internal scroller** *(oracle corrected per R2b-F3 — the page body never overflows; the U-18 instrument's measurement is the model)*. Declared inner regions (log excerpts) may scroll internally when named in the row-template module. | Automated: grid internal scroller `scrollWidth ≤ clientWidth` at 375×667 in the Playwright harness (private-API access noted and accepted as the U-18 instrument does), plus manual check. |
| **SC2** | At desktop width, a migrated view is functionally equivalent pre/post for sorting, filtering, resizing, reordering, row actions, lazy paging and cell styling (incl. `cellPartNameGenerator` parts where used). | Written per-view checklist authored in the JIT Spec and approved before migration; existing suites pre-classified there as behaviour-asserting (must pass) or idiom-asserting (replaced per §3.1 test migration). No post-hoc reclassification. |
| **SC3** | Field-presentation priority is declared once per entity, reviewable without reading `render()`. | The view's **row-template module** exists as a named, importable artifact; quote-agnostic regex confirms no `_narrowScreen` binding remains in the migrated view *(rekeyed per R2a-F8/R2c-F5)*. |
| **SC4** | Lazy views retain incremental fetch at both widths. | Network assertion: scrolling issues incremental `dataProvider` calls; initial load does not fetch the full collection. |
| **SC5** | Keyboard and screen-reader semantics hold at both widths. | Decomposed criteria authored **before** the renderer step: table-semantics announcements retained (row count/position — explicit acceptance of one-column-table ARIA per §3.4); focus order within a list row; `aria-expanded` on disclosure (from Vaadin row-details); actions menu per `grid-menu-a11y` §4; label/value association within row content; live region for incrementally fetched rows; **WCAG 2.5.8 (AA, 24px)** target size *(corrected from 2.5.5/AAA per R2b-F10)*. Verification mechanism blocked on U-16. Manual screen-reader spot-check per `grid-menu-a11y` §5. |
| **SC6** | The 768px breakpoint value appears in exactly one module; viewport subscription (`dorc-app.ts` shell) and container measurement (controller) are separately testable mechanisms. | Static review covering `matchMedia` and the **26 files / 28 occurrences** of CSS-hardcoded `max-width: 768px`. |
| **SC7** | The pilot includes ≥1 lazy `dataProvider` view, ≥1 filter-bearing view, ≥1 dialog-hosted view, ≥1 predominantly-declarative sort-column view, and ≥1 view with row actions. | §3.3: steps 1/4; 1/4; 5; 6; all. |
| **SC8** | No new runtime dependency without explicit approval. | `package.json` diff. |
| **SC9** | The on-call triage journey is completable at 375×667: locate a named failed request (via the bar's filter), open it, identify the failed component, read its failure reason. | Scripted walkthrough, seeded fixture, executed by a non-author, pass/fail recorded. **A rendering-completeness gate, not usage validation** (§8). |
| **SC10** | Sort order (including declarative defaults), active filters, scroll position and disclosure state survive a container crossing the threshold in both directions; `auto-width` recalculates on return to wide. | Automated test driving container width across 768px; asserts the `dataProvider` receives unchanged `sortOrders`/`filters` across the crossing *(sharpened per R2b-F4/R2c-F13)*. |
| **SC11** | The narrow rendering meets the §3.4 design bar, and **no interactive affordance present at wide width is unreachable at narrow width** *(R2c-F7)*. | Objective half automated against the template helper's part/attribute contract (header row hidden; primary + metadata rendered; chip where declared; actions menu reachable; bar controls present — including connection/auto-refresh on steps 1/4). Subjective half: GitHub-reference side-by-side at pilot review, owner + panel, authority to fail on presentation quality alone. |

---

## 6. Verification Approach

1. **Baseline capture** (SC2) — per-view checklist recorded pre-change; the
   four affected suites enumerated (`responsive-grids.test.ts`,
   `grid-column-hiding.test.ts`, `responsive-mixin.test.ts`,
   `page-monitor-requests.test.ts`).
2. **Infrastructure tests** — controller threshold/collapse (container-driven,
   not `matchMedia`-mocked — no existing pattern, U-12), template helper
   contract, bar sort-state preservation.
3. **Per-view behavioural comparison** (SC2) against the approved checklist.
4. **Narrow-viewport verification** (SC1) — internal-scroller overflow
   assertion + manual.
5. **Lazy-fetch assertion** (SC4) *(added per R2a-F7)*.
6. **Accessibility pass** (SC5) — decomposed criteria; keyboard traversal both
   widths; manual screen-reader spot-check.
7. **Threshold-crossing test** (SC10) — state and width-recalc assertions.
8. **Static / diff review** (SC3, SC6, SC7, SC8).
9. **Scripted triage walkthrough** (SC9) — seeded fixture, non-author.
10. **Design-bar review** (SC11) — objective assertions + GitHub-reference
    side-by-side at the pilot gate *(added per R2a-F7)*.
11. **Type and lint gates** — `npm run type-checking`, `eslint`.

---

## 7. Unknowns Register

Resolved unknowns keep their row (struck Blocking cells) for the record; open
unknowns carry explicit routing *(format per R2c-F12)*.

| ID | Description | Owner | Blocking | Status / Resolution |
|---|---|---|---|---|
| **U-1** | Is DORC used on phones, and by whom? | User | ~~YES~~ | **RESOLVED 2026-08-02.** No current usage or telemetry; deliberate foundation for app-support and on-call personas. Consequence in §8 and SC9. |
| **U-2** | Inline cell editing or lazy tree grid on the roadmap? | User | ~~YES~~ | **RESOLVED 2026-08-02.** Tree grid no; inline editing not required beyond today's. (Evidence correction on record: `selectedItems` measures selection, not editing; in-cell editing exists — `add-edit-access-control.ts:531-543,589-599,630-640`, `edit-comments-controls.ts` — and the actions/edit slots accommodate it.) |
| **U-3** | Field-priority ranking per entity — the content of each row-template module. | User | **Per-step: blocks each migration step's JIT Spec**; the infrastructure step needs one worked example (step 1) to validate the contract | Open. Framing: *what does an on-call engineer need to triage at 375px?* |
| **U-4** | Is one threshold sufficient? | Agent/User | NO | **Default adopted in §3.4**: single 768px discontinuity, derived from worst measured demand (764px); no tablet tier. Revisit only on pilot evidence. |
| **U-5** | Pilot set composition. | Agent/User | NO | §3.3 (six entries, roles stated). Pending user confirmation at the IS checkpoint. |
| **U-6** | Naming (CLAUDE.md-compliant). | User | NO | Open. Defaults restated for the §3.4 artifacts *(R2c-F15)*: template helper `dorc-list-row`, per-view modules `<view>-row-template.ts`, controller `NarrowListController`. |
| **U-7** | Grid-cell coupling of the pilot's action components. | Agent | **Per-step: blocks the JIT Specs of steps 1/4 (`request-controls`) and 6 (`edit-comments-controls`)** | **PARTLY RESOLVED, rescoped per R2a-F10**: 4 of 12 components reference a grid (`edit-comments`, `env-controls`, `project-controls`, `request-controls`); `server-controls`/`database-env-controls` are grid-agnostic (0 refs). `request-controls.ts:45-58` carries `vaadin-grid` / `vaadin-grid-cell-content` selectors. Residual: whether those two need an adaptation layer inside the actions menu. |
| **U-8a** | Do the pilot views' renderers re-express as row templates? | Agent | ~~YES~~ | **RESOLVED 2026-08-02 by R2 panel inspection (R2b/R2c) — "no" under the original 4-slot contract** (no home for actions, sorters, header controls, chip mechanism mis-cited), **"yes" under the §3.4 contract as now specified**: full mapping in the §3.4 slot table. This resolution is what forced the contract expansion. |
| **U-8b** | Codebase-wide renderer migration cost (117 `.renderer=` / 47 files; 53 `.headerRenderer=` / 16 files). | Agent | NO | Open; cost estimation for post-pilot sequencing. |
| **U-9** | Column resize/reorder at narrow width. | User | NO | Default: unavailable at narrow width, no error affordance (they are meaningless in a one-column list). |
| **U-10** | Concrete design of the bar. | User/Agent | **Per-step: blocks the infrastructure step's JIT Spec** | **NARROWED**: mechanism default is header-hosted (§3.4, keeps sorters connected); residual is layout/content per view (which filters surface, sort control form, controls region). |
| **U-11** | DOM growth at narrow width. | — | ~~YES~~ | **DISSOLVED by U-17**: the narrow rendering is the same grid; Vaadin's virtualiser applies. *(Stale references swept per R2a-F3.)* |
| **U-12** | Test pattern for container-driven behaviour (existing tests mock `matchMedia`; the controller uses `ResizeObserver`). | Agent | NO — must be answered in the infrastructure step's JIT Spec | Open. |
| **U-13** | Browser/device matrix for the narrow rendering. | User | NO | Default: the three Playwright engines in `vitest.config.ts`. |
| **U-14** | State continuity across a threshold crossing. | Agent | **Per-step: blocks the infrastructure step** | **RESTATED per R2c-F13/R2b-F4**: the grid element persists, so scroll/`dataProvider` state survives; the real residuals are (a) sorter connectivity when wide columns hide (§3.4 bar mechanism), (b) filter-state mirroring between the bar and the wide `headerRenderer` inputs — two UIs over one debounced filter state (`searching-requests-started`, `page-monitor-requests.ts:435-473`), and (c) `auto-width` recalculation on return to wide. All three are SC10 assertions. |
| **U-15** | Row-activation semantics vs the disclosure default. | Agent | **Per-step: blocks the JIT Specs of steps 1 AND 4** *(extended per R2c-F10)* | Open. **Both** journey views open the result page on activation — step 1 via in-cell `.id-btn` (`:670`), step 4 via `@active-item-changed` dispatching `open-monitor-result` (`env-monitor.ts:620-636`) — while §3.4 assigns activation to disclosure. Default: primary line carries the open-result link; activation toggles disclosure. Each view's template must state it. |
| **U-16** | Runtime a11y assertion mechanism: none exists (`eslint-plugin-lit-a11y` is static; `grid-menu-a11y` declined to add a harness) and §4 forbids new test frameworks. | **User** | **Blocks §6 step 6 (the a11y gate)** | Open — **a permission question, not a discovery** *(R2c-F12)*: approve a runtime assertion library (e.g. axe-core in the existing Vitest browser harness) or accept manual-only verification of SC5. Decision requested at this document's approval checkpoint. |
| **U-17** | Direction. | User | ~~YES~~ | **RESOLVED 2026-08-02**: §3.4. GitHub-style list rows inside Vaadin Grid; quality bar part of the resolution. |
| **U-18** | Is there a rendering defect at 375px? | Agent | ~~YES~~ | **RESOLVED 2026-08-02 — YES, twice-measured.** 7 of 10 views overflow (both journey views); revised after R2b audit corrected the content fidelity and withdrew the "3-column ceiling" claim; the cheap width fix measured honestly truncates ~54% of the identity field (U-18c). `MEASURE-U-18-narrow-overflow.md`. |
| **U-19** | Rollback for a migrated view regressing in production. | User/Agent | **Constraint on every migration step** *(routed per R2c-F12)* | Open. Candidates: per-view revert commit discipline; the row-template module makes a one-commit revert per view realistic. |
| **U-20** | Idiom rule for new grid-bearing views post-pilot. | User | NO | Default restated *(R2c-F15)*: new views must ship a row-template module and the controller; `?hidden` bindings are not accepted in review. |

---

## 8. Risks

| Risk | Severity | Mitigation |
|---|---|---|
| The personas are anticipated, not observed (U-1); field priorities are set from assumptions. | MEDIUM–HIGH | Accepted as foundation-laying. Real mitigations: priority lives in one row-template module per view (revision is an edit, not a re-migration); pilot bounded to six views; SC9 is a rendering-completeness gate and is **not** claimed as usage validation. Follow-up commitment: a lightweight session with a real on-call engineer after the pilot ships, tracked at the post-pilot checkpoint. |
| Sort silently drops from queries at narrow width (verified Vaadin behaviour when columns hide). | **HIGH** | §3.4's header-hosted bar keeps sorters connected; SC10 asserts `sortOrders` continuity; fallback mechanism named. The infrastructure step spikes this **first** — it is the highest technical risk in the design. |
| The bar (one surface) and wide headers (another) desync filter state. | MEDIUM | U-14(b); SC10 assertion; single debounced filter-state owner already exists in the views. |
| Accessibility of the row template's internal semantics (chip, disclosure, actions menu, bar). | MEDIUM–HIGH | SC5 decomposed and authored before the renderer step; Vaadin's table semantics and row-details `aria-expanded` retained by construction; U-16 decides the assertion mechanism. |
| The design bar is subjective; "polished" drifts. | MEDIUM | SC11's objective contract + named-reference side-by-side with authority to fail the pilot. |
| Test-coverage regression during migration. | MEDIUM | §3.1 test migration in scope; SC2 pre-classification; coverage re-established against controller/template. |
| Row-template modules less readable than the markup they replace. | MEDIUM | §4 adjudication at the pilot gate with authority to fail. |
| SC6 consolidation touches `ResponsiveMixin` (25 appliers) during a six-view pilot. | MEDIUM | SC6 sequenced as its own step; `responsive-mixin.test.ts` + `grid-column-hiding.test.ts` as regression gate; unmigrated views keep the mixin untouched. |
| Partial migration becomes permanent. | MEDIUM | U-20 governs new views; post-pilot sequencing decision escalated to the user. |
| Mobile network flakiness during incidents stalls incremental fetch. | LOW–MEDIUM | Error-state UX for failed `dataProvider` fetches in the list rendering. |
| `ResizeObserver` churn from container measurement. | LOW | Bounded-callback test; step 5 exercises it. |

**Sequencing (binding on the IS)** *(restated per R2c-F11)*: infrastructure
step first (controller + template helper + bar, spiking the sort-connectivity
risk, validated against step 1's row template as the worked example) → step 1 →
step 5 (container-width) → step 6 (declarative sort + edit actions) → step 3 →
step 4 (activation semantics) → step 2's layout work alongside SC9 preparation.
Width-attribute cleanup (§3.1 last bullet) and SC6 consolidation are
independent steps schedulable in parallel.

---

## 9. Alternatives Considered

### 9.1 Status quo — per-column `?hidden` bindings
**Rejected.** Opt-in (the two worst views have zero handling); cannot express a
non-tabular rendering; and the measured content demand (454–764px) exceeds any
column arithmetic at 375px.

### 9.2 Replace Vaadin Grid with a headless table library
**Rejected.** Restated once more after §3.4 *(R2a-F11)*: the chosen design
incurs **no** virtualiser or grid-a11y ownership at any width — the narrow
rendering is the same grid element. A headless migration transfers both costs
across all 37 grid-rendering files. The blast-radius asymmetry is now total,
not partial. Revisit trigger: none scheduled; U-2's resolution (no tree grid,
no inline-editing expansion) removed the standing candidates.

### 9.3 Separate desktop and mobile component trees
**Rejected** at honest (pilot) scope: five hand-written card components,
against prior art (`request-status-card`, `environment-card`, `project-card`).
Fails §1.1's structural requirement — priority stays undeclared — and the
near-duplicate steps 1/4 show divergence arises even without two trees. The
row-template module is the smallest artifact that fixes the structural defect.

### 9.4 Width-attribute cleanup alone
**Rejected as sufficient; adopted as a component** (§3.1 last bullet). MEASURE
§4a/§4c: removes hundreds of px on the worst-authored views, but forcing the
journey views' surviving columns into 375px truncates ~54% of the identity
field. The cheap fix pays in truncation of exactly the content triage needs.

### 9.5 Row-details progressive disclosure within Vaadin Grid
**CHOSEN — as elevated by §3.4.** The bare chevron-on-a-table form
(`page-projects-audit.ts:198-233`) is the mechanical proof-of-concept, not the
target; §3.4's list-row presentation is the target. The original evaluation
(virtualiser/a11y/dataProvider retained by construction) stands and is why the
direction won.

---

## 10. References

- **Code (at `origin/main`, `9be20dd`)** — figures per the binding counting method:
  - `helpers/responsive-mixin.ts` — `NARROW_BREAKPOINT = 768`; 25 appliers.
  - `components/dorc-app.ts:207,221,320-322,380` — forked viewport subscription
    (legitimately viewport-based; not merged by SC6, only deduplicated).
  - `router/style-registrations.ts` — 110 lines; grid styling moved to
    `cellPartNameGenerator` (31 refs / 11 files, none in the pilot).
  - `pages/page-projects-audit.ts:198-233` — §9.5 mechanical prototype.
  - `components/make-like-production-dialog.ts:91-96,126` — dialog-hosted grid
    (`dialogRenderer` binding at `:126`).
  - `pages/page-monitor-requests.ts:581-602` (`detailsRenderer`), `:660-685`
    (`.id-btn`), `:711-754` (header-hosted connection/refresh controls),
    `:435-473` (debounced filter state).
  - `components/environment-tabs/env-monitor.ts:149,620-636` — activation
    opens result.
  - Vaadin 25.2.6 sources verified: `vaadin-grid-column-mixin.js:818-821`
    (default `width: '100px'`), `:527-540` (`_hiddenChanged` removes content),
    `vaadin-grid-sort-mixin.js:170` (`isConnected` filter),
    `vaadin-grid-mixin.js:598-637` (header-row visibility),
    `vaadin-grid-a11y-mixin.js:48-81,104-116`.
  - `tests/components/responsive-grids.test.ts:100,171`; `vitest.config.ts:22-29`.
- **Measurement**: `MEASURE-U-18-narrow-overflow.md` + `tests/measurements/u18-narrow-overflow.measure.ts`.
- **Prior related work**: `docs/grid-menu-a11y/`, `docs/request-grid-perf/`.
- **Conventions**: `CLAUDE.md`; HLPS pattern per `docs/monitor-robustness/`.

---

## 11. Status / Review Trail

| Round | Date | Status | Reviewers | Outcome |
|---|---|---|---|---|
| — | 2026-08-02 | DRAFT | — | Authored; U-1/U-2 blocking. |
| — | 2026-08-02 | DRAFT | — | U-1, U-2 resolved by user. |
| **R1** | 2026-08-02 | REVISION | 6 reviewers, 4 lenses | **6/6 REVISE** — census error (~2.5× inflation) + 20 further findings; triage §12. |
| — | 2026-08-02 | REVISION | — | U-18 resolved by measurement; U-17 resolved by user (GitHub-style list direction). |
| **R2** | 2026-08-02 | REVISION | 3 reviewers (triage-implementation audit; new-content/measurement audit; IS-readiness) | **3/3 REVISE** — R1 triage verified genuinely implemented (20+ figures independently re-derived, all matched), but: multi-pass edit debt (14 consistency defects), measurement's "3-column ceiling" claim false (default ≠ floor), §3.4 contract missing actions/sort/header-controls/chip answers, sort-loss-at-narrow-width defect found in the design. Triage §13. |
| — | 2026-08-02 | REVISION | — | **This version**: single-pass rewrite implementing the full R2 triage. Measurement corrected and extended (U-18c); §3.4 expanded to the normative contract; U-8a resolved; all stale text swept. **Next: R3 verification round (final permitted round), then user approval.** |

---

## 12. R1 Adversarial Review — Triage (abridged; verified by R2a)

R1 (6/6 REVISE) found: string-count census inflation (~2.5×, four reviewers
independently); no dialog-hosted pilot member despite the claim; a pilot view
with no grid; descriptor model missing filter/header-renderer/row-activation/
part-name fields; §9.2 self-refuting; narrow-path virtualisation unaddressed;
SC2/SC5 unmeasurable; SC9 absent from §6; §1.2 inference invalid; U-8
non-blocking only by framing. **All findings accepted or downgraded; none
rejected.** R2a subsequently re-derived 20+ corrected figures and confirmed
every one, and verified each accepted finding was genuinely implemented (with
the sweep exceptions that became R2's own findings).

## 13. R2 Adversarial Review — Triage

Panel: R2a (triage-implementation audit), R2b (measurement + §3.4 soundness),
R2c (IS-readiness). 3/3 REVISE.

### Accepted — implemented in this version

| Finding | Disposition |
|---|---|
| R2a-F1..F14 (14 consistency defects from multi-pass editing: stale U-11/U-17/U-18/symptom/descriptor text, SC1/SC3 keyed to superseded artifact, SC4+SC11 missing from §6, a11y-inheritance contradiction, "4 grids" figure, withdrawn count still asserted, blocking-column convention, 26/28 figure, reference line numbers) | **ACCEPT — all.** Root cause was incremental patching; this version is a single-pass rewrite. Every item swept: §1.1 rewritten around the measurement; §3.1/SC8/§8/§9.4 U-11 references removed; §4/§8 a11y posture corrected; SC1/SC3 rekeyed; §6 now maps all eleven criteria; step 5 recorded as one grid / 3 columns with derivation; §3.1's dialog count replaced by "at least one, in the pilot"; U-17/U-18/U-8a Blocking cells struck; SC6 reads 26 files / 28 occurrences; §10 cites `:91-96,126`. |
| R2b-F1/F2 ("3-column ceiling" false — 100px is a default, not a floor; cheap-fix refutation unsupported) | **ACCEPT.** Verified against Vaadin source before acting. MEASURE rewritten: §4b restated as default-width characterisation; new U-18c experiment measures the cheap fix honestly — fits only by truncating ~54% of the identity field. Refutation restated on truncation grounds (§9.4 here). |
| R2b-F3 (SC1 oracle cannot detect the measured defect) | **ACCEPT.** SC1 rekeyed to the grid's internal scroller. |
| R2b-F4 / R2c-F2 (sort loss at narrow width; no sort surface for declarative views) | **ACCEPT — the most valuable finding of the round.** Verified `_hiddenChanged`/`isConnected` in Vaadin source. §3.4's bar mechanism (header-hosted, sorters stay connected) designed around it; fallback named; SC10 asserts continuity; top §8 risk; infrastructure step spikes it first. |
| R2b-F5 / R2c-F1 (no actions slot) | **ACCEPT.** Actions slot added: trailing overflow menu reusing the `grid-menu-a11y` pattern, ≤1 promoted inline; U-7 rescoped to `request-controls` + `edit-comments-controls`. |
| R2b-F6 / R2c-F6 (chip mis-cites `cellPartNameGenerator`; slot optionality) | **ACCEPT.** Chip restated as new item-model-driven work; part-name generation kept as wide-path obligation; slot table carries required/optional and the full pilot mapping. |
| R2b-F7/F8/F9/F13/F14/F15 (measurement fidelity, limitations, reproduction, invented flex-grow, CI-glob leak, missing step-5 measurement) | **ACCEPT.** Harness rewritten: renamed `*.measure.ts` (out of CI glob), faithful flex-grow, corrected Details content (flagship figures revised −96px/−96px, verdict unchanged), step 5 added, limitations expanded, chromium-only stated. |
| R2b-F10 (SC11 automatability; wrong WCAG criterion) | **ACCEPT.** SC11 asserts against the template helper's part/attribute contract; WCAG 2.5.8 (AA) replaces 2.5.5. |
| R2b-F11 (wrapping is opt-in; auto-width recalc on crossing) | **ACCEPT.** §3.4 styling notes; SC10 assertion. |
| R2b-F12 (list-vs-table ARIA unreconciled) | **ACCEPT.** §3.4/SC5 explicitly retain one-column-table semantics. |
| R2c-F3 (artifact shape undecided) | **ACCEPT.** §3.4 names the three deliverables (template helper, per-view module, controller); no wrapper element; §3.1 rewritten accordingly. |
| R2c-F4 (threshold unstated; 375–768 band) | **ACCEPT.** 768px pinned with derivation (worst measured demand 764px); band gets the list by design; unconditional collapse stated (R2c-F14). |
| R2c-F5 (SC3 forbids the natural mechanism; ResponsiveMixin disposition) | **ACCEPT.** Controller replaces the mixin in migrated views; SC3 measures the module + absence of `_narrowScreen` in the view. |
| R2c-F7 (header-hosted interactive controls silently lost) | **ACCEPT.** Bar gains a view-level controls region; SC11 asserts no wide-width affordance unreachable at narrow. |
| R2c-F8 (SC9's step-2 work unowned) | **ACCEPT.** §3.1 bullet anchors `page-monitor-result` layout work. |
| R2c-F9 (U-8a should be resolved in the HLPS) | **ACCEPT.** Resolved: "no" under the 4-slot contract, "yes" under §3.4 as expanded; the mapping is the slot table. |
| R2c-F10 (U-15 named the wrong view; step 4's justification voided) | **ACCEPT.** U-15 extended to both journey views; step 4 rejustified against the still-live activation-semantics risk. |
| R2c-F11/F12/F13 (§8 stale; blockers unrouted; U-14 wrong premise) | **ACCEPT.** §8 rewritten with binding sequencing; every open unknown routed; U-14 restated as the three real residuals. |
| R2c-F15/F16 (stale U-6/U-20 defaults; step 5 unmeasured) | **ACCEPT.** Defaults restated; step 5 in the measurement set. |

### Deferred / noted

- **R2c-F12 (U-16 resolvable now)**: partially — it is a *user* permission
  decision; the register now says exactly that and requests the decision at
  the approval checkpoint, but this document cannot make it.

### Rejected

- **None.**

### Verified-sound by the panel (for the record)

R2a: all 20+ re-derived figures matched; R1 triage genuinely implemented.
R2b: transcriptions faithful; U-18=YES survives correction; header-hiding
mechanism confirmed feasible via public API; U-11 dissolution correct;
row-details `aria-expanded` free. R2c: pilot composition sound; SC2/SC5 oracles
"stronger than anything in either reference IS"; filter relocation tractable on
the existing debounced event contract.
