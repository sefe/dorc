# DECISION — Narrow-mode bar mechanism (S-001a)

| Field | Value |
|---|---|
| Status | **APPROVED** (auto-pilot: blanket user approval 2026-08-02 covers this gate) |
| Governing IS | `IS-responsive-record-view.md` S-001a |
| Evidence | `src/dorc-web/tests/helpers/bar-mechanism-spike.test.ts` (in-browser, passing) |

## Decision

**Header-hosted** (HLPS §3.4 default confirmed): at narrow width the single
visible list column carries a `headerRenderer` hosting the bar; sorters and
filter fields inside it remain connected and drive the grid's
`sortOrders`/`filters` normally.

## Evidence

The spike renders a grid with a bar column plus two wide columns, hides the
wide columns, and asserts: (1) a `vaadin-grid-sorter` inside the visible
column's `headerRenderer` remains `isConnected` and its click delivers
`{path, direction}` to the `dataProvider` — the sort survives; (2) the control
case: with wide columns hidden, the bar sorter is the **only** connected
sorter — confirming the R2b-verified hazard (hidden columns' cell content is
removed from the DOM; `_getActiveSorters()` filters on `isConnected`) that the
naive floating-bar design would hit.

## Consequences

- Filter mirroring (U-14b): wide `headerRenderer` inputs and the bar are both
  *renderings of the same host state* (the views' existing debounced filter
  fields); no second state store is introduced.
- Header-row visibility: the bar column's `headerRenderer` keeps the header
  row visible at narrow width by Vaadin's own rule — no CSS forcing.
- The fallback (controller-managed `sortOrders` at `dataProvider` level) is
  not needed and is not built.

## Outcome

Path: header-hosted. Halt-and-escalate not triggered. S-001b may proceed.
