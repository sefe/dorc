---
name: SPEC-S-011 — Consolidated audit entry in the drawer navbar
description: JIT Specification for F-1 — replace the two indented "Scripts → Audit" and "Variables → Audit" drawer entries with a single top-level "Audit" entry that opens a popover listing all four audit surfaces (Scripts / Variables / Projects / Daemons). No changes to the existing audit pages or dialogs.
type: spec
status: APPROVED
---

# SPEC-S-011 — Consolidated audit entry in the drawer navbar

| Field       | Value                                                   |
|-------------|---------------------------------------------------------|
| **Status**  | APPROVED                                                |
| **Step**    | S-011                                                   |
| **Author**  | Agent                                                   |
| **Date**    | 2026-04-24                                              |
| **Parent**  | FOLLOW-UPS.md F-1                                       |
| **Folder**  | docs/daemons-modernisation/                             |

---

## 1. Context

Today the drawer has two "Audit" entries, each indented under its parent feature:
- Scripts → Audit (navigates to `scripts-audit` page)
- Variables → Audit (navigates to `variables-audit` page)

Projects and Daemons audits currently exist only as row-action dialogs on their list pages. If we add them as drawer entries the same way, the drawer gets four separate indented "Audit" rows — visually noisy.

This SPEC collapses all four into a single top-level **"Audit"** drawer entry that opens a popover menu listing the four audit surfaces.

### Scope
- `src/dorc-web/src/components/dorc-navbar.ts` — one file.
  - Remove the two existing indented `<vaadin-tab>` audit entries.
  - Add one top-level "Audit" `<vaadin-tab>` with `vaadin:calendar-user` icon.
  - Attach a popover or context-menu to it listing four items.
  - Each item navigates via router to the corresponding list or audit page.

Out of scope (per user constraint):
- Any change to the audit pages themselves (`page-scripts-audit.ts`, `page-variables-audit.ts`, `project-audit-data.ts`, `daemon-audit-view.ts`).
- Any new pages / new routes / new components beyond the popover inside the navbar.
- Any change to how project and daemon audits are opened from their list pages (row actions stay as-is per SPEC-S-008 and `page-projects-list.ts`).
- Auto-opening audit on navigation (user confirmed option (a): just navigate to the list page; user clicks a row to open the audit dialog).

### Governing constraints
- Keep the nav pattern visually consistent with the rest of the drawer (icon + label, tab styling).
- No new top-level routes; Projects / Daemons audit menu items navigate to existing `/projects` and `/daemons` routes.
- Minimise dependency additions — prefer components already in `package.json`.

---

## 2. Production Code Change

### 2.1 Popover / menu mechanism

The repo already depends on Vaadin; the idiomatic choice for a popover menu attached to a tab is `@vaadin/context-menu`. Check first whether `@vaadin/context-menu` is in `package.json` — if not, the SPEC author may use a simple on-click toggle with a `<vaadin-popover>` (also a Vaadin component) or a plain `details/summary` approach. Preference order during execution:
1. `@vaadin/context-menu` if already installed.
2. `@vaadin/popover` if already installed.
3. HTML `<details>` with styled `<summary>` — zero new dependencies. Acceptable fallback.

### 2.2 Drawer edit in `dorc-navbar.ts`

Remove these blocks (per current file, lines ~190–213):
```ts
<vaadin-tab>
  <a href="${urlForName('scripts-audit')}">
    <div style="margin-left: 20px; width: 210px">
      <vaadin-icon icon="vaadin:calendar-user" theme="small"></vaadin-icon>
      Audit
    </div>
  </a>
</vaadin-tab>
```
…and the equivalent for `variables-audit`.

Add a new top-level tab after the existing Variables tab (or wherever in the drawer ordering makes most sense — recommend grouping it with Configuration as a "cross-cutting" area):

```ts
<vaadin-tab id="audit-menu-tab">
  <vaadin-icon icon="vaadin:calendar-user" theme="small"></vaadin-icon>
  Audit
  <vaadin-icon icon="vaadin:chevron-down-small" theme="small"></vaadin-icon>
</vaadin-tab>
<!-- context-menu OR popover OR <details> sibling as appropriate -->
```

With four menu items, each rendered as a link to the corresponding list/audit page:
| Label | Route name |
|-------|-----------|
| Scripts Audit | `scripts-audit` |
| Variables Audit | `variables-audit` |
| Projects Audit | `projects` (list page; row action opens the audit dialog) |
| Daemons Audit | `daemons` (list page; row action opens the audit dialog) |

Menu open/close behaviour: click the tab to toggle; click outside or on a menu item to close. Keyboard navigation (Enter/Space opens; Esc closes; arrows cycle) is nice-to-have if the chosen component supports it natively; not a hard requirement.

### 2.3 Routes

No changes. All four destinations are existing routes — `scripts-audit`, `variables-audit`, `projects`, `daemons` — already registered in `src/dorc-web/src/router/routes.ts`.

### 2.4 Styling

Match the existing `<vaadin-tab>` font and icon sizing so the "Audit" entry reads as a peer of Scripts, Variables, Daemons, etc. The dropdown arrow hints that it's expandable. Selected / active tab styling for "Audit" should remain consistent with how other parent tabs behave — it should appear "active" when the user is on any of the four destinations.

If the active-state highlight per destination is complex (multiple routes feeding one tab's active state), a simple approach is to keep the "Audit" tab unselected/neutral at all times — the four children navigate to their own pages, and those pages already have their own highlight state (e.g. the "Projects" tab would highlight when on the projects page, regardless of whether the user got there via the Projects tab or via Audit → Projects). That's acceptable.

---

## 3. Branch

Continues on `feat/649-daemons-modernisation`.

---

## 4. Test Approach

- `npx tsc --noEmit -p tsconfig.json`: zero errors.
- Manual QA:
  - Drawer shows one "Audit" entry with a dropdown arrow, no separate indented "Audit" entries under Scripts or Variables.
  - Click "Audit" → menu opens with four items.
  - Each item navigates to the expected page on click.
  - Menu closes on outside click / item selection.

---

## 5. Commit Strategy

Single commit. Touches one file.

---

## 6. Acceptance Criteria

| ID | Criterion |
|----|-----------|
| AC-1 | `dorc-navbar.ts` no longer contains the two indented `<vaadin-tab>` entries for `scripts-audit` and `variables-audit`. |
| AC-2 | A new top-level "Audit" tab exists with a `vaadin:calendar-user` icon and a visual affordance (chevron, caret) suggesting it expands. |
| AC-3 | Clicking the "Audit" tab opens a menu listing four items: Scripts Audit, Variables Audit, Projects Audit, Daemons Audit. |
| AC-4 | Each menu item navigates to the correct route: `scripts-audit`, `variables-audit`, `projects`, `daemons`. No new routes introduced. |
| AC-5 | No changes to the four audit pages / dialog components (`page-scripts-audit.ts`, `page-variables-audit.ts`, `project-audit-data.ts`, `daemon-audit-view.ts`). |
| AC-6 | `npx tsc --noEmit` exits 0. |
| AC-7 | No changes outside `dorc-navbar.ts` (+ possibly a dependency bump if a new Vaadin sub-package is needed — fallback to `<details>` avoids this). |
