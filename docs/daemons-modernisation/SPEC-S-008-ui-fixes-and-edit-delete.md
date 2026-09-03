---
name: SPEC-S-008 — UI fixes + Edit/Delete + daemon audit view
description: JIT Specification for S-008 — DF-4/5/6 fixes in add-daemon.ts + daemon-controls.ts; DF-11 Edit/Delete row actions on page-daemons-list.ts (role-gated); new edit-daemon.ts and daemon-audit-view.ts components.
type: spec
status: APPROVED
---

# SPEC-S-008 — UI fixes + Edit/Delete + daemon audit view

| Field       | Value                                                   |
|-------------|---------------------------------------------------------|
| **Status**  | APPROVED                                                |
| **Step**    | S-008                                                   |
| **Author**  | Agent                                                   |
| **Date**    | 2026-04-24                                              |
| **IS**      | IS-daemons-modernisation.md (APPROVED)                  |
| **HLPS**    | HLPS-daemons-modernisation.md (APPROVED)                |
| **Folder**  | docs/daemons-modernisation/                             |

---

## 1. Context

### What this step addresses
Four UI defects and one surface-addition, completing SC-06 and SC-07 at the UI layer:

- **DF-4** — `add-daemon.ts` `maxlength` is 50, not aligned with DB width 250 (U-6 resolution).
- **DF-5** — `daemon-controls.ts` `userEditable` logic is inverted (buttons disable when user *can* edit).
- **DF-6** — `add-daemon.ts` AccountName and Type are `.readonly="${true}"` with hard-coded defaults. Users can't set real values.
- **DF-11** — `page-daemons-list.ts` has no Edit or Delete row actions. API exposes PUT/DELETE; UI is read-only.
- **Audit view** — S-007 added `DaemonAuditController` GET `/DaemonAudit`. A UI component exposes the history per daemon.

### Scope
Existing files to modify:
- `src/dorc-web/src/components/add-daemon.ts` — DF-4, DF-6.
- `src/dorc-web/src/components/grid-button-groups/daemon-controls.ts` — DF-5.
- `src/dorc-web/src/pages/page-daemons-list.ts` — DF-11 (Edit/Delete row actions, role-gated), wire the two new components.

New components:
- `src/dorc-web/src/components/edit-daemon.ts` — edit-mode dialog, submits PUT.
- `src/dorc-web/src/components/daemon-audit-view.ts` — per-daemon paged audit list.

Out of scope:
- **Audit pages UI consolidation** (FOLLOW-UPS F-1) — daemon-audit-view mirrors `project-audit-data` for internal consistency with today's per-domain pattern. Unifying is deferred.
- Any UI for HLPS SD-3's new `ErrorMessage` field on `ServiceStatusApiModel` (now `DaemonStatusApiModel.ErrorMessage`) — the field is in the wire contract but the UI's rendering of "unreachable: <reason>" is a nice-to-have and is folded into the existing status rendering in `application-daemons.ts` as a small enhancement; not a separate SPEC item.
- Test coverage: the UI has no unit-test infrastructure for Lit components in this repo; manual QA per SC-07 is the gate. The `daemon-controls` getter logic is testable in isolation and a small unit test is added for the four-state matrix (DF-5 regression).

### Governing constraints
- **HLPS SC-06**: Edit visible to PowerUser/Admin; Delete visible to Admin only. AccountName and Type editable. Column-width caps aligned on 250.
- **HLPS SC-07**: manual QA covers the end-to-end flows with sensible 403/409 messages.
- **HLPS U-7 resolution** (via SD-6): Delete requires a confirmation dialog and warns when the daemon is attached to any servers.
- **Role-helper pattern**: mirror `page-projects-list.ts` — `GlobalCache.getInstance().allRolesResp` → `isAdmin` / `isPowerUser` properties; gate template via `?hidden="${!this.isAdmin}"` or equivalent.
- **Audit-view pattern**: mirror `components/project-audit-data.ts` for grid + paged-data-provider + filter/sort — duplication for now, consolidation is F-1.

---

## 2. Production Code Change

### 2.1 `add-daemon.ts` — DF-4 + DF-6

- **DF-4**: `maxFieldLength` private `50` → `250`.
- **DF-6**: Remove `.readonly="${true}"` on the `account-name` and `service-type` text fields. Leave the `getEmptyDaemon()` defaults in place (`"Local System Account"` / `"Windows Service"`) — they become initial suggestions, not locks. Add a placeholder text so users know the defaults can be overwritten: e.g. `placeholder="Local System Account"` — but leaving the current default value in place is fine too.

No other changes to this file. The form validation logic stays.

### 2.2 `daemon-controls.ts` — DF-5

In the three getters (`startDisabled`, `stopDisabled`, `restartDisabled`), change `this.userEditable === true` to `this.userEditable !== true`. No other changes — property name stays `userEditable` (rename churn out of scope per S-006 / Round 1 decision).

Add a small unit test in `src/dorc-web/test/daemon-controls.test.ts` (new file if no test infrastructure exists for this component — or document "manual QA gate" if no unit-test harness exists in the repo). Matrix:

|   | userEditable=true | userEditable=false |
|---|---|---|
| Status=running | startDisabled=true | startDisabled=false |
| Status=stopped | stopDisabled=true | stopDisabled=false |

(Plus the symmetric cases for `stopDisabled` and `restartDisabled`.)

Execute-time check: if `src/dorc-web/test/` doesn't exist or has no test runner config, document the matrix in the PR description and verify by hand.

### 2.3 New `edit-daemon.ts`

A Lit component modelled on `add-daemon.ts` but in edit mode:
- Takes a `DaemonApiModel` input (the row being edited).
- Prefills all four fields.
- All fields editable (including Name — backend rejects with 409 if rename conflicts).
- On Save, calls `RefDataDaemonsApi.refDataDaemonsPut({ id: model.Id, daemonApiModel: model })`.
- On 200, dispatches a `daemon-updated` event so the parent page can refresh and close the dialog.
- On 403 / 409, surfaces the error message from the response body into a label visible to the user.
- On other errors, shows a generic "Error updating daemon: <message>".

Visual layout mirrors `add-daemon.ts` for consistency.

### 2.4 New `daemon-audit-view.ts`

A Lit component modelled on `project-audit-data.ts` (structural parallel):
- Takes a `daemonId: number` input.
- Wraps a Vaadin grid with a data-provider that calls `DaemonAuditApi.daemonAuditPut({ daemonId, page, limit, pagedDataOperators })`.
- Columns: Username, Date (desc-sorted default), Action (with color coding: Create=green, Delete=red, Attach=muted, Detach=muted, Update=neutral), FromValue / ToValue rendered via `hegs-json-viewer` (existing component used by `project-audit-data`).
- Filter + sort via vaadin-grid-filter headers, same pattern as project audit.
- Loading spinner while the initial page loads.

Displayed in a dialog (`<paper-dialog>` or `<hegs-dialog>` to match the project audit pattern) triggered from the daemon list's row-action Audit button.

### 2.5 `page-daemons-list.ts` — DF-11 + wire new components

- **Role lookup**: import `GlobalCache`, add `@property isAdmin` and `@property isPowerUser`, subscribe to `allRolesResp` (pattern copied from `page-projects-list.ts` lines 69–87).
- **Row action column**: new `<vaadin-grid-column>` with a renderer that emits three buttons:
  - **Audit** (icon: `vaadin:info-circle`) — opens `daemon-audit-view` dialog for the clicked daemon. Visible to all authenticated users.
  - **Edit** (icon: `vaadin:edit`) — opens `edit-daemon` dialog prefilled from the row. Visible when `isAdmin || isPowerUser`. Hidden otherwise.
  - **Delete** (icon: `vaadin:trash`) — opens a confirmation dialog; when confirmed calls `RefDataDaemonsApi.refDataDaemonsDelete({ id })`. Visible when `isAdmin`. Hidden otherwise.
- **Delete confirmation dialog**: use Vaadin's `<vaadin-confirm-dialog>` (or the existing `hegs-dialog` used elsewhere) with the message *"Delete daemon '<Name>'? This cannot be undone."* If the daemon has any entry in its attached-server list (from `ServerDaemonsApi.serverDaemonsServerIdGet` — probe on open), include a warning: *"This daemon is currently attached to N server(s). Deleting will detach it from all of them."*

  The "attached-server probe on open" is a nice-to-have. If querying the list for every daemon on page-load is wasteful, fetch just before showing the confirmation — the cost is one extra GET per delete click, which is acceptable.

- **Wire event handlers**: `daemon-updated` (from edit-daemon) and `daemon-deleted` (new custom event from the Delete path) both call `this.getDaemonsList()` to refresh and close any open dialog.

- **Sort column update**: the existing `path="ServiceType"` stays — `DaemonApiModel.ServiceType` C# property is preserved (Round 1 decision / C-02), so the TS client's `.ServiceType` remains. No change to that.

### 2.6 Status-error surfacing — tiny enhancement (not a separate section)

In `application-daemons.ts` where the status column renders, if `daemon.ErrorMessage` is non-null, show a warning icon with the message as a tooltip instead of (or alongside) the status value. This is a one-line renderer tweak that completes the DF-8 user-visible half. Keep it narrow — just add a `title="${daemon.ErrorMessage}"` on the status span, or prepend `⚠` when `ErrorMessage` is present.

---

## 3. Branch

Continues on `feat/649-daemons-modernisation`.

---

## 4. Test Approach

### Rationale
Lit components don't have existing unit-test infrastructure in this repo. TypeScript compile via `npx tsc --noEmit -p tsconfig.json` is the automated gate. Behavioural coverage is manual QA per HLPS SC-07.

### Test 1 — TypeScript compile
`npx tsc --noEmit -p tsconfig.json` exits 0 after all changes.

### Test 2 — DF-5 matrix (manual, from the UI)
Navigate to a daemon's control buttons with a user account that has edit rights; verify buttons are **enabled**. Switch to a read-only user; verify buttons are **disabled**. Document the test steps in the PR description.

### Test 3 — DF-11 role visibility (manual)
Log in as a non-admin / non-PowerUser → visit the daemons list → Edit and Delete buttons absent; Audit button present.
Log in as a PowerUser → Edit and Audit present; Delete absent.
Log in as an Admin → all three present.

### Test 4 — End-to-end daemon lifecycle (manual, SC-07)
Create → rename via Edit → change AccountName and Type via Edit → attach to a server → detach → delete. At each step, open the Audit view and confirm the new row is there with the expected Action and payload.

### Test 5 — Error messages
As a non-privileged user, attempt each mutating action via a crafted API call (browser dev-tools). Verify 403 with a readable body message. Create two daemons with the same Name; verify 409 with `"A daemon with Name '...' already exists"`.

### Regenerated TS client
Must not regress: `DaemonStatusApi`, `DaemonAuditApi`, `RefDataDaemonsApi`, `ServerDaemonsApi` all resolvable from `'../apis/dorc-api'`.

---

## 5. Commit Strategy

Two natural commits:
1. Small UI fixes (DF-4, DF-5, DF-6) + `application-daemons.ts` error-message tooltip.
2. New components (`edit-daemon.ts`, `daemon-audit-view.ts`) + `page-daemons-list.ts` wiring.

One commit is acceptable if the implementer prefers. No value in more than two.

---

## 6. Acceptance Criteria

| ID   | Criterion |
|------|-----------|
| AC-1 | `add-daemon.ts` `maxFieldLength` is `250` (DF-4). |
| AC-2 | `add-daemon.ts` AccountName and Type text fields are not `.readonly`. Existing defaults preserved as initial values (DF-6). |
| AC-3 | `daemon-controls.ts` getters use `this.userEditable !== true` (DF-5). Property name unchanged. |
| AC-4 | `edit-daemon.ts` exists as a new component with prefilled fields, PUT on save, `daemon-updated` event on success, error-body rendering on 403/409. |
| AC-5 | `daemon-audit-view.ts` exists as a new component. Queries `DaemonAuditApi.daemonAuditPut`. Columns: Username, Date (default desc), Action (color-coded), Value (JSON viewer). |
| AC-6 | `page-daemons-list.ts` has an action column with Audit / Edit / Delete buttons. Edit visible iff `isAdmin \|\| isPowerUser`. Delete visible iff `isAdmin`. Audit visible to all authenticated. Role lookup mirrors `page-projects-list.ts`. |
| AC-7 | Delete action triggers a confirmation dialog. Confirmation text includes the daemon Name and, when the daemon is attached to ≥1 servers, a warning with the count. |
| AC-8 | `application-daemons.ts` status cell surfaces `ErrorMessage` when non-null (tooltip or warning icon). Existing status-string rendering unchanged for success cases. |
| AC-9 | `npx tsc --noEmit -p tsconfig.json` exits 0 for `src/dorc-web`. |
| AC-10 | Manual QA checklist from §4 Tests 2–5 is completed and documented in the PR description. |
| AC-11 | No changes outside `src/dorc-web/src/components/`, `src/dorc-web/src/pages/page-daemons-list.ts`, and the auto-generated `src/dorc-web/src/apis/dorc-api/` where already regenerated in S-005 and S-007. |
