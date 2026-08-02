# IS: Migrate Grid and Combo-Box Renderers to the Vaadin Lit Directives

| Field       | Value                                    |
|-------------|------------------------------------------|
| **Status**  | ❌ REVISION — failed review round 1       |
| **Author**  | Agent                                    |
| **Date**    | 2026-08-02                               |
| **Folder**  | `docs/grid-lit-renderers/`               |
| **HLPS**    | `HLPS-grid-lit-renderers.md` (U-2 resolved below) |

> ⚠️ **FAILED ADVERSARIAL REVIEW ROUND 1 — DO NOT EXECUTE.**
> See `../vaadin-alignment/REVIEW-R1-triage.md`. Verified defects in this
> document: the inventory misses 4 `<vaadin-notification>` renderers (the count
> is 171+, not 167); the 91/46 stateful split used a decorator scan and is
> unreliable because this codebase holds filter state in undecorated fields;
> S-002 is graded backwards (40 of 43 header renderers are stateful);
> dependency arrays on undecorated fields never fire, which silently breaks
> 6 of the 12 `requestContentUpdate()` removals; inline `.bind(this)` bindings
> currently self-refresh and must never get an empty dependency array; and
> `focus-target` must be **preserved** through these rewrites, not removed.

---

## 1. The Transformation Is Larger Than "Wrap It In A Directive"

The installed type definitions make this concrete, and it changes the shape of
the work. **The directive's renderer signature is not the current one.**

| | Signature | Style |
|---|---|---|
| **Today** | `(root: HTMLElement, column: GridColumn, model: GridItemModel<T>) => void` | Imperative — writes *into* `root` |
| **Directive** | `(item: T, model: GridItemModel<T>, column: GridColumn) => LitRendererResult` | Declarative — *returns* a template |

Header renderers change more sharply still: `columnHeaderRenderer` takes
`(column) => result` — one argument.

So **every one of the 167 renderers needs its body edited**, not merely its
binding site. For most this is small and mechanical:

```ts
// before
myRenderer(root: HTMLElement, _c: GridColumn, model: GridItemModel<Foo>) {
  render(html`<span>${model.item.Name}</span>`, root);
}
// after
myRenderer = (item: Foo) => html`<span>${item.Name}</span>`;
```

...and the binding moves from a property to a directive:

```ts
<vaadin-grid-column ${columnBodyRenderer(this.myRenderer, [])}></vaadin-grid-column>
```

### 1.1 Resolution of HLPS U-2 — 28 renderers have side effects

The audit required by U-2 is done. **28 renderers across 15 files** do more than
draw, and none of them convert mechanically:

| Pattern | Count | Why it blocks a mechanical swap |
|---|---|---|
| `root.innerHTML = ...` | 10 | Imperative string DOM. Must be rewritten as a template; also currently bypasses Lit escaping. |
| `new Checkbox()` + `addEventListener` | 12 | Constructs elements imperatively and wires listeners by hand. Must become a declarative template with `@change`. |
| Caches the root element (`this._idHeaderRoot = root`) | 4 | `env-monitor` and `page-monitor-requests` stash `root` to re-invoke the renderer manually later. That mechanism disappears — the dependency array replaces it. |

The `root.innerHTML =` group is worth calling out beyond mechanics: it
interpolates model values into an HTML string, so it neither escapes nor
benefits from Lit's sanitisation. Converting them to templates is a small
security improvement as well as a correctness one.

### 1.2 Dependency arrays use strict equality

Per the directive docs: dependencies are compared with `===`, and *"the check
won't detect nested property changes inside objects or arrays"*. Any renderer
whose output depends on the **contents** of an array or object must therefore
depend on a value whose identity changes — the component must replace the array
(`this.items = [...next]`) rather than mutate it. Components that mutate in
place need that fixed as part of their step, or the dependency will never fire.

---

## 2. Steps

| Step | Title | Scope | Risk |
|---|---|---|---|
| **S-001** | Side-effect audit of all renderers | — | ✅ **DONE** — §1.1 |
| **S-002** | Convert item-only **header** renderers | ~53 bindings | Low — `(column) => result`, almost all static |
| **S-003** | Convert item-only **body** renderers | ~91 methods | Low, but the largest volume; slice by directory |
| **S-004** | Convert the 17 combo-box renderers | `comboBoxRenderer` | Low — different module, kept separate |
| **S-005** | Convert the single `rowDetailsRenderer` | 1 | Low |
| **S-006** | Rewrite the 10 `root.innerHTML =` renderers | 8 files | **Medium** — real rewrites, not swaps |
| **S-007** | Rewrite the 12 imperative-element + listener renderers | 6 files | **Medium** |
| **S-008** | Rewrite the 4 root-caching renderers; delete the manual re-invoke paths | `env-monitor`, `page-monitor-requests` | **High** — these interact with SignalR live updates |
| **S-009..n** | Convert remaining stateful renderers by screen, each with its dependency array justified, deleting the `requestContentUpdate()` calls it supersedes | 46 total stateful | **Medium** |
| **Final** | Invert `grid-renderer-staleness.test.ts`; record `lit-analyzer` before/after; confirm zero `requestContentUpdate()` remain | — | Low |

### Why this order

S-002 and S-003 are the bulk (≈144 of 167) and the lowest risk, so they go
first and shrink the problem to the interesting ~28. S-008 is last of the
rewrites because `env-monitor` and `page-monitor-requests` combine cached roots,
`dataProvider`, and live SignalR updates — the three things most likely to
interact badly.

### Per-step definition of done

1. `tsc` clean both tsconfigs; `eslint` clean; build succeeds.
2. Full suite green.
3. For any step touching a stateful renderer: a regression test proving the cell
   updates when its dependency changes.
4. No `requestContentUpdate()` deleted except in the same step as the renderer
   it compensated for.

---

## 3. Verification Strategy

- **The staleness contract.** `grid-renderer-staleness.test.ts` currently
  asserts the *broken* behaviour. When `attached-env-tenants` is converted, that
  test inverts to assert the button **is** disabled after `readonly` flips, with
  no `requestContentUpdate()`. That inversion is the single clearest signal the
  migration achieved its purpose.
- **Per stateful renderer**: change the dependency, assert the cell re-renders.
- **Per side-effecting renderer** (S-006..S-008): assert the rendered output
  matches what the imperative version produced, before and after.
- **Listener behaviour** (S-007): assert the control still fires its action
  after several re-renders — the directive re-invokes more often than the
  current code, so a regression here would be a duplicated or lost handler.

---

## 4. Rollback

Steps are independent per file. S-002..S-005 are mechanical and revert cleanly.
S-006..S-008 change renderer bodies and should each be their own commit with its
tests, so a revert takes the test with it.

---

## 5. Open Questions for Review

1. **S-008 sizing.** `env-monitor` and `page-monitor-requests` may warrant their
   own HLPS rather than being steps here — they combine cached roots, live
   SignalR updates and `dataProvider`. Recommend deciding at review.
2. **Should S-003 be split?** ~91 renderers is a lot for one review even at low
   risk. Recommend slicing by directory (`components/`, `components/
   environment-tabs/`, `components/grid-button-groups/`, `pages/`).
3. **Mutating arrays** (§1.2) — if a component mutates an array in place, fixing
   it is arguably out of scope, but the dependency will not fire without it.
   Recommend fixing in-step and noting each occurrence.

---

## 6. Checkpoint

Per `CLAUDE.md` this IS requires approval before S-002 executes, and it has not
been through the adversarial review panel.
