# JIT Spec: S-002/S-003 — Row-Click Navigation in Monitor Grids

**Status:** APPROVED  
**Created:** 2026-04-10  
**Step IDs:** S-002, S-003  
**Governing Docs:** HLPS (APPROVED), IS (APPROVED), SPEC-S-001 (APPROVED, for pattern reference)

---

## Objective

Replace the ellipsis "detail" button in the ID column of both `page-monitor-requests` and `env-monitor` with row-click navigation. Clicking a row dispatches the same `open-monitor-result` event.

## Branch

`feature/S-002-S-003-monitor-row-click`

## Scope

### Files Modified
- `src/dorc-web/src/pages/page-monitor-requests.ts` — add `@active-item-changed` handler, simplify `idRenderer`, add hover styles
- `src/dorc-web/src/components/environment-tabs/env-monitor.ts` — same changes

### What Changes
- The `idRenderer` is simplified: the ellipsis button is removed, leaving just the ID text.
- An `@active-item-changed` handler on the `<vaadin-grid>` dispatches `open-monitor-result` with the same event detail (`{ request, message: 'Show results for Request' }`, `bubbles: true, composed: true`).
- The handler must immediately reset `activeItem` to `null` to prevent the row from staying "selected" visually and to allow re-clicking the same row.
- CSS `cursor: pointer` is added to grid rows. A subtle hover highlight is added per IS cross-cutting requirements.
- Clicks on interactive elements in other columns (e.g., request-controls buttons) naturally take precedence over `active-item-changed` since button clicks consume the event.

### What Does NOT Change
- The `open-monitor-result` event name, detail shape, or propagation flags.
- The `request-controls` column or any other column renderers.
- Any other component or page.

## Requirements

- **R1:** Clicking a row navigates to detail view via `open-monitor-result` event, dispatched from the component instance (`this.dispatchEvent`) to preserve the existing event propagation path through `shortcuts-store`.
- **R2:** The ellipsis icon (`vaadin:ellipsis-dots-h`) is removed from the ID column.
- **R3:** Rows display `cursor: pointer` and a hover highlight.
- **R4:** Request-controls buttons (cancel, restart, etc.) continue to function.
- **R5:** Re-clicking the same row works (activeItem reset to null).

## Acceptance Criteria

- [ ] AC1: Clicking a row opens the detail view
- [ ] AC2: No ellipsis icon in the ID column
- [ ] AC3: Cursor changes to pointer on row hover
- [ ] AC4: Request-controls buttons still work
- [ ] AC5: Build and lint pass
