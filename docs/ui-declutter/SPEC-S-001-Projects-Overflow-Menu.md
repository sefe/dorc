# JIT Spec: S-001 — Replace Projects Page Action Buttons with Overflow Menu

**Status:** APPROVED  
**Created:** 2026-04-10  
**Step ID:** S-001  
**Governing Docs:** HLPS-UI-Declutter.md (APPROVED), IS-UI-Declutter.md (APPROVED)

---

## 1. Objective

Replace the 6-7 individual icon-only action buttons rendered by `project-controls` on each row of the projects list with a single `vaadin-menu-bar` overflow menu that displays a dropdown of labeled action items when clicked.

## 2. Branch

`feature/S-001-projects-overflow-menu` branched from the current working branch.

## 3. Scope of Change

### Files Modified
- `src/dorc-web/package.json` — add `@vaadin/menu-bar` dependency
- `src/dorc-web/src/components/grid-button-groups/project-controls.ts` — rewrite render method to use menu-bar instead of individual buttons
- `src/dorc-web/src/pages/page-projects-list.ts` — no changes expected if event dispatch from `project-controls` is preserved (per R4). Listed for awareness only; remove from scope if no changes are needed during delivery.

### What Changes
- The `project-controls` component's `render()` method is rewritten to produce a single `vaadin-menu-bar` with one top-level item (ellipsis icon) that opens a sub-menu containing the 7 actions as labeled items.
- Each menu item selection dispatches the same custom event as the current button it replaces.
- The Delete item is conditionally included based on the existing `deleteHidden` property.
- Menu items should include both an icon and a text label for discoverability.

### What Does NOT Change
- The custom event names and their detail payloads. **Note:** event detail shapes vary by action and must be preserved exactly — `open-access-control` dispatches `{ Name: project.ProjectName }` while all other events dispatch `{ Project: project }`. The implementation must not normalise these to a uniform structure.
- The event listeners in `page-projects-list.ts` and `shortcuts-store.ts`.
- The `project` and `deleteHidden` properties on the component.
- Any other page or component.

### Event Dispatch Reference

| Menu Item | Event Name | Detail Payload |
|-----------|-----------|----------------|
| Edit Metadata | `open-project-metadata` | `{ Project: this.project }` |
| Project Access | `open-access-control` | `{ Name: this.project?.ProjectName }` |
| Environments | `open-project-envs` | `{ Project: this.project }` |
| Components | `open-project-components` | `{ Project: this.project }` |
| Reference Data | `open-project-ref-data` | `{ Project: this.project }` |
| Audit | `open-project-audit-data` | `{ Project: this.project }` |
| Delete | `delete-project` | `{ Project: this.project }` |

All events: `bubbles: true, composed: true`.

## 4. Requirements

### R1: Single Menu Trigger
Each project row must display exactly one action trigger — a `vaadin-menu-bar` with a single top-level item showing the ellipsis icon (`vaadin:ellipsis-dots-h`).

### R2: Labeled Menu Items with Icons
The dropdown must contain all current actions as menu items. Each item must include both an icon and a text label for visual consistency with the existing icon buttons and improved discoverability. Icon-only items are not permitted (SC4).

### R3: Conditional Delete
The Delete item must only appear when `deleteHidden` is `false` (i.e., user is admin). This preserves the existing conditional visibility (SC5, C4).

### R4: Event Dispatch Preservation
Selecting a menu item must dispatch the same `CustomEvent` (same event name, same `detail` payload, same `bubbles: true, composed: true`) as the current button click. Events must be dispatched from the `project-controls` component itself (via `this.dispatchEvent`), not from a child element, to preserve the existing event propagation path through the shadow DOM. Parent components must not require any changes (SC6). See the Event Dispatch Reference table in Section 3 for the exact mapping.

### R5: Visual Integration
The menu-bar should be styled to blend with the existing grid row aesthetic — compact, no visible border on the trigger button, consistent colour scheme with `var(--dorc-link-color)`. The Delete menu item should be visually differentiated using `var(--dorc-error-color)` to preserve the existing danger signal from the current delete button.

### R6: Keyboard Accessibility
Vaadin's `menu-bar` component provides built-in keyboard navigation (Arrow keys, Escape to close, Enter to select) and ARIA support. This built-in behaviour is considered sufficient; no additional ARIA customisation is required.

## 5. Test-First Approach

Since this is a Lit web component with no existing unit test infrastructure for components, verification will be manual:

1. **Menu renders:** Each project row shows a single ellipsis button instead of 6-7 buttons.
2. **Menu opens:** Clicking the ellipsis opens a dropdown with 7 labeled items (for admin) or 6 (for non-admin).
3. **Each action works:** Selecting each menu item triggers the expected behaviour:
   - Edit Metadata opens the edit dialog
   - Project Access opens the access control dialog
   - Environments navigates to project environments
   - Components navigates to project components
   - Reference Data navigates to project reference data
   - Audit opens the audit dialog
   - Delete (admin only) prompts for confirmation and deletes
4. **Delete hidden for non-admin:** Log in as a non-admin user; Delete item must not appear in the dropdown.
5. **Build and lint pass:** `npm run build` and `npm run lint` succeed with no errors.

## 6. Commit Strategy

Small, incremental commits:
1. Add `@vaadin/menu-bar` dependency
2. Rewrite `project-controls` to use menu-bar
3. Any styling or integration adjustments

These will be squash-merged to the target branch on completion.

## 7. Acceptance Criteria

- [ ] AC1: Each project row displays exactly one menu trigger button (ellipsis icon)
- [ ] AC2: Dropdown contains all actions with text labels
- [ ] AC3: Delete item is absent for non-admin users
- [ ] AC4: All 7 actions (6 for non-admin) dispatch the correct custom event per the Event Dispatch Reference table and trigger expected behaviour (dialogs open, navigation occurs, delete prompts for confirmation)
- [ ] AC5: Application builds and lints without errors
- [ ] AC6: No visual regressions in the projects list layout
