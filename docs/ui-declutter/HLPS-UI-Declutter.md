# HLPS: DOrc Web UI Declutter - Icon Convention Fix & Projects Action Consolidation

**Status:** APPROVED — Pending user approval  
**Created:** 2026-04-10  
**Author:** Agent (with user direction)

---

## 1. Problem Statement

The DOrc web UI has two related usability problems:

### 1.1 Incorrect Ellipsis Icon Convention
Four views use the horizontal ellipsis icon (`vaadin:ellipsis-dots-h`) as a "navigate to detail" button. This violates the universally understood UI convention where ellipsis (three dots) means "more actions / overflow menu." Users familiar with modern web and mobile interfaces will expect an ellipsis to open a menu, not navigate to a detail page.

**Affected locations:**
| File | Component | Current Behaviour |
|------|-----------|-------------------|
| `page-monitor-requests.ts` | `idRenderer` | Navigates to detailed deployment request results |
| `component-deployment-results.ts` | `_logRenderer` | Opens the full log for a deployment result |
| `component-previous-attempts.ts` | `componentLogRenderer` | Opens the full log for a previous attempt |
| `env-monitor.ts` | `idRenderer` | Navigates to detailed deployment request results |

In all four cases the ellipsis button is a single-action "drill into detail" button — none open a menu.

### 1.2 Cluttered Projects List Action Buttons
The projects list page (`page-projects-list.ts`) renders 7 icon-only action buttons per row via the `project-controls` component:

1. Edit Metadata (pencil)
2. Project Access (lock)
3. Environments (records)
4. Components (package)
5. Reference Data (curly-brackets)
6. Audit (calendar-user)
7. Delete Project (trash — admin only)

With dozens of projects visible, this creates a dense wall of repeated icons that is visually overwhelming, hard to scan, and difficult to use (small targets, no labels, require memorising icon meanings). Non-admin users see 6 buttons; admins see up to 7.

## 2. Constraints

- **C1:** The Vaadin component library (v25) is the established UI framework. Any solution must use Vaadin components or native web platform features — no additional UI library dependencies.
- **C2:** `@vaadin/context-menu` and `@vaadin/menu-bar` are **not** currently in `package.json`. A solution may add one of these if needed, but the choice must be justified.
- **C3:** The existing custom event dispatch pattern (e.g., `open-project-envs`, `open-monitor-result`) used by the action buttons must be preserved. Parent components listen for these events to trigger dialogs and navigation.
- **C4:** The delete action is conditionally hidden based on `isAdmin`. Any consolidated menu must respect this conditional visibility.
- **C5:** No `active-item-changed` row-click handlers exist in any grid currently, so there are no conflicts with introducing row-click behaviour.

## 3. Proposed Solution (High Level)

### 3.1 Fix the Ellipsis Convention
In the four affected views, replace the ellipsis "detail" button with **row-click navigation**. The row itself becomes the clickable affordance for drilling into detail. The standalone ellipsis icon button is removed entirely.

This approach:
- Is the most intuitive interaction pattern (users expect clicking a row to see its details).
- Removes an unnecessary button from every row in 4 views.
- Frees up the ellipsis icon to be used correctly as an overflow menu trigger.

**Design intent:** Row-click navigation must not prevent normal text selection or interfere with other interactive elements (e.g., links) within row cells. If row-click cannot be made conflict-free in a specific view (e.g., log preview cells with selectable text), the fallback is to retain a non-ellipsis icon button (such as `vaadin:chevron-right`) for that specific view rather than dropping the requirement.

### 3.2 Consolidate Projects Page Actions
Replace the 7 individual icon buttons in `project-controls` with a **single ellipsis overflow menu** (`vaadin:ellipsis-dots-h`) that opens a dropdown listing the actions with text labels. This correctly uses the ellipsis as "more actions."

This approach:
- Reduces visual clutter from 7 buttons to 1 per row.
- Provides text labels alongside icons in the menu, improving discoverability.
- Correctly applies the universal ellipsis convention.
- Requires adding `@vaadin/context-menu` or `@vaadin/menu-bar` as a dependency.

## 4. Success Criteria

- **SC1:** No usage of `vaadin:ellipsis-dots-h` as a single-action navigation button remains in the codebase.
- **SC2:** The four affected detail-navigation views use row-click to navigate instead of a button.
- **SC3:** The projects list page displays a single overflow menu button per row instead of 6-7 individual action buttons. (Contingent on U1 resolution — the specific menu component will be determined during IS phase.)
- **SC4:** All menu items in the overflow menu include text labels. Icons may be paired with text labels but icon-only menu items are not permitted.
- **SC5:** The delete action remains conditionally visible based on admin status.
- **SC6:** All existing navigation and action event dispatches continue to function identically.
- **SC7:** The application builds and lints successfully with no regressions.

## 5. Out of Scope

- Restyling or redesigning any other pages or components beyond the identified scope.
- Changing the navigation structure or routing of the application.
- Modifying the backend API.

## 6. Unknowns Register

| ID | Description | Owner | Blocking? |
|----|-------------|-------|-----------|
| U1 | Which Vaadin component is best for the overflow menu — `context-menu` or `menu-bar`? Both are available in Vaadin 25 but neither is currently in the project. | Agent | No (resolvable during IS/spec phase) |
| U2 | Do the monitor/deployment-results views have other clickable elements in rows that might conflict with row-click navigation? | Agent | No (investigated — grep for `active-item-changed` across `src/dorc-web` returned zero matches; no existing row-click handlers. Log columns contain links in separate cells; potential interaction with row-click in those cells is tracked in U3) |
| U3 | In `component-deployment-results.ts` and `component-previous-attempts.ts`, the ellipsis button sits inside a log preview cell alongside truncated text. Row-click here may need to be scoped to avoid conflict with text selection. | Agent | No (will be addressed in JIT spec. Fallback: retain a non-ellipsis icon button for these views if row-click proves incompatible — see Section 3.1 design intent) |
