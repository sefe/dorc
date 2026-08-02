# AUDIT: `dorc-web` Alignment with Vaadin 25 Documentation

| Field        | Value                                          |
|--------------|------------------------------------------------|
| **Status**   | DRAFT — for review                             |
| **Author**   | Agent                                          |
| **Date**     | 2026-08-02                                     |
| **Folder**   | `docs/vaadin-alignment/`                       |
| **Scope**    | `src/dorc-web` (Lit + Vaadin web components UI) |
| **Baseline** | Vaadin platform 25.2.6 (current `latest` on npm) |

> **Update 2026-08-02** — F-2 is resolved: the app has migrated off
> `@vaadin/router` to `universal-router`. See
> `MIGRATION-router-universal-router.md` in this folder. The original F-2 text
> below recommended `@lit-labs/router`; that recommendation was **wrong** and is
> corrected in place.

---

## 1. Purpose

Compare the DOrc web UI against the recommendations published in the current
Vaadin documentation and the API surface actually shipped in the installed
`@vaadin/*` 25.2.6 packages, and identify where the codebase has drifted.

This is an assessment only — no production code was changed. Findings are
ranked so they can be turned into an Implementation Sequence if accepted.

## 2. Method

1. Inventoried every `@vaadin/*` import and element usage across 568 TypeScript
   files under `src/` and `tests/`.
2. Compared the resolved dependency tree (`package-lock.json`) against the npm
   registry to establish version currency.
3. Verified each questioned API against the **installed** package type
   definitions in `node_modules/@vaadin/*` — the authoritative source for what
   25.2.6 actually accepts — rather than relying on documentation prose alone.
4. Ran the repository's own configured toolchain: `eslint`, `lit-analyzer
   --strict`.
5. Cross-checked against the Vaadin 25 upgrade guide, the Vaadin 25.0 release
   notes, and the components/styling documentation.

## 3. Headline Result

**The platform version itself is fully up to date.** All `@vaadin/*` packages
resolve to 25.2.6, which is the current `latest` dist-tag. The Vaadin 25 Lumo
migration — the single largest breaking change in this release, where Lumo
component styles stopped shipping with the components and became a separately
loaded stylesheet — has already been done correctly, via a Vite plugin serving
`@vaadin/vaadin-lumo-styles/dist/lumo.css` and its `src/`+`components/` trees
(`vite.config.js`, `index.html`).

The drift is not in *versions*, it is in *usage patterns*: the application is
running current Vaadin against several APIs that no longer exist, a routing
library Vaadin has stopped maintaining, and a Polymer dependency chain that
Vaadin itself removed in this release.

`eslint` passes cleanly. `lit-analyzer --strict` reports 504 problems across 89
files (152 errors, 352 warnings) — it is not wired into the build gate, only
into the optional `type-checking` script.

---

## 4. Findings

### F-1 — `auto-validate` no longer exists on Vaadin fields (HIGH)

21 occurrences across 11 components set `auto-validate` on `<vaadin-text-field>`
and friends, e.g. `src/components/add-permission.ts:71`,
`src/components/add-edit-environment.ts:262`, `src/components/add-daemon.ts:79`.

There is no `autoValidate` property or `auto-validate` attribute anywhere in the
installed `@vaadin/field-base` or `@vaadin/text-field` type definitions. The
attribute is inert: it lands in the DOM and does nothing.

Vaadin fields in 24+ validate automatically on blur and on value change once
touched, so the *observable* behaviour is largely what the authors intended —
but the code asserts a contract the framework no longer offers, and anyone
reading it will assume validation is opt-in. `lit-analyzer` flags all 21.

**Recommendation:** delete the attribute. Where a field genuinely needs eager
validation, call `.validate()` explicitly.

### F-2 — `@vaadin/router` is no longer maintained (HIGH — strategic) — ✅ RESOLVED

`@vaadin/router@2.0.1` was the app's routing layer (`src/router/`, 11 importing
modules). The Vaadin 25 upgrade guide states plainly that "the Vaadin Router
library is no longer actively maintained, as Vaadin now uses React Router as its
primary client-side routing solution," and recommends migrating Lit views.

Version 2.0.1 is the terminal release. It still worked, and nothing was urgent,
but it was an unmaintained dependency carrying the app's entire navigation and
route-guard surface.

**Correction to this audit's original recommendation.** The first version of
this document proposed `@lit-labs/router` as "a maintained Lit-compatible
router." That was wrong on the facts, and checking the registry before starting
the migration showed why:

| | `@vaadin/router` | `@lit-labs/router` |
|---|---|---|
| Latest version | 2.0.1 | 0.1.4 |
| Published | 2025-11-14 | 2025-02-14 |
| Weekly downloads | 40,650 | 28,323 |
| Stability | 2.x, terminal release | Lit Labs, pre-1.0, "may receive breaking changes or stop being supported" |

The proposed replacement was **staler than what it would replace**. Worse, its
own API documentation states that `goto()` "does not navigate parent routes, so
it isn't (yet) a general page navigation API" — but this app navigates
imperatively from 13 call sites that cross top-level and nested route
boundaries. It would also have required a `URLPattern` polyfill for
Safari/Firefox. The migration would have delivered a hand-written navigation
shim on top of a less-maintained dependency.

**Resolution: migrated to `universal-router` v10.0.3** (published 2026-01-16,
48,515 weekly downloads) — the route resolver `@vaadin/router` is itself built
on, so the existing route table's nested `children`, `action` redirects and
`:param` syntax carry over almost unchanged. It is framework-agnostic, so the
outlet rendering, link interception and location plumbing are now ~380 lines of
owned, tested application code rather than a dependency.

See `MIGRATION-router-universal-router.md` for the design, the behavioural
differences, and the verification performed.

### F-3 — Polymer dependency chain retained after Vaadin dropped it (HIGH)

Vaadin 25 removed Polymer from the web components entirely — they are Lit-based
now. The app nonetheless pulls **20 Polymer packages** (`@polymer/polymer@3.5.2`
plus 19 `iron-*`/`paper-*` transitive packages) for exactly two elements:

- `<paper-dialog>` — 19 files, 9 element usages
- `<paper-toggle-button>` — 6 files

Polymer 3 is end-of-life. The cost is visible in the codebase: `index.html`
carries a dark-mode block overriding `--paper-dialog-background-color`,
`--primary-background-color` and `--primary-text-color` purely to stop
`paper-dialog` looking wrong in dark mode, and `lit-analyzer` reports 82 unknown
attributes that are Polymer-isms (`focus-target` ×58, `dialog-confirm` ×12,
`allow-click-through` ×12) plus `flex` ×5, `closed` ×3, `raised` ×1.

Vaadin already ships equivalents the app depends on: `@vaadin/dialog` (already
used, with the Lit renderer directives, in 8 files) and `@vaadin/checkbox`.

**Recommendation:** replace `paper-toggle-button` with `<vaadin-checkbox>` (6
files, low risk) and `paper-dialog` with `<vaadin-dialog>` (19 files, moderate —
each needs its `focus-target`/`dialog-confirm` wiring re-expressed). Removing
both drops 20 packages and the dark-mode override block.

### F-4 — Grid renderers bypass the documented Lit directives (MEDIUM)

117 raw `.renderer=` / `.headerRenderer=` property bindings across 47 files,
e.g. `src/pages/page-variables.ts:373`,
`src/components/environment-tabs/env-variables.ts:362`.

`@vaadin/grid@25.2.6` ships `@vaadin/grid/lit`, exporting `columnBodyRenderer`,
`columnHeaderRenderer`, `columnFooterRenderer` and `gridRowDetailsRenderer` —
the documented approach for Lit applications. The app already uses the identical
pattern for dialogs (`dialogRenderer`, `dialogFooterRenderer` from
`@vaadin/dialog/lit`), so the convention is established, just not applied to
grids.

This is not cosmetic. A plain renderer function is a stable reference, so Grid
has no way to know when the component state it closes over has changed. The
codebase compensates with 12 scattered manual `grid.requestContentUpdate()`
calls (`env-variables.ts:404`, `page-variables.ts:695`,
`page-config-values-list.ts:61`, …). `columnBodyRenderer(fn, deps)` takes a
dependency array and re-renders automatically, which is what those calls are
hand-rolling. Any cell whose renderer reads state *not* covered by one of those
12 call sites is silently stale.

It also costs type safety: `lit-analyzer` reports the raw bindings as
`no-incompatible-type-binding` because the generic `GridBodyRenderer<TItem>`
cannot be inferred through a plain property binding.

**Recommendation:** migrate grid renderers to the `@vaadin/grid/lit` directives,
removing the manual `requestContentUpdate()` calls as each is superseded. This
is the single highest-value correctness item in the audit. It is also the
largest, so it should be sequenced file-by-file rather than as one change.

### F-5 — Imports reaching into Vaadin private module paths (MEDIUM)

Seven imports bypass the package's public entry points and reach into internal
directories:

| File | Import |
|---|---|
| `src/components/add-edit-server.ts:2` | `@vaadin/combo-box/src/vaadin-combo-box` |
| `src/components/deploy/deploy-env.ts:4` | `@vaadin/combo-box/src/vaadin-combo-box` |
| `src/components/add-sql-port.ts:3` | `@vaadin/combo-box/src/vaadin-combo-box` |
| `src/components/make-like-production.ts:14` | `@vaadin/combo-box/src/vaadin-combo-box` |
| `src/pages/page-deploy.ts:3` | `@vaadin/combo-box/src/vaadin-combo-box` |
| `src/pages/page-project-components.ts:8` | `@vaadin/grid/src/vaadin-grid-column` |
| ~~`src/components/add-edit-environment.ts:21`~~ | ~~`@vaadin/router/dist/router.js`~~ — removed by the F-2 migration |

`src/` and `dist/` are internal layout, not API. Vaadin reorganises these
between minors without it counting as a breaking change, so these are the
imports most likely to break on the next `25.x` bump. Every one of them is
available from the package root (`@vaadin/combo-box`, `@vaadin/grid`,
`@vaadin/router`), which the rest of the codebase already uses.

**Recommendation:** rewrite all seven to the public entry point. Trivial, and it
removes a class of upgrade breakage.

### F-6 — `filter-property` is not a Vaadin combo-box API (LOW)

Five `<vaadin-combo-box>` elements set `filter-property`
(`attach-server.ts:65`, `edit-database-permissions.ts:90` and `:104`,
`add-edit-database.ts:186`, `attach-environment.ts:65`).

No such attribute exists on `@vaadin/combo-box`. In every case the value is
identical to the element's own `item-label-path` — which is what the combo-box
filters on by default — so the attribute is redundant as well as unrecognised.
It appears to be a survival from a pre-Vaadin combo-box API.

**Recommendation:** delete. For genuinely custom filtering, the documented route
is `filteredItems` plus the `filter-changed` event.

### F-7 — `eslint-plugin-lit`, `eslint-plugin-lit-a11y` and `eslint-plugin-wc` are installed but never run (MEDIUM)

`package.json` declares all three as devDependencies. `eslint.config.js`
registers none of them — the flat config loads only `@eslint/js`,
`typescript-eslint` and `eslint-config-prettier`.

The project is therefore paying for Lit and accessibility linting it does not
receive, which matters here because accessibility is an active concern in this
repo (`docs/grid-menu-a11y/`). `stylelint@17` is likewise a devDependency with
no configuration file and no npm script.

**Recommendation:** wire the three plugins into `eslint.config.js` (expect an
initial backlog of findings — introduce them as warnings first, then promote).
Either configure `stylelint` or drop it.

### F-8 — Icon-only buttons rely on `title` for their accessible name (LOW–MEDIUM)

Of 66 icon-only `<vaadin-button theme="icon">` elements, 5 have `aria-label`, 60
have `title`, and 4 have neither: `add-edit-access-control.ts:155` and `:163`,
`grid-button-groups/bundle-request-controls.ts:37` and `:54`.

`title` does provide an accessible-name fallback, so the 60 are not broken — but
Vaadin's accessibility guidance recommends an explicit `aria-label`, with
`<vaadin-tooltip slot="tooltip">` for the visible hint, because `title` is not
exposed to touch or keyboard users. `@vaadin/tooltip` is not currently a
dependency.

**Recommendation:** fix the 4 unlabelled buttons now (genuine gap). Treat the
`title` → `aria-label` + `<vaadin-tooltip>` conversion as a separate, optional
piece of work.

### F-9 — `lit-vaadin-helpers` is an unused dependency pulling in Lit 2 (LOW)

`lit-vaadin-helpers@0.3.1` is declared in `dependencies` and imported nowhere in
`src/` or `tests/`. It carries its own nested copy of `lit@2.8.0`,
`lit-html@2.8.0` and `lit-element@3.3.3` alongside the app's `lit@3`.

It is the community predecessor of the official `@vaadin/*/lit` directives the
app has already adopted for dialogs. A second Lit version in the tree is a real
hazard if anything ever imports it.

**Recommendation:** remove from `package.json`.

### F-10 — `CSS.registerProperty` monkey-patch in `vite.config.js` (LOW — investigate)

`vaadinCssRegisterPropertyPatch` rewrites Vaadin's own source at build time,
wrapping every `CSS.registerProperty({…})` call in a `try/catch` because
duplicate registration "throws an uncaught DOMException and can block page
rendering."

The dependency tree contains exactly one copy of `@vaadin/component-base`, so
duplicate registration should not be possible from module duplication. The more
likely cause is the four-entry-point build (`index`, `signin`, `signin-callback`,
`signout-callback`) or dev-server module re-execution.

Patching a vendor's source in the bundler is fragile — it is a regex over
upstream code that will silently stop matching if Vaadin reformats. Worth
confirming whether it is still needed on 25.2.6, and if so raising it upstream.

### F-11 — Overlay theme propagation may now be redundant (LOW — verify)

`src/theme/theme-manager.ts` runs a `MutationObserver` over `document.body`
copying `theme="dark"` onto every element whose tag contains `overlay`, plus a
one-off sweep of existing `vaadin-dialog-overlay` / `vaadin-overlay` elements.

Vaadin 25's Lumo implements dark mode through `color-scheme` and `light-dark()`
in `src/global/color-scheme.css`, keyed off `[theme~='dark']`. Both
`color-scheme` and CSS custom properties inherit, and overlays remain inside the
`<html theme="dark">` subtree, so the cascade should reach them unaided. This
machinery looks like a carry-over from the Vaadin 24 theming model.

**Recommendation:** verify against the running app before removing — this is a
visual-regression risk, not a code-reading one.

### F-12 — 42 components used without being imported (LOW)

`lit-analyzer` reports 42 `no-missing-import` findings: `<vaadin-vertical-layout>`
×14, `<vaadin-button>` ×11, `<vaadin-horizontal-layout>` ×5,
`<vaadin-grid-sorter>` ×5, `<vaadin-grid-sort-column>` ×2, `<vaadin-grid-filter>`
×2, and one each of `<vaadin-icon>`, `<vaadin-details>`, `<vaadin-combo-box>`.

These currently work because some *other* module in the bundle imports them, but
Vaadin's documented rule is that each module imports the components it uses.
The dependency is invisible and breaks the moment the unrelated importer is
deleted or the module is loaded in isolation (tests, code-splitting).

**Recommendation:** add the missing side-effect imports.

---

## 5. What Is Already Correct

Worth recording, so a future review does not re-litigate it:

- **Version currency** — every `@vaadin/*` package is on 25.2.6, the current
  release. Nothing is behind.
- **Vaadin 25 Lumo migration** — done, and done the right way. Lumo is loaded as
  a standalone stylesheet rather than assumed to arrive with the components.
- **Dark mode** — `<html theme="dark">` is the mechanism Lumo 25 supports
  (`[theme~='dark']` in `color-scheme.css`); the `index.html` overrides sit at
  higher specificity than Lumo's own block, so they take effect as intended.
- **Icons** — `<vaadin-icon icon="vaadin:…">` with
  `import '@vaadin/icons/vaadin-icons'` matches current documentation. (The
  `@vaadin/icon` package README shows `name=`, which is *not* in the 25.2.6 type
  definitions — the README is wrong or forward-looking. Do not follow it.)
- **Notifications** — `Notification.show()` throughout, which is the documented
  API, rather than hand-managed `<vaadin-notification>` elements.
- **Dialog Lit renderers** — 8 files already use `dialogRenderer` /
  `dialogFooterRenderer` from `@vaadin/dialog/lit`. This is the pattern F-4 asks
  to extend to grids.
- **No removed/deprecated component APIs in use** — no `overlay-role`, no
  `height-by-rows`; `all-rows-visible` (7 usages) is the current spelling.
- **Node/Lit compatibility** — `lit@3.3.3` satisfies Vaadin's `lit@^3.0.0`. The
  Vaadin 25 "Node 24+" requirement applies to Vaadin's own Flow/Hilla build
  tooling, not to npm consumers; the `@vaadin/*` packages declare no `engines`
  constraint, so `node >=20` is fine.
- **`eslint` passes clean.**

---

## 6. Suggested Sequencing

Ordered by value-to-risk, not by severity alone:

| # | Finding | Effort | Risk |
|---|---------|--------|------|
| 1 | F-5 private imports → public entry points | Trivial | Very low |
| 2 | F-1 remove dead `auto-validate` | Trivial | Very low |
| 3 | F-6 remove dead `filter-property` | Trivial | Very low |
| 4 | F-9 drop `lit-vaadin-helpers` | Trivial | Very low |
| 5 | F-8 label the 4 unlabelled icon buttons | Trivial | Very low |
| 6 | F-12 add missing component imports | Small | Very low |
| 7 | F-7 wire up lit / lit-a11y / wc ESLint plugins | Small | Low (surfaces backlog) |
| 8 | F-3a `paper-toggle-button` → `<vaadin-checkbox>` | Small | Low |
| 9 | F-4 grid renderers → `@vaadin/grid/lit` directives | Large | Medium |
| 10 | F-3b `paper-dialog` → `<vaadin-dialog>` | Large | Medium |
| 11 | F-10 / F-11 verify Vite patch and overlay propagation | Small | Needs runtime check |
| ~~12~~ | ~~F-2 record a decision on `@vaadin/router`~~ | ✅ Done | Migrated to `universal-router` |

Items 1–6 are mechanical dead-code removal and could reasonably go in as a
single change. Items 9 and 10 each warrant their own HLPS/IS.

---

## 7. Unknowns Register

| ID | Unknown | Blocking? | Resolution path |
|----|---------|-----------|-----------------|
| U-1 | Is the `CSS.registerProperty` Vite patch (F-10) still required on 25.2.6? | No | Disable the plugin, run the app across all four entry points |
| U-2 | Does removing overlay theme propagation (F-11) regress dark mode? | No | Visual check of dialogs/combo-box overlays in dark mode |
| ~~U-3~~ | ~~Preferred direction for `@vaadin/router` (F-2)~~ | — | **Resolved 2026-08-02**: migrate to `universal-router` |
| U-4 | Should the `title` → `aria-label` + `<vaadin-tooltip>` conversion (F-8) be in scope? | No | User decision; adds `@vaadin/tooltip` dependency |

---

## 8. Sources

- Vaadin upgrade guide — <https://vaadin.com/docs/latest/upgrading>
- Vaadin 25.0 release notes — <https://vaadin.com/blog/vaadin-25-0-release>
- Vaadin Icons documentation — <https://vaadin.com/docs/latest/components/icons>
- Vaadin styling / Lumo documentation — <https://vaadin.com/docs/latest/styling>
- Installed package type definitions under `src/dorc-web/node_modules/@vaadin/`
  at 25.2.6 (authoritative for API existence claims)
