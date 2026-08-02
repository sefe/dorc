# HLPS — Responsive Record View (DORC-owned grid presentation layer)

| Field | Value |
|---|---|
| Status | **REVISION** — R1 adversarial panel returned 6/6 REVISE; findings triaged in §12 |
| Date | 2026-08-02 |
| Owner | Ben Hegarty |
| Topic slug | `responsive-record-view` |
| Origin | User observation: "we're about to move past the capabilities of vaadin… we need something more specific to DORC so we can render better in both large screens and small phone screens" |
| Component | `dorc-web` (frontend only) |
| **Direction** | **RESOLVED — §3.4: Vaadin Grid retained; narrow width renders GitHub-style single-column list rows via row templates. U-17, U-18 closed. Remaining blockers are per-step (see §7).** |

> **Counting method (binding for every figure in this document).** Column counts
> are **opening tags only**: `grep -o '<vaadin-grid-column\|<vaadin-grid-sort-column'`.
> The DRAFT of this document counted raw string occurrences, which include closing
> tags and `import` lines, inflating every column figure by ~2.5×. Any figure
> added later must state its derivation inline or it is inadmissible.

---

## 1. Problem Statement

`dorc-web` presents most of its data through Vaadin Grid. As of `origin/main`
(`9be20dd`), **61 files import `@vaadin/grid`, of which 37 actually render one**
(the other 24 import types or side effects only). Those 37 files declare **180
columns** — 116 `<vaadin-grid-column>` and 64 `<vaadin-grid-sort-column>` — every
one written as markup inside a `render()` method.

### 1.1 The mobile symptom

The current responsive mechanism is `src/dorc-web/src/helpers/responsive-mixin.ts`
— a mixin exposing a reactive `_narrowScreen` boolean driven by
`matchMedia('(max-width: 768px)')`. **25 classes** apply it, and each independently
decides which columns to suppress via `?hidden="${this._narrowScreen}"`.

Corrected census — the views with the most columns surviving at ≤768px:

| View | Columns | Hidden when narrow | Remaining on a phone |
|---|---|---|---|
| `pages/page-servers-audit.ts` | 6 | **0** | **6** |
| `pages/page-databases-audit.ts` | 6 | **0** | **6** |
| `components/environment-tabs/env-deployments.ts` | 5 | **0** | **5** |
| `components/add-edit-access-control.ts` | 5 | **0** | **5** |
| `pages/page-env-history.ts` | 9 | 5 | 4 |
| `pages/page-monitor-requests.ts` | 7 | 3 | 4 |
| `components/environment-tabs/env-monitor.ts` | 7 | 3 | 4 |
| `pages/page-environments-list.ts` | 8 | 6 | **2** |

**What the corrected numbers do to the argument.** The DRAFT claimed 13–15
columns surviving on a phone and concluded "that is a horizontally-scrolling
table". That claim was false. The true worst case anywhere in the codebase is
**6 columns**, and the views the DRAFT called "largest" (`page-env-history`,
`page-environments-list`) turn out to be among the *best* handled at narrow width.

The severity of the rendering symptom is therefore **unestablished**. Six
custom-rendered columns at 375px may or may not overflow — several pilot columns
render composite content (the `Details` column composites Project, Environment
and BuildNumber into one cell; `component-deployment-results` renders log
excerpts). Whether this constitutes a defect is now an open question resolved by
measurement, not by column count — see **U-18**.

**What survives the recount unchanged** is the second failure, which is
structural rather than quantitative:

- **The policy is opt-in, so coverage gaps are guaranteed.** `page-servers-audit.ts`
  and `page-databases-audit.ts` carry zero narrow-screen handling — they are the
  two worst-rendering views precisely *because* nothing obliged their authors to
  opt in. `env-deployments.ts` and `add-edit-access-control.ts` are the same.
- **The policy is unreviewable.** No artifact answers "which fields matter on a
  phone" for any DORC entity. That decision is distributed across 180 column
  declarations and drifted the moment a second developer touched a grid.
- **The breakpoint is forked three ways.** `components/dorc-app.ts` imports
  `NARROW_BREAKPOINT` but reimplements the `matchMedia` subscription itself
  (`:207`, `:221`, `:320-322`, `:380`); separately **26 files** hardcode
  `@media (max-width: 768px)` in component CSS.

This structural argument, not the column count, is what the document now rests on.

### 1.2 Scope survey: which Vaadin Grid capabilities DORC actually uses

*(The DRAFT titled this section "The premise that does not hold" and used it to
argue that Vaadin's ceiling does not bind. That inference was invalid — low usage
of a capability is equally consistent with "we never needed it" and "we wanted it,
found it costly, and worked around it", and this document presents no artifact
that discriminates. The table is retained for what it legitimately establishes: a
**bound on the feature surface** a Vaadin-delegating wide path must reproduce,
which is what sizes SC2. The build-on-Vaadin decision now rests on the
replacement-cost argument in §9.2, which does not depend on this census.)*

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
(`router/style-registrations.ts`), but that module's own header records that grid
cell styling **migrated out** to `cellPartNameGenerator` + `::part()` during the
Vaadin 25 upgrade — **31 references across 11 files**. The descriptor model must
therefore carry part-name generation, or migrated views lose their cell styling.

Neither prior grid work programme was blocked by a Vaadin capability limit:
`docs/request-grid-perf/` addressed non-SARGable SQL predicates;
`docs/grid-menu-a11y/` resolved *in favour of* Vaadin's `active-item-changed`.

---

## 2. Goal

Introduce a DORC-owned presentation layer that treats a view's columns as **data**
rather than markup, and renders that column set in a form appropriate to the
available width — delegating to Vaadin Grid at desktop widths, and rendering a
non-tabular form at phone widths — so that responsive policy is declared once per
entity and is reviewable in one place.

**Direction resolved (U-17, 2026-08-02):** the goal is delivered *inside* Vaadin
Grid, not beside it. At narrow width the grid renders a GitHub-style
single-column list from a per-view **row template** — the slim, reviewable
residue of the descriptor idea — and at wide width it remains the ordinary
multi-column grid. Full specification in §3.4.

The target users are **application support and on-call engineers** (U-1), and the
target situation is incident triage away from a desk. This is a foundation laid
ahead of demand rather than a response to observed usage — see §8.

---

## 3. Scope

### 3.1 In Scope

> **Supersession note (post-U-17).** The full column-descriptor model below is
> **superseded by the §3.4 row-template contract** — a slimmer artifact carrying
> the same structural obligation (per-entity field priority, declared once,
> reviewable). The field-set enumeration is retained because the panel's
> findings embedded in it (filter/header-renderer coverage, interactive
> renderers, part-name generation, pathless columns) transfer to the
> row-template contract and the IS must honour them there.

- **A column-descriptor model.** Minimum field set — expanded from the DRAFT after
  the panel found three load-bearing omissions:
  - field path, header text, presentation priority, width behaviour
  - **cell renderer** (117 `.renderer=` across 47 files)
  - **header renderer / filter affordance** — in the pilot views *all* filtering
    and sorting UI lives inside `headerRenderer`s (**53 across 16 files**), a
    category the DRAFT never counted and a card rendering has nowhere to put
  - **sort path and sort-control placement**
  - **row-activation semantics** — the pilot views use two *incompatible*
    mechanisms (see U-15)
  - **cell part-name generation** (`cellPartNameGenerator`, 31 refs / 11 files)
  - a **priority vocabulary** that can rank pathless columns: action columns
    (`request-controls`), and composite columns (`Details` = Project +
    Environment + BuildNumber in one cell). A scheme keyed on "field" cannot rank
    these.
- **A responsive record-view component** consuming that model, selecting a
  rendering strategy by **container** width (not viewport — 13 dialog-hosted
  grids exist, see §4):
  - **Wide** — delegate to `<vaadin-grid>`, emitting columns from the descriptors.
  - **Narrow** — a non-tabular rendering with priority-ranked fields, progressive
    disclosure for the remainder, **a filter and sort surface**, and a **bounded
    DOM strategy** (see U-11).
- **Narrow-width filter and sort affordances** — promoted from an unstated
  assumption to an explicit deliverable. SC9 is unachievable without them.
- **Integration with `grid-button-groups/`** (12 components). U-7 has partly
  resolved: only 4 reference a grid or `GridItemModel`; the pilot depends on
  `request-controls` (which carries `vaadin-grid` and `vaadin-grid-cell-content`
  selectors in its own styles), `server-controls` and `database-env-controls`.
- **Preservation of the lazy-loading contract** (13 files, 23 `clearCache` sites)
  in the wide rendering, and a **named** virtualisation strategy for the narrow
  rendering (U-11).
- **Test migration.** Existing suites assert the idiom this work removes —
  `tests/components/responsive-grids.test.ts` asserts responsive CSS on
  `ComponentDeploymentResults` and `EnvMonitor` (both pilot views);
  `tests/pages/grid-column-hiding.test.ts` and `tests/helpers/responsive-mixin.test.ts`
  encode `?hidden="${this._narrowScreen}"` as the contract. Coverage removed from
  a migrated view must be re-established against the record-view component.
- **Breakpoint consolidation**, including the `dorc-app.ts` fork and a stated
  disposition for the 26 CSS-hardcoded breakpoints.
- **An idiom decision record for new views.** §1.1's root cause is that policy is
  opt-in; without a rule for view #38 the defect is preserved, not fixed.
- **A documented per-entity field-priority decision** for each migrated view.

### 3.2 Out of Scope

- Removing `@vaadin/grid`. This HLPS retains it.
- Migrating all 37 grid-rendering views. The pilot proves the abstraction.
- Replacing any other Vaadin component.
- Backend or API changes. No change to paging, filtering or sorting contracts.
- A visual redesign (colour, typography, iconography unchanged).
- Introducing a third-party headless table library — see §9.2.
- Offline / PWA / native-app concerns. *(Ordinary mobile network flakiness during
  an incident is a different matter and is registered as a risk in §8.)*

### 3.3 Target journey and pilot set

U-1 resolved to: no current phone usage, no telemetry, deliberate foundation for
**application support** and **on-call engineers**. The target is the incident
triage journey — *a deployment has failed and I am not at my desk*.

**The DRAFT's pilot set was wrong in three ways** (panel findings, all verified):
it claimed a dialog-nested hard case it did not contain; it included a view with
no grid; and two of its four members are near-duplicates.

| Step | View | Columns | Role | Hard case |
|---|---|---|---|---|
| 1 | `pages/page-monitor-requests.ts` | 7 (3 hidden → 4) | **Migration target** | Lazy `dataProvider`; 5 header renderers carrying all filtering |
| 2 | `pages/page-monitor-result.ts` | **0 — renders no grid** | **Journey step only** | Not a descriptor test; needs page/card layout work |
| 3 | `components/component-deployment-results.ts` | 5 (2 hidden → 3) | **Migration target** | `.items` + `all-rows-visible` — *not* virtualised; 5 cell renderers |
| 4 | `components/environment-tabs/env-monitor.ts` | 7 (3 hidden → 4) | **Migration target** | Row activation via `@active-item-changed` (the `grid-menu-a11y` outcome) |
| **5 (added)** | `components/make-like-production.ts` | 4 grids | **Migration target** | **Genuinely dialog-hosted** via `dialogRenderer` in `make-like-production-dialog.ts:91-96` — the only member exercising §4's container-width constraint |
| **6 (added)** | `pages/page-env-history.ts` | 9 (5 hidden → 4) | **Migration target** | **7 declarative `<vaadin-grid-sort-column>`** — the codebase's dominant idiom, which steps 1/3/4 contain **zero** of |

Corrections applied from the panel:

- **Step 2 is not a migration target.** It declares 0 columns; the DRAFT's
  "Columns: 1" was an import line. It remains in the pilot as a *journey* step
  because SC9 passes through it, and the layout work it needs is now named in §3.1.
- **Step 5 added.** No original member was dialog-hosted;
  `component-deployment-results` sits inside `<vaadin-details>` at
  `page-monitor-result.ts:401` and merely *opens* dialogs. §4's container-width
  requirement — the most technically novel thing in this document — was otherwise
  untested by the pilot.
- **Step 6 added.** Steps 1, 3 and 4 use custom `.renderer` for every column and
  contain **0 of the codebase's 64** `<vaadin-grid-sort-column>` declarations. A
  cost estimate sampled only from 100%-custom views is unrepresentative of the
  remaining 33.
- **Steps 1 and 4 are near-duplicates** — verified identical column headers, same
  renderers, same entity, same API call. Step 4 is retained solely because it
  carries `@active-item-changed` row activation, which step 1 does not (step 1
  opens results via an in-cell `.id-btn` button). That is the only new risk it
  retires, and it is now stated as such rather than implied.

**On excluding the audit pages.** The DRAFT excluded them as "the worst offenders
in the codebase" on persona grounds. At corrected counts that framing inverts:
`page-servers-audit.ts` and `page-databases-audit.ts` are the *genuinely* worst
remaining-on-phone views (6 columns, zero handling), and they are lazy
`dataProvider` views — so excluding them is not excluding easy cases. The
exclusion now rests on the persona argument alone, which is where it belongs, and
reviewers should judge it on that basis.

### 3.4 Chosen direction and design bar (U-17 resolution)

The user resolved U-17 for the §9.5 direction — stay inside Vaadin Grid — with a
binding quality reservation: the existing `page-projects-audit` pattern (a
chevron column appended to an otherwise unchanged table) is mechanically sound
but **visually below the bar**. The target presentation is the GitHub-mobile
list idiom.

**What that means concretely at narrow width (≤ breakpoint):**

- The grid collapses to a **single column**. The table header row is hidden —
  a one-column list has no meaningful column headers.
- Each row is rendered by a **row template** as a stacked list item:
  - **primary line** — the record's identity (e.g. request id + project),
    typographically dominant;
  - **metadata line** — muted, secondary fields inline (e.g. environment,
    requested time, user);
  - **status chip** — the existing `cellPartNameGenerator` success/failure
    part styling carried over as a visible chip, not a cell background;
  - **disclosure affordance** — row activation opens row details carrying the
    fields that today live in hidden columns.
- **Filtering and sorting move to a bar above the list** (the U-10 default):
  the `headerRenderer` filter inputs have no host in a one-column list, exactly
  as the panel found. GitHub's own answer — a filter/search bar over the list —
  is the model.
- **Wide width is unchanged**: the same `<vaadin-grid>` with the ordinary
  multi-column layout. One element, two presentations, no second rendering
  engine.

**What survives from the descriptor idea.** The row template is a *slim*
descriptor: each migrated view must declare, in one reviewable place, which
fields form the primary line, the metadata line, the chip, and the details
panel. That is §1.1's structural requirement — priority declared once, per
entity — at a fraction of the original model's surface. The full descriptor
model (§3.1's field-set enumeration) is **superseded**: its remaining value is
absorbed into the row-template contract, and the IS should specify that
contract, not the original model.

**Why this clears the measured constraint by construction.** U-18 established a
hard ceiling of 3 columns at 375px. A single-column list is under it by
definition, and stays under it as content grows — the failure mode becomes
vertical wrapping, not horizontal scrolling.

**Design-quality gate.** "Polished" is not automatable, so it is gated the same
way cognitive complexity is (§4): at the pilot review, the narrow rendering is
judged side-by-side against the named reference (GitHub's mobile issue/PR
list) by the adversarial panel and the owner, with explicit authority to fail
the pilot on presentation quality alone. SC11 records the objective half
(header hidden, two-line rows, chip present, tap targets); the reference
comparison covers what cannot be enumerated.

---

## 4. Constraints

- **No regression in grid accessibility.** The `docs/grid-menu-a11y/` outcomes are
  binding. The narrow rendering inherits none of them from Vaadin Grid. Note the
  pilot cannot verify all of them: the menu half of that work lives in
  `project-controls.ts`, which the pilot does not use.
- **Container-width measurement, not viewport.** Step 5 is dialog-hosted; the
  component must measure its own container. *(The DRAFT asserted "13 grid-bearing
  views render inside a dialog". That figure is not reproducible — 34 grid files
  mention "dialog", 9 contain dialog markup, 0 nest a grid in dialog markup since
  Vaadin uses imperative `dialogRenderer`. The constraint stands on step 5's
  existence; the count is withdrawn pending enumeration by name.)*
- **Cognitive complexity must not increase** (CLAUDE.md). **Adjudication method:**
  a side-by-side of one migrated view's descriptor set against its pre-migration
  `render()`, judged by the adversarial panel at the pilot gate, with the panel
  empowered to fail the pilot. *(The DRAFT deferred this to the IS, leaving a
  constraint no reviewer could apply.)*
- **Naming must comply with CLAUDE.md** — no `Manager`/`Helper`/`Service`/`Util`/
  `Common`/`Shared`. Component name unresolved (U-6).
- **Virtualisation preserved at desktop width.**
- **Incremental and reversible.** Migrated and unmigrated views coexist on `main`
  indefinitely. No flag-day cutover.
- **Vaadin version fixed** at `^25.2.6`.
- **Existing test tooling.** Vitest with **real browsers via Playwright**
  (chromium, firefox, webkit — `vitest.config.ts:22-29`), so layout-dependent
  assertions are feasible. Type-checking via `npm run type-checking`. **No new
  test framework** — which collides with SC5, see U-16.

---

## 5. Success Criteria

| ID | Criterion | Measurement |
|---|---|---|
| **SC1** | At 375px, a migrated view's **page body** does not scroll horizontally. Designated inner regions (log excerpts, monospace output) may scroll internally and are exempt when declared in the view's descriptor set. | Automated overflow assertion in the Playwright browser harness at 375×667, plus manual check. |
| **SC2** | At desktop width, a migrated view is functionally equivalent pre/post for sorting, filtering, resizing, reordering, row actions and lazy paging. | **Against a written per-view behavioural checklist authored in that view's JIT Spec and approved before migration begins.** Existing suites are pre-classified in the same spec as behaviour-asserting (must pass) or idiom-asserting (must be replaced, per §3.1 test migration). No post-hoc reclassification. |
| **SC3** | Field-presentation priority is declared once per entity and locatable without reading `render()`. | Regex on `_narrowScreen` (quote-agnostic — 5 bindings use single quotes), plus the descriptor set existing as a named, importable artifact. |
| **SC4** | Lazy views retain incremental fetch in both renderings. | Network-call assertion: scrolling issues incremental `dataProvider` calls; initial load does not fetch the full collection. |
| **SC5** | Keyboard operability and screen-reader semantics hold in both renderings. | **Decomposed into per-behaviour criteria at `grid-menu-a11y` §4 granularity, authored before the renderer step**: focus order within a card, `aria-expanded` on disclosure toggles, label/value association, list-vs-table semantics and position announcement, keyboard reach of row actions, live region for incrementally fetched rows, WCAG 2.5.5 touch targets. Verification mechanism is **blocked on U-16**. |
| **SC6** | The 768px breakpoint value appears in exactly one module, and viewport-subscription and container-measurement are separately testable mechanisms. | Static review covering `matchMedia` **and** the 26 CSS-hardcoded `max-width: 768px` occurrences. `dorc-app.ts` legitimately needs viewport width; that is not a fork to merge. |
| **SC7** | The pilot includes ≥1 lazy `dataProvider` view, ≥1 filter-bearing view, ≥1 **dialog-hosted** view, and ≥1 predominantly-declarative `sort-column` view. | Satisfied by the §3.3 set **as revised** (steps 1/4, 1/4, 5, 6 respectively). It was **not** satisfied by the DRAFT set. |
| **SC8** | No new runtime dependency without explicit approval. | `package.json` diff. Note U-11 may require `@vaadin/virtual-list`, which would exercise this clause. |
| **SC9** | The on-call triage journey is completable at 375×667: locate a named failed request, open it, identify the failed component, read its failure reason. | Scripted walkthrough against a **seeded fixture** (named request, defined dataset). **This is a rendering-completeness gate, not usage validation** — see §8. Pass/fail recorded; executed by someone other than the U-3 priority author. |
| **SC10** | Sort order, active filters and scroll position survive a container crossing the breakpoint in both directions. | Automated test driving container width across the threshold. |
| **SC11** | The narrow rendering meets the §3.4 design bar. | Objective half, automated where possible: header row hidden at narrow width; rows render primary + metadata lines; status chip present; filter bar reachable; WCAG 2.5.5 tap targets. Subjective half: side-by-side comparison against the named reference (GitHub mobile issue/PR list) at the pilot review, owner + panel, with authority to fail the pilot on presentation quality alone. |

---

## 6. Verification Approach

Each success criterion maps to a step. *(The DRAFT omitted SC3, SC6, SC7, SC8 and
— most seriously — SC9, the criterion it named its own primary gate.)*

1. **Baseline capture** (SC2) — per-view behavioural checklist recorded pre-change;
   affected test suites enumerated (`responsive-grids.test.ts`,
   `grid-column-hiding.test.ts`, `responsive-mixin.test.ts`,
   `page-monitor-requests.test.ts`).
2. **Component-level tests** — descriptor model and renderer selection, including
   container-width measurement (U-12: no existing pattern; current tests mock
   `matchMedia`, which does not apply to container measurement).
3. **Per-view behavioural comparison** (SC2) against the approved checklist.
4. **Narrow-viewport verification** (SC1) — automated overflow assertion + manual.
5. **Accessibility pass** (SC5) — decomposed criteria; keyboard traversal at both
   widths; **manual screen-reader spot-check**, following `grid-menu-a11y` §5.
6. **Breakpoint-crossing test** (SC10).
7. **Static / diff review** (SC3, SC6, SC7, SC8).
8. **Scripted triage walkthrough** (SC9) — seeded fixture, 375×667, executed by a
   non-author, pass/fail recorded.
9. **Type and lint gates** — `npm run type-checking`, `eslint`.

---

## 7. Unknowns Register

A blocking unknown halts entry into the Delivery Loop until resolved.

| ID | Description | Owner | Blocking | Status / Resolution |
|---|---|---|---|---|
| **U-1** | Is DORC used on phones, and by whom? | User | YES | **RESOLVED 2026-08-02.** No current usage, no telemetry. Deliberate foundation for application-support and on-call personas. Anticipated, not measured — consequence carried into §8 and SC9. |
| **U-2** | Is inline cell editing or lazy tree grid on the roadmap? | User | YES | **RESOLVED 2026-08-02.** Tree grid: no. Inline editing: not required beyond today's capabilities. **Evidence correction:** the DRAFT cited `selectedItems = 0` as evidence about editing — that measures row *selection* and is a category error. In-cell editing does exist today (`add-edit-access-control.ts:531-543,589-599,630-640` checkbox cells with `checked-changed`; `edit-comments-controls.ts` edit/save/cancel). The resolution stands; the descriptor model must accommodate an **interactive** cell renderer, not only a display renderer. |
| **U-17** | **DIRECTION: is the descriptor model the right response, or does the row-details alternative (§9.5) suffice?** | **User** | **YES — blocks the IS** | **RESOLVED 2026-08-02 by the user.** Direction is **§9.5 — stay inside Vaadin Grid — with an explicit quality reservation**: the existing `page-projects-audit` implementation "looks cheap", and the target is a polished presentation "along the lines of how GitHub handles it". That reservation is design-shaping, not cosmetic, and it is specified in **§3.4**: at narrow width the grid does not remain a shrunken table with a chevron column — it collapses to a **single-column list whose rows are rendered as stacked list items** (primary line, metadata line, status chip, disclosure), GitHub-mobile style, with filtering relocated to a bar above the list. This synthesis retains Vaadin's virtualiser, `dataProvider` contract and grid a11y (the §9.5 advantages) while requiring per-view row templates (a slim residue of the descriptor idea — see §3.4). Consequences for other unknowns: **U-11 dissolves** (the virtualiser is Vaadin's); **U-15 dissolves** (`active-item-changed` remains available); **U-10 remains live in narrowed form** — a single-column list has no column headers, so the filter surface must move to a filter bar, now the stated default. |
| **U-18** | **Is there a rendering defect at all at 375px?** With the census corrected, no measurement establishes that 4–6 custom-rendered columns overflow a phone viewport. | Agent | **YES — blocks the IS** | **RESOLVED 2026-08-02 — YES.** Measured in the project's Playwright harness: **7 of 9 views overflow at 375px**, including both pilot grid views on the on-call journey (`page-monitor-requests` +235px, `env-monitor` +175px). Worst is `page-env-history` at +389px. Full method, results and limitations: **`MEASURE-U-18-narrow-overflow.md`**. Two structural findings: (a) 94% of `page-env-history`'s excess comes from three hardcoded width attributes, not column count — width policy is a separable contributor; (b) with width policy relaxed, required width converges to exactly 100px/column (Vaadin's default minimum), so **at 375px a grid fits at most 3 columns** regardless of how widths are declared. This refutes "just fix the width attributes" as a complete answer and gives the design a hard target: **≤3 columns at 375px**. |
| **U-3** | Field-priority ranking per incident-response entity. | User | Per-step | Open, narrowed by U-1. Framing: *what does an on-call engineer need to triage at 375px?* Must also rank pathless action columns and composite columns (§3.1). |
| **U-4** | Is one breakpoint sufficient, or is a tablet tier needed? | Agent / User | NO | Open. Default: model admits >2 tiers, implement two. |
| **U-5** | Pilot set composition. | Agent / User | NO | **REVISED** per §3.3 — steps 5 and 6 added, step 2 reclassified. Pending user confirmation at the IS checkpoint. |
| **U-6** | Component and module naming. | User | NO | Open. Default: `dorc-column-set` (descriptors), `dorc-record-view` (renderer). |
| **U-7** | Do `grid-button-groups` components assume grid-cell context? | Agent | Per-step | **PARTLY RESOLVED.** 4 of 12 reference a grid/`GridItemModel`; 8 are grid-agnostic. But `request-controls.ts:45-58` (used by pilot steps 1 and 4) carries `vaadin-grid` and `vaadin-grid-cell-content` selectors in its own styles. Residual question scoped to `request-controls`, `server-controls`, `database-env-controls`. |
| **U-8a** | **Do the pilot views' cell renderers re-express as row templates?** *(reframed after U-17 — the target is now the §3.4 row-template contract, not the full descriptor model)* | Agent | **YES — blocks the IS** | Open. The pilot's cell renderers (`idRenderer`, `detailsRenderer`, `timingsRenderer`, status renderers) must map onto primary line / metadata line / chip / details slots. A "no" invalidates the row-template contract before any migration. Must be answered before the contract is fixed in the IS. |
| **U-8b** | Codebase-wide renderer migration cost (117 `.renderer=` / 47 files; **plus 53 `.headerRenderer=` / 16 files the DRAFT never counted**). | Agent | NO | Open. Cost estimation only. |
| **U-9** | Accepted behaviour for column resize/reorder at narrow width. | User | NO | Open. Default: unavailable, no error affordance. |
| **U-10** | **Where do filter and sort affordances live in the narrow rendering?** All pilot filtering lives in `headerRenderer`s; a single-column list has no header row. | User / Agent | **Per-step** (reclassified after U-17) | **NARROWED by §3.4.** Default resolved: a **filter/sort bar above the list**, per the GitHub reference. Residual per-step question is its concrete design (which filters surface, how sort is exposed) — blocks the filter-bar step's JIT Spec, no longer the IS. |
| **U-11** | **What bounds DOM growth in the narrow rendering?** | Agent | ~~YES~~ | **DISSOLVED by U-17.** The narrow rendering *is* `<vaadin-grid>` in single-column form — Vaadin's virtualiser and the `dataProvider` contract apply unchanged. The candidate the DRAFT listed first ("a single-column `<vaadin-grid>` with a card cell renderer") is what §3.4 adopts. |
| **U-12** | How is a dual-rendering component tested? Existing responsive tests mock `matchMedia`; §4 requires container measurement, for which no pattern exists in the suite. | Agent | NO | Open. Must be answered before the first migration step's JIT Spec. |
| **U-13** | Browser/device support matrix for the narrow rendering. | User | NO | Open. Default: the three Playwright engines already configured. |
| **U-14** | State continuity across a breakpoint crossing (sort, filters, scroll, disclosure state). Today's `?hidden` preserves it free because the grid is never torn down; a renderer swap tears down and rebuilds. | Agent | Per-step | Open. Now also covered by SC10. |
| **U-15** | **Can the chosen model express row activation?** Pilot views use incompatible mechanisms: `env-monitor.ts:149` uses `@active-item-changed`; `page-monitor-requests.ts` uses an in-cell `.id-btn` (`:670`). | Agent | **Per-step** (reclassified after U-17) | **MOSTLY DISSOLVED by U-17.** The narrow rendering stays inside Vaadin Grid, so `active-item-changed` remains available — the DRAFT's concern (no analogue in a card list) no longer applies. Residual per-step question: §3.4 assigns row activation to *disclosure* (opening details), so `page-monitor-requests`' activation-opens-result behaviour needs an explicit affordance in its row template (e.g. the id as a link in the primary line, matching its current `.id-btn`). Blocks that view's migration JIT Spec. |
| **U-16** | **Does a runtime a11y assertion mechanism exist, and is adding one permitted?** SC5 requires "ARIA assertions in tests". The project has only `eslint-plugin-lit-a11y` (static lint); `grid-menu-a11y` §5 states there is no automated a11y harness and declined to build one; §4 forbids new test frameworks. | User | **Per-step — blocks the a11y gate** | Open. SC5 is unverifiable until resolved. |
| **U-19** | Rollback path for a migrated view that regresses in production. The target users depend on these exact views *during incidents*; a broken triage view is the worst failure mode. | User / Agent | Per-step | Open. Candidates: feature flag, documented one-commit revert per view. |
| **U-20** | Which idiom must a *new* grid-bearing view use after the pilot? Without a rule, §1.1's root cause is preserved for everything outside the pilot. | User | NO | Open. Default: descriptor model mandatory for new views. |

---

## 8. Risks

| Risk | Severity | Mitigation |
|---|---|---|
| **The justification does not survive its own correction.** The programme was scoped against a symptom (13 columns on a phone) that does not exist; at 4–6 columns a cheaper option may suffice. | **HIGH** | U-17 and U-18 both block the IS. No implementation work proceeds until the symptom is measured and the direction chosen. |
| The personas are anticipated, not observed (U-1). Field priorities will be set from assumptions and not tested against real users before Delivery. | MEDIUM–HIGH | Accepted deliberately as foundation-laying. Honest mitigations: field priority lives in one descriptor per entity, so revising after real feedback is an edit not a re-migration; the pilot is bounded. **SC9 is no longer offered as a mitigation** — see below. |
| **Narrow-rendering DOM growth without virtualisation.** Two pilot views are lazy over `deploy.DeploymentRequest`, a table `request-grid-perf` established is large and growing. | **MEDIUM–HIGH** | U-11 blocks the IS. A node-count bound or recycling strategy must be named before the renderer is built. |
| Accessibility regresses in the narrow rendering, which inherits nothing from Vaadin Grid. | MEDIUM–HIGH | SC5 decomposed to `grid-menu-a11y` granularity **and authored before the renderer step**, so the criteria gate the build rather than assess it afterwards. U-16 must resolve the verification mechanism. *(The DRAFT's mitigation — "SC5 is a gate" — was circular and is withdrawn.)* |
| The abstraction is proven on easy views and breaks on a hard one late. | MEDIUM | SC7 now genuinely forces the hard cases (the DRAFT's set did not contain the dialog-hosted case it claimed). Difficulty order for the IS: step 1 (lazy + all filtering in headers) → step 5 (dialog-hosted) → step 6 (declarative idiom) → step 3 → step 4. The descriptor model is built before the first migration. |
| **Test-coverage regression during migration.** `responsive-grids.test.ts` asserts responsive CSS on two pilot views; those assertions fail by design once behaviour moves to a shared component, and SC2's carve-out would make deleting them compliant. | MEDIUM | §3.1 test migration is in scope; SC2 requires pre-classification in the JIT Spec with no post-hoc reclassification. |
| Descriptors become less readable than the markup they replace. | MEDIUM | §4 now names an adjudication method and empowers the panel to fail the pilot. |
| **SC6 consolidation touches shared infrastructure with 25 consumers during a 5-view pilot.** | MEDIUM | Sequence SC6 as its own IS step with its own verification; `responsive-mixin.test.ts` and `grid-column-hiding.test.ts` are the regression gate. |
| Partial migration becomes permanent; two idioms coexist indefinitely. | MEDIUM | Sequencing decision escalated to the user at pilot completion; U-20 governs new views so the gap does not widen. |
| Mobile network flakiness during an incident stalls incremental fetch. | LOW–MEDIUM | Error-state UX for failed `dataProvider` fetches in the narrow rendering. |
| Container-width measurement introduces `ResizeObserver` churn. | LOW | Bounded-callback test; step 5 now actually exercises it. |

---

## 9. Alternatives Considered

### 9.1 Status quo — per-column `?hidden` bindings
**Rejected.** Opt-in, so new views miss it entirely — which is precisely why the
two worst-rendering views in the codebase have zero handling. Cannot express a
non-tabular rendering.

### 9.2 Replace Vaadin Grid with a headless table library
**Rejected, on restated reasoning.** *(The DRAFT rejected it because it "would
transfer ownership of virtual scrolling and full grid keyboard accessibility to
DORC" — costs the chosen option also incurs for its narrow path, per §3.1 and §4.
As written the argument did not discriminate between the alternatives.)* The
correct argument is **blast radius**: a headless migration incurs those costs
across all 37 grid-rendering files including every desktop path; this proposal
incurs them for the narrow path of 5 pilot views. That is a real difference, and
it is the only version of this argument that holds.

### 9.3 Separate desktop and mobile component trees
**Rejected, re-evaluated at pilot scope.** *(The DRAFT rejected this at 61 views
while judging the proposal at 4 — an unfair comparison.)* At pilot scope it is 5
hand-written card components. The codebase already uses this idiom:
`request-status-card.ts` (468 lines), `environment-card.ts`, `project-card.ts` —
and §3.3 step 2 already composes `request-status-card`. The "guarantees
divergence" argument is also weakened by steps 1 and 4, which have already
diverged without two trees. It remains rejected because it does not address
§1.1's structural defect — priority stays undeclared and unreviewable — but it is
the closest competitor and the comparison is now honest.

### 9.4 Status of the §9.2 revisit trigger
U-2 resolved to no on both counts, so the trigger named in the DRAFT did not fire.
*(The DRAFT's "no further revisit is scheduled" is withdrawn: §9.2's reasoning has
changed, and U-11's virtualisation estimate is a new trigger — if the narrow path
requires DORC to own a virtualiser anyway, the blast-radius argument narrows.)*

### 9.5 **Row-details progressive disclosure within Vaadin Grid** *(added by panel)*
**CHOSEN — 2026-08-02, per U-17, in the elevated form specified in §3.4.** The
user's reservation about the existing implementation's visual quality is part of
the resolution: the bare chevron-on-a-table form of `page-projects-audit` is
explicitly *not* the target; the GitHub-style single-column list form is. The
original evaluation follows for the record.

`pages/page-projects-audit.ts:198-233` already combines `.detailsOpenedItems`,
`.rowDetailsRenderer`, a chevron column, a lazy `.dataProvider`,
`headerRenderer` filters, `multi-sort` and `frozen` — in one file, with one
renderer, shipping today. Applied at narrow width (hide all but 1–2 columns, route
the remainder to row details) it would satisfy SC1 and SC9, and satisfies SC4 and
SC5 **by construction** because it is still `<vaadin-grid>`: no second rendering
engine, no bespoke virtualiser (U-11 dissolves), no reimplemented row activation
(U-15 dissolves), no lost filter surface (U-10 dissolves), and the existing
`grid-menu-a11y` outcomes carry over.

Against it: it does not by itself make field priority declarative or reviewable —
§1.1's structural defect — and row details inside a 375px grid may be cramped. But
it is dramatically cheaper, and at the corrected column counts the case for
preferring a second rendering engine over it is unproven.

---

## 10. References

- **Code (at `origin/main`, `9be20dd`)** — all figures derived by the counting
  method stated at the head of this document:
  - `helpers/responsive-mixin.ts` — `NARROW_BREAKPOINT = 768`; applied by 25 classes.
  - `components/dorc-app.ts:207,221,320-322,380` — forked `matchMedia`.
  - `router/style-registrations.ts` — 110 lines; header records grid styling
    migrated to `cellPartNameGenerator` (31 refs / 11 files).
  - `pages/page-projects-audit.ts:198-233` — the §9.5 prototype already in production.
  - `components/make-like-production-dialog.ts:91-96` — dialog-hosted grid via `dialogRenderer`.
  - `pages/page-monitor-requests.ts:670` (`.id-btn`) vs `env-monitor.ts:149`
    (`@active-item-changed`) — divergent row activation.
  - `tests/components/responsive-grids.test.ts:100,171` — asserts responsive CSS
    on two pilot views.
  - `vitest.config.ts:22-29` — Playwright browser harness.
- **Prior related work**: `docs/grid-menu-a11y/`, `docs/request-grid-perf/`.
- **Conventions**: `CLAUDE.md`. HLPS pattern follows `docs/monitor-robustness/`.

---

## 11. Status / Review Trail

| Round | Date | Status | Reviewers | Outcome |
|---|---|---|---|---|
| — | 2026-08-02 | DRAFT | — | Authored. U-1, U-2 blocking and unanswered. |
| — | 2026-08-02 | DRAFT | — | U-1 and U-2 resolved by user. §3.3 added; SC9 added; pilot set proposed. |
| **R1** | 2026-08-02 | **REVISION** | 6 independent reviewers (4 lenses; scope and risk lenses run on two model tiers) | **6/6 REVISE.** Census error confirmed by 4 reviewers independently and verified by the author. Triage in §12. Two new blocking unknowns (U-17 direction, U-18 symptom measurement) escalated to the user. |
| — | 2026-08-02 | REVISION | — | **U-18 resolved by measurement** (`MEASURE-U-18-narrow-overflow.md`): 7 of 9 views overflow at 375px; hard ceiling of 3 columns at 375px established; "fix the widths" refuted as a complete answer. |
| — | 2026-08-02 | REVISION | — | **U-17 resolved by the user**: §9.5 direction, elevated to the GitHub-style single-column list form specified in new §3.4. Descriptor model superseded by the row-template contract. U-11 dissolved, U-15 mostly dissolved, U-10 narrowed to per-step. SC11 (design bar) added. **No IS-blocking unknowns remain except U-8a** (pilot renderers → row templates), which is answerable by inspection during IS drafting and must be resolved before the contract is fixed. **Next step: re-submit to the adversarial panel for approval (R2), then the IS.** |

---

## 12. R1 Adversarial Review — Triage

Panel: evidence/premise audit, scope & sequencing, architecture skeptic, risk &
unknowns. Scope and risk lenses were re-run on a higher model tier at user
request; both tiers reported independently and both are counted.

### Accepted — CRITICAL / HIGH

| Finding | Raised by | Disposition |
|---|---|---|
| Census counts string occurrences, not declarations; inflated ~2.5× | 4 reviewers independently | **ACCEPT.** Verified by author. All figures re-derived; counting method now binding at head of document. §1.1 severity argument withdrawn and replaced by U-18. |
| Pilot set contained no dialog-hosted grid; "satisfies SC7 in full" false | 4 reviewers | **ACCEPT.** `make-like-production` added as step 5. |
| `page-monitor-result.ts` declares 0 columns; not a migration target | 2 reviewers | **ACCEPT.** Reclassified as journey step; its layout work named in §3.1. |
| Descriptor model has no filter / header-renderer field | 3 reviewers | **ACCEPT.** Added to §3.1; U-10 raised as IS-blocking. |
| Row-details alternative (§9.5) never considered, already in repo | Architecture skeptic | **ACCEPT.** §9.5 added; U-17 raised as IS-blocking with prototype-first resolution path. |
| §9.2's rejection argument is self-refuting | 2 reviewers | **ACCEPT.** Restated as blast radius. |
| Narrow-path virtualisation unaddressed | 3 reviewers | **ACCEPT.** U-11 raised as IS-blocking; risk added at MEDIUM–HIGH. |
| U-8 non-blocking only because framed codebase-wide | 2 reviewers | **ACCEPT.** Split into U-8a (pilot-scoped, IS-blocking) and U-8b (cost, non-blocking). |
| SC9 named primary gate but absent from §6 | 2 reviewers | **ACCEPT.** Now §6 step 8. |
| SC9 is self-validating; cannot discharge the persona risk | 2 reviewers | **ACCEPT** (option iii). Restated as a rendering-completeness gate; withdrawn as a mitigation in §8; execution assigned to a non-author. |
| SC2 and SC5 unmeasurable as written | Scope reviewer | **ACCEPT.** Both given oracles; SC5 decomposed and blocked on U-16. |
| SC5's verification mechanism does not exist in the project | Risk reviewer | **ACCEPT.** U-16 raised. |
| §1.2 inference invalid (absence of evidence) | Evidence auditor | **ACCEPT.** Demoted to a scope survey; decision load moved to §9.2. |
| Row activation diverges across pilot views | Risk reviewer | **ACCEPT.** U-15 raised as IS-blocking. |
| Existing responsive tests assert the removed idiom on 2 pilot views | 2 reviewers | **ACCEPT.** Test migration in scope; SC2 requires pre-classification. |
| 61 files ≠ 61 grid views (37 render a grid) | Evidence auditor | **ACCEPT.** Corrected throughout; §9.2 cost re-argued at 37. |
| U-2 cited `selectedItems` as evidence about editing (category error); in-cell editing exists today | Evidence auditor | **ACCEPT.** Resolution stands; evidence corrected; descriptor must support interactive renderers. |
| Pilot steps 1 and 4 are near-duplicates | Scope reviewer | **ACCEPT.** Verified identical headers; step 4's sole justification now stated explicitly. |
| Pilot contains 0 of 64 declarative sort-columns | Scope reviewer | **ACCEPT.** Step 6 added; SC7 extended. |
| No rule for which idiom view #38 uses | Scope reviewer | **ACCEPT.** U-20 raised; in scope. |

### Accepted — MEDIUM

`?hidden` bindings use both quote styles (SC3 regex made quote-agnostic) ·
26 CSS-hardcoded breakpoints outside `matchMedia` (SC6 restated) ·
`registerStyles` count misleading, styling moved to `cellPartNameGenerator`
(§1.2 restated) · ResponsiveMixin appliers = 25, not 26 · unreproducible
"35/28 filter references" figures withdrawn · "13 dialog-nested views" withdrawn
pending enumeration · cognitive-complexity constraint given an adjudication
method · state continuity (U-14, SC10) · SC6 blast radius (risk added) ·
priority vocabulary must rank pathless and composite columns · rollback path
(U-19) · U-7 partly closed from inspection · browser matrix (U-13) ·
test-harness pattern for container width (U-12).

### Downgraded

- **Mobile network reliability** → risk row at LOW–MEDIUM rather than an unknown.
  Real, but it is an error-state UX concern, not a question that shapes the
  descriptor model.

### Rejected

- **None.** Every finding raised by the panel was accepted or downgraded. No
  finding was assessed as incorrect.

### Escalated to user

- **U-17 (direction)** and **U-18 (is there a symptom at all)**. Both block the
  IS. The panel did not overturn the proposal's direction — the architecture
  skeptic explicitly found it "defensible" — but with the census corrected, the
  evidence that justified preferring it over §9.5 no longer exists. That is a
  decision for the owner, not the panel.
