# IS: Dedicated Audit Pages — Implementation Sequence

| Field      | Value                              |
|------------|------------------------------------|
| **Status** | APPROVED                           |
| **Author** | Agent                              |
| **Date**   | 2026-04-26                         |
| **HLPS**   | HLPS-audit-pages.md (APPROVED)     |
| **Folder** | docs/audit-pages/                  |

---

## Step Index

| ID    | Title                                                              | Addresses       | Depends On    |
|-------|--------------------------------------------------------------------|-----------------|---------------|
| S-001 | Cross-record query for daemon audit (persistent source + DTO)      | SD-1, SC-02, SC-05 | —          |
| S-002 | Cross-record query for project audit (persistent source + DTO)     | SD-1, SC-01, SC-05 | —          |
| S-003 | Make `daemonId` / `projectId` optional on existing audit endpoints | SD-2, SC-01, SC-02 | S-001, S-002 |
| S-004 | `page-daemons-audit` page + route + nav rewire                     | SD-3, SC-02, SC-04 | S-001, S-003 |
| S-005 | `page-projects-audit` page + route + nav rewire                    | SD-3, SC-01, SC-04 | S-002, S-003 |
| S-006 | Smoke verify per-record modal audits unchanged                     | SC-03, SC-06    | S-003, S-004, S-005 |

S-001 and S-002 are independent and can run in parallel; S-003 unifies them at the API boundary; S-004 and S-005 are independent again once the API surface is stable.

---

## S-001 — Cross-record query for daemon audit (persistent source + DTO)

### What changes
Add a new method `GetDaemonAudit(int limit, int page, PagedDataOperators operators)` to `IDaemonAuditPersistentSource` and its concrete implementation. The query joins `deploy.DaemonAudit` with `deploy.Daemon` (LEFT JOIN — `DaemonId` is nullable) so the response carries the daemon name on each row, applies `PagedDataOperators` filters/sort, paginates, and returns a `GetDaemonAuditListResponseDto` whose item shape is extended with a `DaemonName` field. The existing `GetDaemonAuditByDaemonId` continues to work and shares helpers with the new method.

### Why it changes
**Addresses SD-1 / SC-02 / SC-05.** The dedicated audit page needs a feed across all daemons, with the daemon name visible per row. The existing per-record method only filters by a single `daemonId` and does not project the daemon name (it doesn't need to — the modal already knows which daemon it's showing).

### Dependencies
None.

### Verification intent
- Persistent-source-level query test: insert `DaemonAudit` rows for two daemons (one of them later deleted), call `GetDaemonAudit` with no filter, confirm rows from both daemons appear, with `(deleted)` (or null/empty per the chosen convention) for the daemon-name of the deleted-daemon's rows.
- Filter test: filter on `Username`, `DaemonName`, `Action` independently.
- Sort + page test: insert > 1 page worth of rows, confirm `Date DESC` is the default sort and that `page=2` returns the next slice.

---

## S-002 — Cross-record query for project audit (persistent source + DTO)

### What changes
Add `GetRefDataAudit(int limit, int page, PagedDataOperators operators)` to `IManageProjectsPersistentSource`. The query joins `deploy.RefDataAudit` with `deploy.Project` (LEFT JOIN — `RefDataAudit.ProjectId` is nullable, `ON DELETE SET NULL`), applies the paging operators, and returns `GetRefDataAuditListResponseDto` extended with `ProjectName` per item. Existing `GetRefDataAuditByProjectId` continues unchanged.

### Why it changes
**Addresses SD-1 / SC-01 / SC-05.** Mirror reasoning to S-001, applied to the project audit feed.

### Dependencies
None.

### Verification intent
- Same shape as S-001's tests, applied to project audit and `deploy.Project`.
- Specifically cover the deleted-project case: a `RefDataAudit` row whose `ProjectId` was nulled by the FK's `ON DELETE SET NULL` must appear in the cross-record feed (it's part of the audit history) with the project-name column reflecting the absent project.

---

## S-003 — Make `daemonId` / `projectId` optional on existing audit endpoints

### What changes
Both `DaemonAuditController.Put` and `RefDataProjectAuditController.Put` accept the Id parameter as `int?` (or accept its absence via the model binder default). When the Id is supplied, the controller delegates to the existing per-record persistent-source method. When the Id is absent, it delegates to the new cross-record method from S-001 / S-002. The OpenAPI surface advertises one endpoint per domain with an optional Id parameter; the SDK regeneration produces a single method per endpoint.

### Why it changes
**Addresses SD-2 / HLPS U-2 (Option A) / SC-01 / SC-02.** Per HLPS Section 5, this is the preferred shape: one endpoint per audit domain whose behaviour scales from "single record" to "all records" via an optional filter. Simpler SDK, simpler swagger, no parallel endpoints.

### Dependencies
S-001 and S-002 (the new persistent-source methods are what the optional-Id branch delegates to).

### Verification intent
- Controller-level test: PUT without `daemonId` returns the cross-record feed; PUT with `daemonId=N` returns only that daemon's history. Same for project audit.
- SDK regeneration produces a single method per endpoint with an optional Id parameter; existing TypeScript callers in `daemon-audit-view` and `project-audit-data` compile against the regenerated SDK without source changes.
- The two existing per-record modal callers continue to work end-to-end after the change (this is the precondition for S-006).

---

## S-004 — `page-daemons-audit` page + route + nav rewire

### What changes
Add `src/dorc-web/src/pages/page-daemons-audit.ts`, modelled structurally on `page-scripts-audit.ts`. The grid uses `.dataProvider` to call the regenerated `DaemonAuditApi` PUT endpoint without `daemonId`. Columns: Daemon, User, Action, Date, From, To. Header-renderer filters on Daemon-name, Username, Action (mirroring scripts-audit's filter pattern). Cell-part-name generator from `daemon-audit-view` is re-used for action-based row colouring (Create/Delete styles). A new route `/daemons/audit` named `daemons-audit` is added to `routes.ts`. The `Daemons Audit` sub-tab in `dorc-navbar.ts` switches from `urlForName('daemons')` to `urlForName('daemons-audit')`.

### Why it changes
**Addresses SD-3 / SC-02 / SC-04.** Closes the gap that the navbar Daemons-Audit entry currently routes back to the regular Daemons listing.

### Dependencies
S-001 (the cross-record persistent source) and S-003 (the SDK shape that `dataProvider` calls).

### Verification intent
- Route resolution: navigating to `/daemons/audit` resolves to `page-daemons-audit`, not `page-daemons-list`.
- Navbar wiring: clicking "Daemons Audit" lands on `/daemons/audit`; clicking the main "Daemons" tab still lands on `/daemons`. The two are no longer the same.
- Per-record modal regression check: the row-level "view audit history" button on `page-daemons-list` still opens the per-daemon modal and shows that daemon's history only.

---

## S-005 — `page-projects-audit` page + route + nav rewire

### What changes
Add `src/dorc-web/src/pages/page-projects-audit.ts` modelled on `page-scripts-audit.ts`, calling the project-audit endpoint without `projectId`. Columns: Project, User, Action, Date, JSON-value renderer (the project audit stores `Json`, not `From`/`To` — the renderer mirrors `project-audit-data.ts`'s value column). New route `/projects/audit` named `projects-audit`. The `Projects Audit` sub-tab in `dorc-navbar.ts` switches from `urlForName('projects')` to `urlForName('projects-audit')`.

### Why it changes
**Addresses SD-3 / SC-01 / SC-04.** Mirror of S-004 for projects.

### Dependencies
S-002 and S-003.

### Verification intent
- Same shape as S-004's verification, applied to projects.
- Specifically: the navbar's `Projects Audit` link no longer routes to the regular `/projects` list.

---

## S-006 — Smoke verify per-record modal audits unchanged

### What changes
No code change. A short verification pass confirms that:
1. `page-projects-list` row → "Audit" button still opens `project-audit-data` in `hegs-dialog` and shows only that project's audit history.
2. `page-daemons-list` row → "View audit history" button still opens `daemon-audit-view` in `paper-dialog` and shows only that daemon's audit history.
3. The data shown in (1) and (2) matches what filtering by the same Project/Daemon shows on the new global pages.

### Why it changes
**Addresses SC-03 / SC-06.** HLPS commits to no regression in the per-record modals. After S-003 changes the SDK shape, this is the natural place to confirm the existing modals still bind correctly.

### Dependencies
S-003, S-004, S-005.

### Verification intent
- Manual UI smoke is sufficient (modals are well-covered by their own component tests already; this step is about end-to-end after the SDK regen).
- If any data mismatch is observed between (1)/(2) and the global page filtered to the same record, treat it as a defect on whichever side has the inconsistency.
