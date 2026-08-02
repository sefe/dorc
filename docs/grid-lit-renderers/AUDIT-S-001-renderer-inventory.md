# AUDIT S-001: Renderer Inventory and Classification

| Field       | Value                                                    |
|-------------|----------------------------------------------------------|
| **Status**  | COMPLETE — supersedes all earlier counts                 |
| **Author**  | Agent                                                    |
| **Date**    | 2026-08-02                                               |
| **Folder**  | `docs/grid-lit-renderers/`                               |
| **Redoes**  | `REVIEW-R1-triage.md` R1-06, R1-13, R1-19 — both audits failed review round 1 |
| **Tool**    | `src/dorc-web/tools/renderer-audit.mjs` (committed, re-runnable) |

---

## 1. Why This Was Redone

Round 1 of adversarial review rejected both of the earlier audits. The root
cause was method, not arithmetic:

- **Body extraction** used a fixed 1500-character window from the method name,
  so it read past short methods into their neighbours and truncated long ones.
- **State classification** scanned for `@property`/`@state` decorator *text*
  near a name, which misclassifies this codebase because much of its filter
  state is held in undecorated fields.
- **Side-effect detection** used heuristics that neither added up (10 + 12 + 4
  against a stated total of 28) nor were reproducible.

This audit replaces all of it with an AST analysis.

## 2. Method

Reproduce with `node tools/renderer-audit.mjs` (add `--json` for raw data).

Binding *sites* are found by regex — Lit templates are strings and the binding
syntax genuinely is regular. **Everything semantic uses the TypeScript AST**
(`typescript@6.0.3`, already a project dependency):

- Class members are enumerated from the AST, with real decorator nodes, so
  "reactive" means Lit will actually track it.
- Renderer bodies are the AST body node — exact, no windowing.
- `this.<name>` reads are collected by walking property-access expressions.
- Reads are resolved **one level** through getters and helper methods, because
  a renderer reading `this.filteredRows` where that getter reads
  `this.nameFilter` genuinely depends on `nameFilter`. Chains deeper than one
  hop are reported as such rather than silently truncated — there are 3.

### 2.1 Known limits, stated rather than hidden

- **Inherited members are not resolved.** Reads of members defined on
  `PageElement`, `PageEnvBase` or `ResponsiveMixin` are counted separately as
  `inherited`, not as state. Anything in that bucket needs a human read.
- **One-hop transitivity only.** The 3 renderers with deeper chains are listed.
- **Element construction** is detected as `new X()` where `X` is imported from
  `@vaadin/*` in that file. An earlier version of this script counted any
  `new X()` and reported 57 "element constructions"; the true figure is 8. That
  correction is why this section exists.

---

## 3. Results

```
Files scanned: 191        Files with bindings: 48
Total renderer bindings:  172
Distinct renderer members: 160  (+2 unresolved — module-level functions)
```

### 3.1 Bindings by host element

| Element | Property | Count | Target directive |
|---|---|---|---|
| `vaadin-grid-column` | `.renderer` | 78 | `columnBodyRenderer` |
| `vaadin-grid-column` | `.headerRenderer` | 53 | `columnHeaderRenderer` |
| `vaadin-grid-sort-column` | `.renderer` | 18 | `columnBodyRenderer` |
| `vaadin-combo-box` | `.renderer` | 17 | `comboBoxRenderer` |
| `vaadin-notification` | `.renderer` | 4 | `notificationRenderer` |
| `vaadin-dialog` | `.renderer` | 1 | `dialogRenderer` |
| `vaadin-grid` | `.rowDetailsRenderer` | 1 | `gridRowDetailsRenderer` |
| | **Total** | **172** | |

The audit's original figure was 167 and the first IS repeated it. The missing
five are the 4 notification renderers (found by review) **and one
`vaadin-dialog .renderer`**, which no reviewer found either.

### 3.2 Binding styles — two previously unidentified

| Style | Count | Note |
|---|---|---|
| `this.foo` plain reference | 154 | |
| `this.foo.bind(this)` inline | 8 | **Self-refreshing today** — new identity per host render |
| **`this.factory('arg')` — renderer factory** | **6** | **Newly identified.** Also self-refreshing. |
| **module-level function reference** | **2** | **Newly identified.** No `this` at all. |
| inline arrow | 2 | Self-refreshing today |

The **renderer factory** pattern — `.renderer="${this.valueRenderer('FromValue')}"`
in `page-daemons-audit.ts:144,152`, `page-databases-audit.ts:129,136`,
`page-servers-audit.ts:129,136` — calls a method at template time that returns a
closure over its argument. Neither I nor any of the three reviewers identified
it. Like the `bind`-inline and arrow groups it produces a fresh function
identity per render, so it currently self-refreshes; converting it needs the
factory argument carried into the directive, and it must never get an empty
dependency array.

The **module-level** renderers are free functions in
`add-{endur,windows}-user-or-group.template.ts` — no `this`, so no dependency
question, but they still need the signature change.

### 3.3 Classification — by body read, not decorator scan

| Category | Count |
|---|---|
| Item-only (no `this.<state>` reads) | **113** |
| Stateful | **47** |
| — of which read **undecorated** fields | **33** |
| Reads chaining deeper than one hop | 3 |

The earlier split was 91 / 46. The difference is not a rounding error: correct
body extraction moved 22 renderers into the item-only bucket, and the
undecorated-field breakdown did not exist at all before.

### 3.4 The finding that most changes the plan

**33 of the 47 stateful renderers read at least one undecorated field.**

`LitRendererDirective.update()` runs only when Lit re-renders the host template,
so a dependency on a field Lit does not track is never compared. A dependency
array containing an undecorated field **fires nothing**, silently.

Review round 1 identified this for `_editingValueId` and estimated it affected
6 of the 12 `requestContentUpdate()` removals. The real exposure is far larger,
and it is concentrated in one pattern — in-header filter fields:

| File | Renderers affected | Undecorated fields |
|---|---|---|
| `page-variables.ts` | 3 | `allPropertyValues`, `scopeFilterValue`, `valueFilterValue`, `_editingValueId` |
| `page-variables-audit.ts` | 4 | `nameFilterValue`, `environmentFilterValue`, `userFilterValue`, `valueFilterValue` |
| `page-variables-value-lookup.ts` | 4 | `nameFilterValue`, `scopeFilterValue`, `valueFilterValue`, `_editingValueId` |
| `page-scripts-audit.ts` | 4 | `nameFilterValue`, `projectNamesFilterValue`, `userFilterValue`, `valueFilterValue` |
| `page-{daemons,databases,servers,projects}-audit.ts` | 8 | `userFilter`, `actionFilter` |
| `page-scripts-list.ts` | ≥1 | `userRoles` |
| `env-variables.ts`, `env-monitor.ts` | 2 | `_editingValueId`, `_idHeaderRoot`, `grid`, `maxCountBeforeRefresh` |
| `page-project-bundles.ts` | 1 | `_boundHandleBundleNameFilterChange` |
| `add-edit-access-control.ts` | 2 | `acStyles` |
| `page-projects-audit.ts` | 2 | `userFilter`, `actionFilter` |

**Consequence for the plan:** promoting these fields to `@state` is a
prerequisite, not a detail — and promoting a field changes when the host
re-renders, which is a behaviour change in its own right and needs its own
verification. Some (`acStyles`, `grid`, `_idHeaderRoot`) are *not* state at all
and must be excluded from dependency arrays rather than promoted.

### 3.5 Side effects — 25 renderers

| Effect | Count |
|---|---|
| `root.innerHTML =` | 12 |
| `addEventListener` | 8 |
| Constructs a Vaadin element | 8 |
| Caches the root element | 2 |
| **Distinct renderers** | **25** |

Categories overlap — `page-config-values-list.isSecuredRenderer` both constructs
a `Checkbox` and attaches a listener — which is why the column sums to 30 and
the distinct total is 25. The earlier document reported 10/12/4 against a total
of 28 with no explanation, which is what review caught.

`caches-root` is **2** (`env-monitor.idHeaderRenderer`,
`page-monitor-requests.idHeaderRenderer`), confirming the reviewer's correction
of my earlier 4 — the extra two were re-invocation *call sites*, not renderers.

---

## 4. What This Changes

1. **The total is 172**, and the step structure must cover notification and
   dialog renderers.
2. **Two binding styles were missing from the plan entirely** — factories and
   module-level functions.
3. **S-002 must not be "item-only header renderers"** — that category does not
   exist, and header renderers are where the undecorated-filter-state problem
   is concentrated.
4. **A new prerequisite step is required**: decide, per undecorated field,
   promote-to-`@state` or exclude-from-dependencies. 33 renderers depend on it.
5. **The item-only bucket is 113, not 91** — the low-risk bulk is larger than
   thought, which is the one piece of good news here.

The revised Implementation Sequence follows in a separate document; this audit
is the input to it, not the plan itself.
