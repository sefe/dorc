# MIGRATION: `@vaadin/router` → `universal-router`

| Field       | Value                                    |
|-------------|------------------------------------------|
| **Status**  | COMPLETE — for review                    |
| **Author**  | Agent                                    |
| **Date**    | 2026-08-02                               |
| **Folder**  | `docs/vaadin-alignment/`                 |
| **Resolves**| AUDIT-vaadin-25-alignment.md, finding F-2 |
| **Scope**   | `src/dorc-web/src/router/` and its 13 imperative-navigation call sites |

---

## 1. Why

Vaadin's own upgrade guide states that "the Vaadin Router library is no longer
actively maintained." `@vaadin/router@2.0.1` is its terminal release.

### 1.1 Why not `@lit-labs/router`

The audit originally proposed `@lit-labs/router`. Checking the registry before
starting showed that recommendation was wrong:

- **Staler than the thing it replaces.** `@lit-labs/router@0.1.4` was published
  2025-02-14; `@vaadin/router@2.0.1` was published 2025-11-14.
- **Pre-1.0 Lit Labs**, carrying an explicit "may receive breaking changes or
  stop being supported" warning.
- **No general navigation API.** Its own docs say `goto()` "does not navigate
  parent routes, so it isn't (yet) a general page navigation API." DOrc
  navigates imperatively from 13 call sites that cross top-level and nested
  route boundaries — every one would have needed a hand-written shim.
- **Needs a `URLPattern` polyfill** for non-Chromium browsers.

### 1.2 Why `universal-router`

- v10.0.3, published 2026-01-16; 48,515 weekly downloads.
- **It is the resolver `@vaadin/router` was built on.** The existing route
  table's nested `children`, `action` redirects and `:param` syntax carry over
  with three small changes (§3).
- Framework-agnostic, so what it does *not* provide — outlet rendering, link
  interception, the `location` object — becomes owned, tested application code
  instead of a dependency. That is the part that was unmaintained; it is now
  ~380 lines under our control with 25 tests against it.

---

## 2. Shape of the change

`src/router/` went from two modules to five, splitting concerns that Vaadin
Router had bundled together:

| Module | Responsibility |
|--------|----------------|
| `routes.ts` | The route table. Unchanged in structure; see §3 for the three edits. |
| `route-config.ts` | *(new)* Route and location types, independent of the routing library. |
| `route-resolution.ts` | *(new)* Wraps `universal-router`; turns a pathname into either a redirect or a chain of component tags. |
| `route-outlet.ts` | *(new)* Renders a resolved chain into nested light-DOM elements, reusing what has not changed. |
| `router.ts` | History integration, link interception, `navigate`, `urlForName`, `location`. |

### 2.1 Chain resolution

Routes are matched by `universal-router`, which populates `route.parent` as it
traverses. A matched leaf is walked back up those links to produce the chain of
components to nest — `dorc-app` → `page-environment` →
`page-environment-components` → `env-servers`. A route that has `children` is
treated as a layout: it contributes its component to the chain but does not
terminate resolution.

`parent` links are also pre-computed at construction. universal-router only sets
them lazily during traversal, so the not-found fallback would otherwise render
without its layout when it is the very first thing matched. There is a
regression test for exactly this.

### 2.2 Element reuse

This is the behaviour the app most depends on and the easiest to get wrong.
`RouteOutlet` reproduces Vaadin Router's rule:

- Elements up to the first route that differs from the previous navigation are
  **kept**, and only their `location` is refreshed.
- The diverging tail is removed and recreated.
- `onAfterEnter` fires **only for newly created elements**, after the whole
  chain is attached.

Divergence is decided by **route identity**, not tag name — matching Vaadin.
Two routes that render the same component (`/deploy` and the `/` default both
render `page-deploy`) are distinct views, and reusing the element across them
would carry state over.

Without this, `<dorc-app>` would be torn down and rebuilt on every navigation,
losing its sidebar state, splitter listener and keyboard shortcuts.

---

## 3. Route table changes

Three edits, all forced by `universal-router` using **path-to-regexp v8** where
Vaadin Router used v6:

1. **Catch-all**: `path: '(.*)'` → `path: '/*notFound'`. v8 requires named
   wildcards; the bare `(.*)` form throws `Missing parameter name`.

2. **Nested index route**: `path: '/'` → `path: ''` for the
   `/environment/:id/components` default-tab redirect. Under v8 a `'/'` path
   only matches when the URL has a **trailing slash**, but the tab navigation
   produces `/environment/DEV1/components` without one. Left unchanged, opening
   the Components tab would have fallen through to the not-found page. This was
   caught by a test, not by reading — see §5.

3. **Redirect actions**: `commands.redirect(path)` → returning
   `{ redirect: path }`. universal-router has no `commands` argument; the
   convention is now declared in `route-config.ts` and handled by the router's
   resolve loop, which also guards against redirect cycles (`MAX_REDIRECTS`).

---

## 4. Call-site changes

| Before | After | Sites |
|--------|-------|-------|
| `Router.go(path)` | `navigate(path)` | 13 |
| `import { Router } from '@vaadin/router'` | `import { navigate } from '.../router/router'` | 9 files |
| `router.setRoutes(routes as any)` | `router.setRoutes(routes)` | 2 (the `any` cast is no longer needed) |
| `vaadin-router-location-changed` | `dorc-router-location-changed` (exported as `LOCATION_CHANGED_EVENT`) | 2 listeners + 1 test |

`urlForName`, `router.location` and the `location` property on routed
components keep their existing signatures, so the ~30 `urlForName` call sites,
the three `onAfterEnter` implementations and `PageElement` needed no changes
beyond swapping the `Route` type import.

### 4.1 Deliberate non-goals

- **`onBeforeEnter` / `onBeforeLeave` are not implemented.** Nothing in the
  codebase uses them. Adding unused lifecycle plumbing would be speculative.
  If a route guard is needed later, the hook belongs in `RouteOutlet.render`.
- **Route animations are not implemented.** Vaadin Router supported them; this
  app never used them.

---

## 5. Verification

**Unit — 25 new tests** (`tests/router/`), run in-browser like the rest of the
suite:

- `route-resolution.test.ts` (16) — layout nesting, root path, three-level
  chains, ancestor parameter inheritance, URL decoding, prefix-overlap
  ordering, both redirect forms, trailing-slash handling, not-found fallback
  (including the cold-start regression case), metadata, and `urlForName`
  including its encoding and unknown-name behaviour.
- `route-outlet.test.ts` (9) — light-DOM nesting, per-element location with its
  own route, ancestor reuse, tail replacement, subtree teardown, location
  refresh on reuse, `onAfterEnter` firing only for new elements and only once
  the chain is attached, and chain growth.

**Full suite:** 143 tests across 14 files pass. `eslint` clean, `tsc --noEmit`
clean for both `tsconfig.json` and `tsconfig.test.json`, `npm run build`
succeeds.

**End-to-end against the running app.** The unit tests do not cover the real
route table or link interception, so the dev server was driven with Playwright
against stubbed API responses. All 15 checks passed:

- root renders `dorc-app > page-deploy`
- deep link to `/environments` renders the right page
- `/environment/DEV1/components/servers` builds the full four-element chain
- `/environment/DEV1/components` redirects to `.../servers`
- `/about` redirects to `/analytics`
- an unmatched path renders `page-not-found` inside the layout
- a `dorc-navbar` link **inside a shadow root** is intercepted and routed
- that click does not reload the page
- `<dorc-app>` is the same element instance after navigating
- the leaf swaps to `page-projects-list`
- browser Back returns to `/deploy` and re-renders it, without reloading
- an external `https://example.com` link is left to the browser

The shadow-DOM case matters: nearly all of this app's navigation lives in
`dorc-navbar`'s shadow root, so the click handler resolves anchors through
`event.composedPath()` rather than `event.target`.

---

## 6. Residual risk

| Risk | Assessment |
|------|------------|
| A path in the route table behaves differently under path-to-regexp v8 than v6 | Medium → mitigated. The `'/'`-vs-`''` case was real and caught by test. The remaining paths are literal segments and simple `:id` params, which are identical between versions, and every route is covered by either a unit test or an end-to-end check. |
| Element-reuse divergence differs subtly from Vaadin's | Low. Vaadin also compared `path` and consulted an element-reusability check; both are redundant here because route identity already implies path identity, and no route in this table supplies a pre-built element. |
| `onAfterEnter` not re-firing for reused elements | Low — this **matches** Vaadin. Note it means `page-environment-components` does not re-sync its tab index on Back/Forward between sibling tabs. That behaviour is pre-existing, not introduced here; flagged rather than fixed to keep the migration behaviour-preserving. |
| Scroll restoration | Not implemented before, not implemented now. |

## 7. Follow-ups (not done here)

- `src/helpers/render-page-not-found.ts` is dead code with no callers, and its
  `TODO` links to a `vaadin-router` issue that no longer applies. Left in place
  as it is outside this migration's scope.
- The remaining six private-path Vaadin imports from finding F-5 are unrelated
  to routing and still stand.
