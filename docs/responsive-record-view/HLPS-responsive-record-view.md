# HLPS — Responsive Record View (DORC-owned grid presentation layer)

| Field | Value |
|---|---|
| Status | **DRAFT** — not yet reviewed |
| Date | 2026-08-02 |
| Owner | Ben Hegarty |
| Topic slug | `responsive-record-view` |
| Origin | User observation: "we're about to move past the capabilities of vaadin… we need something more specific to DORC so we can render better in both large screens and small phone screens" |
| Component | `dorc-web` (frontend only) |

---

## 1. Problem Statement

`dorc-web` presents almost all of its data through Vaadin Grid. As of `origin/main`
(`9be20dd`), **61 TypeScript files import `@vaadin/grid`**, declaring **273
`vaadin-grid-column` and 185 `vaadin-grid-sort-column`** elements — 458 column
declarations in total, every one of them written as markup inside a `render()`
method.

The presenting complaint is that the UI renders poorly on small screens. The
underlying condition is that **column presentation policy is expressed as markup
and duplicated per call site**, so there is no single place to express "what does
this record look like at 375px versus 2560px".

### 1.1 The mobile symptom

The current responsive mechanism is `src/dorc-web/src/helpers/responsive-mixin.ts`
— a mixin exposing a single reactive boolean `_narrowScreen`, driven by
`matchMedia('(max-width: 768px)')`. **26 files** consume it, and each one
independently decides which columns to suppress by attaching
`?hidden="${this._narrowScreen}"` to selected columns.

This produces two failures.

**First, the surviving column count is still far beyond a phone viewport.** A
census of the highest-column-count views (columns declared / columns hidden when
narrow / columns remaining at ≤768px):

| View | Columns | Hidden when narrow | Remaining on a phone |
|---|---|---|---|
| `pages/page-env-history.ts` | 20 | 5 | **15** |
| `pages/page-environments-list.ts` | 19 | 6 | **13** |
| `pages/page-projects-list.ts` | 16 | 5 | **11** |
| `pages/page-monitor-requests.ts` | 16 | 3 | **13** |
| `components/environment-tabs/env-monitor.ts` | 16 | 3 | **13** |
| `pages/page-servers-audit.ts` | 14 | **0** | **14** |
| `pages/page-databases-audit.ts` | 14 | **0** | **14** |
| `pages/page-scripts-list.ts` | 14 | 4 | 10 |
| `pages/page-daemons-list.ts` | 14 | 4 | 10 |
| `pages/page-servers-list.ts` | 12 | 3 | 9 |

Thirteen columns in a 375px viewport is a horizontally-scrolling table. The two
audit pages carrying 14 columns and **zero** narrow-screen handling demonstrate
the coverage problem directly: because the policy is opt-in per file, files
added later simply do not get it.

**Second, the policy is unreviewable.** There is no artifact that answers "which
fields matter on a phone" for any entity in DORC. That decision is distributed
across 458 column declarations, and it drifted the moment the second developer
touched a grid.

A related duplication: `components/dorc-app.ts` imports `NARROW_BREAKPOINT` from
the mixin module but reimplements the `matchMedia` subscription itself
(`dorc-app.ts:207`, `:221`, `:320-322`, `:380`) rather than applying
`ResponsiveMixin`. The breakpoint constant is shared; the behaviour is forked.

### 1.2 The premise that does *not* hold: Vaadin's feature ceiling

The originating hypothesis was that DORC has outgrown Vaadin Grid's
capabilities. Codebase evidence does not support this. DORC uses a
conservative subset of the component:

| Vaadin Grid capability | Occurrences in `src/dorc-web/src` |
|---|---|
| Tree grid (`item-has-children`) | **0** |
| Row selection (`selectedItems`) | **0** |
| Frozen columns (`frozen`) | **1** |
| Row details (`rowDetailsRenderer`) | **1** |
| Lazy `dataProvider` | 13 files |
| `resizable` | 164 |
| `auto-width` | 75 |
| Multi-sort | 29 |
| Column reordering | 25 |

Theme-layer friction is likewise low: all `registerStyles` overrides in the
application total **110 lines**, confined to
`src/dorc-web/src/router/style-registrations.ts`.

Neither prior grid-related work programme was blocked by a Vaadin capability
limit. `docs/request-grid-perf/` addressed non-SARGable SQL predicates in
`RequestsStatusPersistentSource` — a backend query-shape defect.
`docs/grid-menu-a11y/` addressed row-activation and menu ARIA semantics, and its
`DECISION-U-2.md` resolved *in favour of* Vaadin's existing
`active-item-changed` event rather than working around it.

**Restatement of the problem.** DORC has not exceeded Vaadin Grid's
capabilities. DORC has exceeded the capability of *markup-declared columns* to
express responsive presentation policy, and no third-party grid library resolves
that, because at phone widths the correct presentation is not a table at all.

---

## 2. Goal

Introduce a DORC-owned presentation layer that treats a view's columns as
**data** rather than markup, and renders that column set in a form appropriate to
the available width — delegating to Vaadin Grid at desktop widths, and rendering a
non-tabular (card/list) form at phone widths — so that responsive policy is
declared once per entity and is reviewable in one place.

The target users are **application support and on-call engineers** (U-1), and the
target situation is incident triage away from a desk. This is a foundation laid
ahead of demand rather than a response to observed usage — see §3.3 for the
journey this scopes to, and §8 for the risk that carries.

Retaining Vaadin Grid as the wide-viewport renderer is a deliberate goal, not a
compromise: it preserves virtual scrolling, the lazy `dataProvider` contract used
by 13 files, keyboard accessibility (including the `grid-menu-a11y` outcomes),
column resizing, reordering and multi-sort — the components most expensive to
reimplement correctly.

---

## 3. Scope

### 3.1 In Scope

- **A column-descriptor model.** A serialisable per-column descriptor carrying at
  minimum: field path, header text, a presentation priority (e.g.
  primary / secondary / detail), optional cell renderer, sort path/behaviour, and
  width behaviour. Exact field set is specified by the IS.
- **A responsive record-view component** consuming that model and selecting a
  rendering strategy by available width:
  - **Wide** — delegate to `<vaadin-grid>`, emitting columns from the descriptor
    set. Behaviour must be indistinguishable from today's grids for the
    capabilities listed in §1.2.
  - **Narrow** — render a non-tabular card/list form from the same descriptor
    set, surfacing priority-ranked fields with the remainder reachable via
    progressive disclosure.
- **Integration with the existing per-row action components** under
  `src/dorc-web/src/components/grid-button-groups/` (12 components), so row
  actions are available in both renderings.
- **Preservation of the lazy-loading contract.** The `dataProvider` /
  `clearCache` pattern (13 files, 23 `clearCache` call sites) must work unchanged
  in the wide rendering and must be honoured — not silently replaced by
  full-collection loading — in the narrow rendering.
- **Migration of a pilot set of views**, chosen by the IS, sufficient to prove the
  abstraction against the hardest cases (a lazy `dataProvider` view, a
  filter-bearing view, a grid nested inside a dialog).
- **Consolidation of breakpoint logic**, including resolving the forked
  `matchMedia` implementation in `dorc-app.ts`.
- **A documented per-entity field-priority decision** for each migrated view —
  the artifact that does not exist today.

### 3.2 Out of Scope

- **Removing `@vaadin/grid` from the project.** This HLPS explicitly retains it.
- **Migrating all 61 grid-bearing files.** The pilot set proves the abstraction;
  wholesale migration is sequenced by a later IS or follow-up HLPS.
- **Replacing any other Vaadin component.** `@vaadin/button` (77),
  `@vaadin/combo-box` (49), `@vaadin/dialog` (31), `@vaadin/router` (13) and the
  rest are untouched.
- **Backend or API changes.** No change to paging, filtering or sorting
  contracts. If a narrow rendering appears to need a different server-side shape,
  that is a finding to escalate, not to implement here.
- **A visual redesign.** Colour, typography and iconography are unchanged; this
  is a layout/structure change.
- **Introducing a third-party headless table library** (TanStack Table or
  equivalent). Recorded as a rejected alternative in §9.2; revisiting it requires
  a new HLPS.
- **Offline / PWA / native-app concerns.**

### 3.3 Target journey and pilot set (following U-1)

U-1 resolved to: no current phone usage, no telemetry, and a deliberate
foundation for **application support** and **on-call engineers**. That narrows
the target from "DORC on a phone" to a specific journey — *a deployment has
failed and I am not at my desk*:

| Step | View | Columns | Narrow today | Hard-case coverage |
|---|---|---|---|---|
| 1. Find the failing request | `pages/page-monitor-requests.ts` | 16 | 3 hidden → 13 shown | Lazy `dataProvider`; 35 filter references |
| 2. Open it | `pages/page-monitor-result.ts` | 1 | none | Composes `request-status-card` + drill-down |
| 3. See which component failed | `components/component-deployment-results.ts` | 13 | 2 hidden → 11 shown | **Dialog-nested** (13 dialog references) |
| 4. Same, environment-pinned | `components/environment-tabs/env-monitor.ts` | 16 | 3 hidden → 13 shown | Lazy `dataProvider`; 28 filter references |

This set satisfies SC7 in full: lazy `dataProvider` (steps 1, 4), filter-bearing
(steps 1, 4), dialog-nested (step 3).

**A deliberate reversal worth flagging to reviewers.** The §1.1 census identified
`page-servers-audit.ts` and `page-databases-audit.ts` — 14 columns each with
*zero* narrow handling — as the worst offenders in the codebase, and
`page-env-history.ts` (20 columns) and `page-environments-list.ts` (19 columns)
as the largest. **None of them are in the pilot set.** They are administrative
and compliance surfaces, not incident-response surfaces; an on-call engineer at
02:00 does not read a server audit log on a phone. The worst-rendering views and
the highest-value views are not the same views, and U-1 is what separates them.
Reviewers should challenge this if they disagree — it is the single most
consequential scoping judgement in this document.

---

## 4. Constraints

- **No regression in grid accessibility.** The outcomes of
  `docs/grid-menu-a11y/` — row activation semantics, menu ARIA, menu keyboard
  navigation — are binding. The narrow rendering must independently satisfy
  keyboard operability and screen-reader semantics; it does not inherit them from
  Vaadin Grid.
- **Cognitive complexity must not increase** (CLAUDE.md). A descriptor model that
  is harder to read than the markup it replaces is a failed design. The IS must
  state how this is judged, not assert it.
- **Naming must comply with CLAUDE.md.** `Manager`, `Helper`, `Service`,
  `Util(s)`, `Common`, `Shared` and equivalents are prohibited. Namespace pattern
  `Dorc.[Component].[Feature]` applies to .NET; the frontend equivalent is a
  cohesive custom-element name. The component name is **unresolved** — see U-6.
- **Virtualisation must be preserved at desktop widths.** Views backed by a lazy
  `dataProvider` must not degrade to loading full collections.
- **Incremental and reversible.** Migrated and unmigrated views must coexist on
  `main` indefinitely. No flag-day cutover.
- **Vaadin version is fixed** at the current `^25.2.6` line. No Vaadin major
  upgrade as part of this work.
- **Existing test tooling.** Vitest (`npm run test`); type-checking via
  `tsc --noEmit`, `tsconfig.test.json` and `lit-analyzer --strict`
  (`npm run type-checking`). No new test framework.
- **13 grid-bearing views render inside a dialog.** The component cannot assume
  it measures the viewport; available width must be measured from its own
  container, not from `window`.

---

## 5. Success Criteria

| ID | Criterion | Measurement |
|---|---|---|
| **SC1** | At a 375px viewport, every migrated view presents its records without horizontal scrolling. | Manual verification at 375×667 plus an automated width-overflow assertion in the pilot views' tests. |
| **SC2** | At desktop width, a migrated view is functionally equivalent to its pre-migration form for sorting, filtering, resizing, reordering, row actions and lazy paging. | Per-view behavioural checklist executed before/after; existing Vitest suites for migrated views pass unmodified where they assert behaviour rather than DOM shape. |
| **SC3** | Field-presentation priority for each migrated entity is declared exactly once and is locatable without reading `render()`. | Static review: no `?hidden="${this._narrowScreen}"` remains in a migrated view. |
| **SC4** | Lazy-loading views retain incremental fetch in both renderings. | Network-call assertion: scrolling a migrated lazy view issues incremental `dataProvider` calls; initial load does not fetch the full collection. |
| **SC5** | Keyboard operability and screen-reader semantics hold in **both** renderings. | Keyboard-only traversal of a migrated view at both widths; ARIA assertions in tests, consistent with the `grid-menu-a11y` acceptance criteria. |
| **SC6** | Breakpoint logic exists in exactly one module. | Static review: one `matchMedia` breakpoint subscription implementation in `src/dorc-web/src`; `dorc-app.ts` fork resolved. |
| **SC7** | The abstraction is proven against the hard cases, not only the easy ones. | The pilot set includes ≥1 lazy `dataProvider` view, ≥1 filter-bearing view, and ≥1 grid nested in a dialog. Satisfied by the §3.3 set. |
| **SC8** | No net increase in `@vaadin/*` dependencies, and no new runtime dependency without explicit approval. | `package.json` diff. |
| **SC9** | The on-call triage journey is completable end-to-end on a phone. | Scripted walkthrough at 375×667: locate a named failed deployment request, open it, identify which component failed, and read its failure reason — without horizontal scrolling and without switching to a desktop. Because U-1 resolved to *anticipated* rather than observed usage, this task-completion walkthrough is the substitute for real usage evidence, and it is the primary acceptance gate for the pilot. |

---

## 6. Verification Approach (high-level)

1. **Baseline capture** — for each pilot view, record current behaviour at
   desktop width against the SC2 checklist before any change.
2. **Component-level tests** — Vitest coverage of the descriptor model and
   renderer selection, including the container-width (not viewport-width)
   measurement required by §4.
3. **Per-view behavioural comparison** — SC2 checklist re-executed post-migration.
4. **Narrow-viewport verification** — 375px manual pass plus automated overflow
   assertion (SC1).
5. **Accessibility pass** — keyboard-only traversal at both widths, ARIA
   assertions (SC5).
6. **Type and lint gates** — `npm run type-checking` (includes
   `lit-analyzer --strict`) and `eslint` must pass.

Detailed verification per step is specified in the JIT Specs, not here.

---

## 7. Unknowns Register

A blocking unknown halts entry into the Delivery Loop until resolved.

| ID | Description | Owner | Blocking | Status / Resolution |
|---|---|---|---|---|
| **U-1** | **Is DORC actually used on phones, and by whom for what?** The entire premise rests on this. If phone usage is an on-call engineer checking deployment status, the narrow rendering needs a handful of views done well. If it is general administration, scope is far larger. Is there usage telemetry, or is this a judgement call? | User | **YES** | **RESOLVED 2026-08-02.** There is **no current phone usage and no telemetry**. The work is a deliberate foundation-laying exercise for two named personas: **application support** and **on-call engineers**. This is an anticipated-demand decision, not a measured one — recorded as such, with the consequence carried into §8 (Risks) and §5 (SC7). Scope consequence: the target is the **incident-response journey**, not administrative or compliance surfaces. See U-5. |
| **U-2** | **Is inline cell editing or lazy-loaded tree grid on the DORC roadmap?** Neither exists today (`selectedItems` 0, `item-has-children` 0). Both are cases where the build-on-Vaadin recommendation would need re-examination, because both are where Vaadin Grid's own constraints start to bind. | User | **YES** | **RESOLVED 2026-08-02.** Lazy tree grid: **no**. Inline cell editing: **not required** beyond the editing capabilities that exist today. Neither trigger fires, so the §9.2 rejection of a headless-library migration stands on its stated reasoning, and the build-on-Vaadin direction in §2 is confirmed. |
| **U-3** | **What is the field-priority ranking per entity?** Which 2–4 fields identify a Deployment Request, an Environment, a Server, a Script on a phone? This is a product decision, not an engineering one, and it is the substance of the work. | User | **Per-step** (blocks each view's migration step) | Open, **narrowed by U-1**. The ranking question is now scoped to the incident-response entities (deployment request, deployment result, component result, environment) rather than every entity in DORC. The framing question per entity is: *what does an on-call engineer need to see to triage, at 375px, without scrolling?* Does not block the IS; blocks each migration step's JIT Spec. |
| **U-4** | **Is one breakpoint sufficient?** Today there is a single 768px threshold. Tablet-width behaviour (768–1200px) is currently "desktop", which may be wrong for the 16–20 column views. Does the model need a third tier? | Agent (with user confirmation) | NO | Open. Default position: design the descriptor model to admit more than two tiers, implement two initially. |
| **U-5** | **What is the pilot set?** Must satisfy SC7. | Agent (proposes) / User (confirms) | NO | **PROPOSED 2026-08-02, pending user confirmation at the IS checkpoint.** Following U-1, the pilot set is the on-call triage journey rather than the highest-column-count views — see §3.3. Proposed: `pages/page-monitor-requests.ts`, `pages/page-monitor-result.ts`, `components/component-deployment-results.ts`, `components/environment-tabs/env-monitor.ts`. |
| **U-6** | **Component and module naming.** CLAUDE.md prohibits grab-bag names. Candidates: `dorc-record-view`, `dorc-record-table`, `dorc-column-set` (descriptor model) paired with a renderer element. | User | NO | Open. Default candidate: `dorc-column-set` for the descriptor model, `dorc-record-view` for the rendering element. Cheap to change before Delivery, expensive after. |
| **U-7** | **Do the 12 `grid-button-groups` components assume a grid cell context?** If they depend on `vaadin-grid-cell-content` slotting or grid-relative positioning, the narrow rendering needs an adaptation layer, which changes the size of the work. | Agent | NO | Open. Answerable by code inspection during IS drafting. |
| **U-8** | **Is `renderer` usage compatible with a descriptor model?** ~124 `renderer` occurrences across 50 files, many rendering Lit templates into grid cells. Whether these lift cleanly into descriptors or need per-renderer rework determines migration cost per view. | Agent | NO | Open. IS must sample several before estimating. |
| **U-9** | **What is the accepted behaviour for column resize/reorder at narrow width?** These have no meaning in a card rendering. Silently unavailable, or explicitly surfaced as desktop-only? | User | NO | Open. Default position: unavailable at narrow width, no error affordance. |

---

## 8. Risks

| Risk | Severity | Mitigation |
|---|---|---|
| The abstraction is proven on easy views, then breaks on a hard one late — most likely on a lazy `dataProvider` view with per-field filters. | **HIGH** | SC7 forces the hard cases into the pilot set. The IS must sequence the hardest view early, not last. |
| Column descriptors become less readable than the markup they replace, breaching the CLAUDE.md cognitive-complexity constraint. | MEDIUM | Adversarial review of the descriptor model at the pilot stage, before migrating beyond it. Explicit stop-and-reconsider gate at end of pilot. |
| Accessibility regresses in the narrow rendering, which inherits nothing from Vaadin Grid's keyboard and ARIA implementation. | MEDIUM–HIGH | SC5 is a gate, not a nice-to-have. `docs/grid-menu-a11y/` acceptance criteria are reused directly. |
| Partial migration becomes permanent: pilot ships, remaining ~55 views never migrate, and the codebase carries two grid idioms indefinitely. | MEDIUM | Accepted as a deliberate constraint (§4 requires coexistence). The follow-up sequencing decision is escalated to the user at pilot completion rather than assumed. |
| **The personas are anticipated, not observed.** U-1 confirmed there is no current phone usage and no telemetry. The field-priority decisions in U-3 will therefore be made from assumptions about what on-call engineers need, and those assumptions will not be tested against real users before Delivery. Wrong priorities produce a phone UI that is responsive but not useful. | **MEDIUM–HIGH** | Accepted deliberately by the user as foundation-laying. Mitigations: (a) SC9 makes a scripted triage walkthrough the primary acceptance gate, so the design is tested against a concrete task even without users; (b) field priority lives in one descriptor per entity, so revising it after real feedback is a small edit, not a re-migration — this is a direct argument for the descriptor model over per-file `?hidden` bindings; (c) the pilot stops at four views, bounding the exposure if the priorities prove wrong. |
| The pilot set (§3.3) is scoped to incident response, so the worst-rendering views in the codebase — the two 14-column audit pages with zero narrow handling — remain unimproved and visibly bad on a phone. | LOW–MEDIUM | Deliberate: they are not on-call surfaces. Accepted as a known gap, recorded here rather than silently omitted. They become candidates for the post-pilot sequencing decision. |
| Descriptor indirection makes stack traces and Lit template errors harder to diagnose than inline markup. | LOW–MEDIUM | Keep the wide path a thin emitter over `<vaadin-grid>`; avoid a bespoke rendering pipeline where a Lit template would do. |
| Container-width measurement (required for the 13 dialog-nested grids) introduces `ResizeObserver` churn or layout thrash. | LOW | Component-level test asserting bounded observer callbacks; measure in the pilot. |

---

## 9. Alternatives Considered

### 9.1 Continue with `?hidden="${this._narrowScreen}"` per column

**Rejected.** It is the status quo, and it produces 13–15 columns on a phone in
the highest-value views. It is opt-in, so new views miss it entirely (two audit
pages at 14 columns with zero handling). It cannot express a non-tabular
rendering, which is what phone widths require.

### 9.2 Replace Vaadin Grid with a headless table library (TanStack Table or equivalent)

**Rejected for now; recorded for revisit.** A headless library would supply the
column-as-data model this HLPS needs, and would remove a dependency the team is
ambivalent about. It would also transfer ownership of virtual scrolling and full
grid keyboard accessibility to DORC — the latter being work the team has already
priced once, in `docs/grid-menu-a11y/`. It requires touching all 61 files with no
incremental path, against a codebase using a conservative grid subset (§1.2) that
Vaadin serves adequately. Revisiting requires a new HLPS, and U-2 resolving to
"yes" is the most likely trigger.

### 9.3 Two parallel component trees (a desktop view and a separate mobile view per entity)

**Rejected.** Doubles the surface for all 61 views and guarantees divergence —
the same failure mode as §9.1, at larger scale. The single-descriptor-set
approach exists specifically to prevent two sources of truth.

### 9.4 Status of the §9.2 revisit trigger

§9.2 named U-2 resolving to "yes" as the most likely trigger for reopening the
headless-library option. **U-2 resolved to no on both counts** (2026-08-02): no
lazy tree grid, and no inline cell editing beyond today's capabilities. The
trigger has not fired. The build-on-Vaadin direction stands on the §1.2 evidence,
and no further revisit is scheduled.

---

## 10. References

- **Origin**: user observation, 2026-08-02, on Vaadin's fit for DORC's
  large-screen/small-screen requirements.
- **Code (at `origin/main`, `9be20dd`)**:
  - `src/dorc-web/src/helpers/responsive-mixin.ts` — `ResponsiveMixin`,
    `NARROW_BREAKPOINT = 768`; consumed by 26 files.
  - `src/dorc-web/src/components/dorc-app.ts:207,221,320-322,380` — forked
    `matchMedia` implementation.
  - `src/dorc-web/src/router/style-registrations.ts` — all 110 lines of
    `registerStyles` theme overrides.
  - `src/dorc-web/src/components/grid-button-groups/` — 12 per-row action
    components.
  - Highest-column views: `pages/page-env-history.ts` (20),
    `pages/page-environments-list.ts` (19), `pages/page-monitor-requests.ts` (16),
    `components/environment-tabs/env-monitor.ts` (16).
  - Zero narrow handling: `pages/page-servers-audit.ts`,
    `pages/page-databases-audit.ts` (14 columns each).
  - `src/dorc-web/package.json` — `@vaadin/grid` `^25.2.6`.
- **Prior related work**:
  - `docs/grid-menu-a11y/` — row activation and menu accessibility; `DECISION-U-2.md`
    resolved in favour of Vaadin's `active-item-changed`.
  - `docs/request-grid-perf/` — backend substring-scan performance; unrelated to
    grid component capability.
- **Conventions**: `CLAUDE.md`. HLPS pattern follows `docs/monitor-robustness/`.

---

## Status / Review Trail

| Round | Date | Status | Reviewers | Outcome |
|---|---|---|---|---|
| — | 2026-08-02 | DRAFT | — | Authored. Not yet submitted to the adversarial panel. **U-1 and U-2 are blocking and unanswered.** |
| — | 2026-08-02 | DRAFT | — | **Both blocking unknowns resolved by the user.** U-1: no current phone usage or telemetry; deliberate foundation for application-support and on-call personas. U-2: no lazy tree grid, inline editing not required beyond today's capabilities. Consequent changes: §2 goal restated around the target personas; new §3.3 target journey and proposed pilot set; U-3 narrowed to incident-response entities; U-5 proposed; SC9 added (scripted triage walkthrough as the substitute for usage evidence); anticipated-persona risk added at MEDIUM–HIGH; §9.4 records that the §9.2 revisit trigger did not fire. **No blocking unknowns remain — ready for the adversarial panel.** |
