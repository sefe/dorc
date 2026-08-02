# HLPS: Migrate Grid and Combo-Box Renderers to the Vaadin Lit Directives

| Field       | Value                                              |
|-------------|----------------------------------------------------|
| **Status**  | DRAFT — awaiting review                            |
| **Author**  | Agent                                              |
| **Date**    | 2026-08-02                                         |
| **Folder**  | `docs/grid-lit-renderers/`                         |
| **Resolves**| `docs/vaadin-alignment/AUDIT-vaadin-25-alignment.md`, finding F-4 |
| **Scope**   | `src/dorc-web` — 167 renderer bindings across 47 files |

---

## 1. Problem Statement

The UI assigns cell, header and item renderers by binding plain functions to
Vaadin component properties:

```ts
<vaadin-grid-column .renderer="${this.variableValueControlsRenderer}">
```

Vaadin 25 ships Lit directives for exactly this — `columnBodyRenderer`,
`columnHeaderRenderer`, `columnFooterRenderer`, `gridRowDetailsRenderer` from
`@vaadin/grid/lit`, and `comboBoxRenderer` from `@vaadin/combo-box/lit`. The
codebase already uses the equivalent directives for dialogs (`dialogRenderer`,
`dialogFooterRenderer`) in 8 files, so the convention exists; it was simply
never applied to grids and combo-boxes.

This is not a style preference. **A plain renderer function is a stable
reference, so the component has no way to know that state the function closes
over has changed.** The directives take a dependency array and re-render when
it changes; hand-bound functions cannot.

### 1.1 Confirmed defect, not a theoretical one

`tests/components/grid-renderer-staleness.test.ts` reproduces the failure
against real application code:

`attached-env-tenants` renders a per-row **Detach** button whose `?disabled`
and icon colour come from `this.readonly`, a reactive `@property`. The
component never calls `requestContentUpdate()`. `readonly` is fed from
`env-tenants.envReadOnly`, which is assigned in `notifyEnvironmentReady()` —
**after** the environment API call resolves, and therefore after the grid has
already rendered its rows.

The reproduction asserts three things, and all three pass today:

1. While `readonly` is `false`, the Detach button renders enabled.
2. When `readonly` flips to `true`, **the button stays enabled.**
3. Calling `grid.requestContentUpdate()` disables it.

Step 3 proves this is a re-render problem rather than a binding problem. The
user-visible consequence: on an environment the user may only read, the Detach
control appears enabled and coloured as active. The permission itself is
enforced server-side, so this is a misleading affordance rather than a
privilege escalation — but it is a real defect, and it is the class of defect
this pattern produces silently.

### 1.2 The existing workaround is partial and manual

Twelve `requestContentUpdate()` calls across seven files exist to paper over
this. They are the same fix the directives apply automatically, applied by
hand, at whichever call sites somebody noticed. Any renderer reading reactive
state in a component *without* such a call is silently stale — as above.

### 1.3 Secondary cost: type safety

`lit-analyzer --strict` reports the raw bindings as `no-incompatible-type-binding`
because the generic `GridBodyRenderer<TItem>` cannot be inferred through a plain
property binding. The directives are generic and infer correctly.

---

## 2. Current State

Measured against `src/`, not estimated:

| Binding | Count | Target directive | Module |
|---|---|---|---|
| `vaadin-grid-column .renderer` | 78 | `columnBodyRenderer` | `@vaadin/grid/lit` |
| `vaadin-grid-column .headerRenderer` | 53 | `columnHeaderRenderer` | `@vaadin/grid/lit` |
| `vaadin-grid-sort-column .renderer` | 18 | `columnBodyRenderer` | `@vaadin/grid/lit` |
| `vaadin-combo-box .renderer` | 17 | `comboBoxRenderer` | `@vaadin/combo-box/lit` |
| `vaadin-grid .rowDetailsRenderer` | 1 | `gridRowDetailsRenderer` | `@vaadin/grid/lit` |
| **Total** | **167** | across **47 files** | |

> **Correction to the audit.** Finding F-4 stated "117 raw `.renderer=`
> bindings". That counted only `.renderer=` and missed `.headerRenderer=`. The
> true figure is **167**, and 17 of them are combo-boxes needing a *different*
> directive from a *different* module. The work is roughly 40% larger than the
> audit implied, and this HLPS supersedes that number.

### 2.1 How renderers are written today

| Style | Count |
|---|---|
| Plain method reference — `${this.fooRenderer}` | 135 |
| Bound in field initialiser — `_boundX = this.X.bind(this)` | 7 |
| `.bind(this)` inline in the template | 8 |
| Inline arrow function | 1 |

### 2.2 How much state is involved

Of **137** distinct renderer methods (some are bound to more than one column):

- **91 are item-only** — they read `model.item` and nothing else. These convert
  mechanically with an empty dependency array.
- **46 read reactive state** declared as `@property`/`@state` on their host
  component. These need a considered dependency array and are where both the
  risk and the value of this work sit.

The heaviest are `add-edit-database._boundADGroupsRenderer` (8 reactive
dependencies), `attach-database._boundDatabasesRenderer` (7), and
`edit-database-permissions._boundPermissionsRenderer` / `_boundUsersRenderer`
(6 each).

> This classification comes from static analysis over a fixed window of each
> method body, so treat the 46/91 split as a planning estimate. Each renderer's
> dependency array must be derived by reading the method, not from this table.
> See U-1.

---

## 3. Scope

### In scope

- All 167 bindings listed in §2, converted to the corresponding directive.
- Removal of each `requestContentUpdate()` call **once** the renderer it was
  compensating for carries a correct dependency array.
- Inverting `grid-renderer-staleness.test.ts` to assert correct behaviour.
- Regression tests for the renderers that read reactive state.

### Out of scope

- Changing what any renderer draws. This migration is behaviour-preserving
  except where the current behaviour is the stale-cell defect.
- `dataProvider` vs `items` data-loading strategy (7 grids use `dataProvider`;
  untouched).
- `clearCache()` calls — these invalidate *data*, not rendering, and are a
  separate mechanism that stays as-is.
- The `.cellPartNameGenerator` bindings.
- Grid column-hiding, sorting or filtering behaviour.

---

## 4. Constraints

1. **Behaviour-preserving.** Every converted renderer must draw exactly what it
   draws today, given the same inputs.
2. **Dependency arrays must be derived by reading each method**, not pattern-
   matched. An incomplete array reintroduces the very staleness this fixes, and
   silently.
3. **An over-broad dependency array is a performance regression, not a bug** —
   it forces needless cell re-renders. Prefer correct over generous, but when
   genuinely uncertain, err generous and note it.
4. **Combo-box renderers use `comboBoxRenderer` from `@vaadin/combo-box/lit`.**
   They are not grid columns and must not be swept in with them.
5. `requestContentUpdate()` calls may only be deleted *in the same step* as the
   renderer they compensate for, so no interval exists where neither mechanism
   is present.
6. Existing tests (150) must stay green throughout; the suite must be run per
   step, not only at the end.
7. No new dependencies. Everything needed ships in the installed
   `@vaadin/grid@25.2.6` and `@vaadin/combo-box@25.2.6`.

---

## 5. Success Criteria

| # | Criterion | How it is verified |
|---|---|---|
| SC-1 | No `.renderer` / `.headerRenderer` / `.rowDetailsRenderer` property bindings remain on Vaadin elements | `grep` returns zero |
| SC-2 | The stale-cell defect is fixed | `grid-renderer-staleness.test.ts`, inverted, asserts the button **is** disabled after `readonly` flips, with no `requestContentUpdate()` |
| SC-3 | All 12 `requestContentUpdate()` compensation calls are gone | `grep` returns zero, and the tests covering those screens still pass |
| SC-4 | `lit-analyzer --strict` `no-incompatible-type-binding` count materially falls | before/after counts recorded (132 today, though not all are renderer-related) |
| SC-5 | No regression | 150 existing tests green, `tsc` clean on both tsconfigs, `eslint` clean, build succeeds |
| SC-6 | Renderers reading reactive state have regression tests | new tests accompany each such conversion |

---

## 6. Risks

| Risk | Severity | Mitigation |
|---|---|---|
| An incomplete dependency array reintroduces staleness silently | **High** | Derive each array by reading the method; add a regression test for every stateful renderer; these are the 46 from §2.2 |
| A renderer with side effects (caching a root element, wiring listeners) behaves differently when re-invoked more often | Medium | `env-monitor` caches `_idHeaderRoot`; audit for this pattern before converting, not during |
| 47 files is a large blast radius for one review | Medium | The IS must slice by file/screen, each independently shippable and testable |
| Removing `requestContentUpdate()` too eagerly | Medium | Constraint 5 — same-step only |
| Over-broad dependency arrays quietly cost render performance | Low | Note the deliberate ones; the grids here are small enough that correctness wins |

---

## 7. Unknowns Register

| ID | Unknown | Blocking? | Resolution path |
|----|---------|-----------|-----------------|
| U-1 | Exact dependency array for each of the 46 stateful renderers | **No** — resolved per-step | Read each method during its JIT spec; the 46/91 split in §2.2 is a planning estimate only |
| U-2 | Do any renderers rely on being invoked *rarely*, e.g. by caching a root element or attaching listeners? `env-monitor.idHeaderRenderer` stores `_idHeaderRoot` | **No** | Audit the 46 stateful renderers for side effects as IS step S-001 before any conversion |
| U-3 | Do the 7 `dataProvider` grids interact with renderer re-invocation differently from `items` grids? | **No** | Verify on the first `dataProvider` grid converted; if they differ, split the IS |
| U-4 | Is the `attached-env-tenants` stale Detach button the only *user-visible* instance, or are there others worth fixing first? | **No** | The audit of U-2 will surface them; prioritise the IS by user impact |
| U-5 | Should the 12 `requestContentUpdate()` removals be one final step or folded into each file's step? | **No** | Recommend folded in, per constraint 5; confirm at IS review |

**No blocking unknowns.** This HLPS can proceed to an Implementation Sequence.

---

## 8. Recommended Shape of the Implementation Sequence

Not the IS itself — that is the next document — but the shape proposed for it:

1. **S-001** — Audit the 46 stateful renderers for side effects (U-2). Output is
   a table, no code change.
2. **S-002** — Convert the 91 item-only renderers. Mechanical, empty dependency
   arrays, low risk, and it removes most of the volume in one reviewable pass.
3. **S-003** — Convert the 17 combo-box renderers with `comboBoxRenderer`.
   Separate because it is a different directive and module.
4. **S-004..n** — Convert the stateful renderers, grouped by screen, each with
   its dependency array justified and a regression test, and each deleting the
   `requestContentUpdate()` calls it supersedes.
5. **Final** — Invert `grid-renderer-staleness.test.ts`; record before/after
   `lit-analyzer` counts.

Sequencing S-002 before the stateful ones front-loads the low-risk bulk and
keeps the risky reviews small.

---

## 9. Checkpoint

Per `CLAUDE.md`, this HLPS requires approval before an Implementation Sequence
is drafted. It has not been through the adversarial review panel; that gate is
outstanding.
