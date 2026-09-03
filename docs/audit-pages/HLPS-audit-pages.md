# HLPS: Dedicated Audit Pages for Projects and Daemons

| Field      | Value                          |
|------------|--------------------------------|
| **Status** | APPROVED                         |
| **Author** | Agent                          |
| **Date**   | 2026-04-26                     |
| **Folder** | docs/audit-pages/              |

---

## 1. Problem Statement

The dorc-web nav drawer offers four "Audit" entries:

| Sub-tab           | Currently routes to       | Behaviour                                    |
|-------------------|---------------------------|----------------------------------------------|
| Scripts Audit     | `/scripts/audit`          | Dedicated audit page (`page-scripts-audit`)  |
| Variables Audit   | `/variables/audit`        | Dedicated audit page (`page-variables-audit`)|
| **Projects Audit**| **`/projects`**           | **Regular projects list — same as the main Projects tab** |
| **Daemons Audit** | **`/daemons`**            | **Regular daemons list — same as the main Daemons tab**   |

Per-record audit views exist for projects (`project-audit-data` opened in `hegs-dialog` from `project-controls`) and daemons (`daemon-audit-view` opened in `paper-dialog` from `page-daemons-list` row buttons). A user wanting a *cross-record* audit feed for either domain has nowhere to go — the nav drawer's promise of a Projects/Daemons audit page is unfulfilled.

The visible symptom: clicking either sub-tab silently lands the user on the listing they came from, so the feature appears broken.

---

## 2. Scope

**In scope:**
- New backend endpoints to fetch paged, filterable, sortable audit feeds across all projects and across all daemons.
- New frontend pages `page-projects-audit` and `page-daemons-audit` modelled on `page-scripts-audit` / `page-variables-audit`.
- New routes `/projects/audit` and `/daemons/audit`.
- Navbar `urlForName(...)` updates so the two sub-tabs target the new routes.

**Out of scope:**
- Existing per-record audit modals (`project-audit-data`, `daemon-audit-view`) — they remain reachable from row buttons unchanged.
- Audit *retention* policy or storage layout — `deploy.RefDataAudit` and `deploy.DaemonAudit` are unchanged.
- Audit *write path* — `InsertDaemonAudit` and the project audit insert path are not touched.
- A unified cross-domain audit page — the four feeds remain distinct (matches existing scripts/variables pattern).
- Authorization changes — the new endpoints inherit `[Authorize]` from their controllers; no role gating beyond what the existing per-record endpoints enforce (see U-3).

---

## 3. Goals and Success Criteria

| ID    | Success Criterion |
|-------|-------------------|
| SC-01 | Clicking "Projects Audit" in the nav opens a dedicated page that lists `deploy.RefDataAudit` rows across all projects, with the originating project name visible in the row. Server-side paginated and sortable, with at least Project / User / Date column filters. |
| SC-02 | Clicking "Daemons Audit" in the nav opens a dedicated page that lists `deploy.DaemonAudit` rows across all daemons, with the originating daemon name visible in the row. Server-side paginated and sortable, with at least Daemon / User / Date column filters. |
| SC-03 | The per-record modal audit views (`project-audit-data` for projects, `daemon-audit-view` for daemons) continue to function exactly as before — no regression in the row-level "view audit history" buttons. |
| SC-04 | Both new pages follow the visual idiom of `page-scripts-audit` (Vaadin grid with `.dataProvider`, header-renderer filters, AND/OR toggle if useful) so the four audit pages feel like one feature. |
| SC-05 | The new endpoints respond in under 1 second for a 50-row page on the production-sized DV3 dataset (≈1k–10k audit rows per domain). Index review is part of the design, not an afterthought. |
| SC-06 | All new code paths are covered by tests at the level the behaviour lives: persistent-source query tests for paging/filtering/sorting; controller route tests for parameter binding; smoke-level frontend test that the route resolves to the new page. |

---

## 4. Constraints

- **C-01** — No schema change to `deploy.RefDataAudit` or `deploy.DaemonAudit` beyond additive (e.g. new index). Backfill is out of scope.
- **C-02** — Existing per-record endpoints (`PUT /DaemonAudit?daemonId=X`, `PUT /RefDataProjectAudit?projectId=X`) must continue to work — they back the row-level modals. Either expand them to make the Id optional, or add new sibling endpoints; do not remove or break them.
- **C-03** — The frontend SDK is regenerated from the OpenAPI surface; backend route shape changes flow through to TypeScript. Any new endpoint must be a clean addition, not a rename of an existing one (avoids a breaking SDK delta inside an unrelated PR window).
- **C-04** — `RefDataAudit.ProjectId` is nullable (FK with `ON DELETE SET NULL`) and `DaemonAudit.DaemonId` is also nullable. Cross-record queries must handle audit rows whose target was deleted (display "(deleted)" or similar rather than erroring).
- **C-05** — Pagination must be server-side via the existing `PagedDataOperators` pattern. No "fetch everything then paginate in the browser" — datasets grow.
- **C-06** — No change to the Vaadin / Lit / @vaadin/router versions; the new pages use the same components already in the project.

---

## 5. Proposed Solution Directions

### SD-1: New persistent-source methods for cross-record audit

Add two methods alongside the existing per-record ones:

- `IDaemonAuditPersistentSource.GetDaemonAudit(int limit, int page, PagedDataOperators operators)` — joins `deploy.DaemonAudit` to `deploy.Daemon` for the daemon name, projects `RefDataAuditAction.Action` as a string column, returns the same `GetDaemonAuditListResponseDto` shape extended with `DaemonName` per item.
- `IManageProjectsPersistentSource.GetRefDataAudit(int limit, int page, PagedDataOperators operators)` — joins `deploy.RefDataAudit` to `deploy.Project` for the project name, returns the same `GetRefDataAuditListResponseDto` shape extended with `ProjectName` per item.

The DTOs stay shape-compatible with the per-record variants so the frontend can share renderers between the modal view and the dedicated page where useful.

### SD-2: New controllers (or expanded existing ones)

Two options, picked in S-001 of the IS:

- **Option A** — make the existing controllers' `daemonId` / `projectId` parameters optional. Pros: one endpoint per domain. Cons: slight API ambiguity (one endpoint with two distinct meanings depending on whether the Id is set), and the existing OpenAPI surface stays stable but the swagger description has to cover both modes.
- **Option B** — add new `GET/PUT` endpoints (e.g. `PUT /DaemonAudit/all`, `PUT /RefDataProjectAudit/all`). Pros: each endpoint has one meaning. Cons: two more routes per controller.

Both options preserve C-02. Option A is preferred because it keeps the SDK delta tighter and matches the way `scripts-audit` / `variables-audit` controllers are shaped (single endpoint per audit feed) — pending U-2 confirmation that no consumer depends on the parameter being mandatory.

### SD-3: Two new frontend pages and routes

- `src/dorc-web/src/pages/page-projects-audit.ts` — Vaadin grid with `.dataProvider`, columns: Project, User, Date, Action, JSON-diff value renderer. Header-renderer filter on Project / User / Date plus the AND/OR toggle from `page-scripts-audit`.
- `src/dorc-web/src/pages/page-daemons-audit.ts` — same shape, columns: Daemon, User, Date, Action, From → To value renderer (re-use the cell-part-name generator from `daemon-audit-view`).
- New routes `/projects/audit` and `/daemons/audit` in `routes.ts`, with the same `RouteMeta` envelope as the scripts/variables audit routes.
- Two `urlForName` swaps in `dorc-navbar.ts` (currently `'projects'` and `'daemons'`, change to `'projects-audit'` and `'daemons-audit'`).

The existing per-record modal components stay in place and untouched.

### SD-4: Performance posture

Audit tables grow over time. The persistent-source SQL must use the same paging-with-COUNT(*) idiom the scripts/variables audit already uses, and the join to `Daemon` / `Project` for the name column must use the existing PK indexes. If the row count exceeds ~50k in any environment, an index on `Date DESC` is the natural next step; this HLPS does not pre-emptively add it but flags the threshold (see U-4).

---

## 6. Unknowns Register

| ID  | Description | Owner | Blocking | Resolution |
|-----|-------------|-------|----------|------------|
| U-1 | Should the existing per-record audit modals (`project-audit-data`, `daemon-audit-view`) remain after the dedicated pages exist? Assumption: yes — the row-level "view audit for this one record" affordance is still useful. | User | Non-blocking | Default: keep. Confirm. |
| U-2 | Does any current consumer of `PUT /DaemonAudit?daemonId=X` or `PUT /RefDataProjectAudit?projectId=X` rely on the Id parameter being **required** at the binding level (e.g. would a 400 on missing Id be a contract any caller depends on)? Affects choice between SD-2 Option A and Option B. | User | **Blocking** for SD-2 choice | **RESOLVED.** No backwards-compat constraint. Going with Option A (single endpoint per domain with optional Id) as the industry-standard collection-with-optional-filter pattern. |
| U-3 | Are audit feeds globally readable to any authenticated user, or do they need the same project/daemon-level RBAC the rest of the listings enforce? Existing per-record endpoints use `[Authorize]` only; the cross-record feed will inherit that unless told otherwise. | User | Non-blocking unless RBAC is required, in which case **blocking** | Default: keep `[Authorize]` only, matching siblings. Confirm. |
| U-4 | What is the current row count in `deploy.RefDataAudit` and `deploy.DaemonAudit` on PR / production? Drives whether SD-4 needs an index now or can defer. | User | Non-blocking | Default: defer index work; revisit when the page lands. |
| U-5 | Should the audit pages link a row's Project / Daemon name back to the relevant listing entry (click the name → navigate to that project/daemon)? Affects scope of S-003 / S-004. | User | Non-blocking | Default: yes, link if cheap; otherwise show plain text. |

---

## 7. Out-of-Scope Risks

- **Audit volume growth**: long-term, the cross-record feed will become slow without an index. SD-4 flags the threshold but does not pre-empt it. If the dataset is already large at U-4 resolution time, index work moves into this HLPS's IS as an additional step.
- **RBAC mismatch**: if U-3 lands "yes, RBAC is required," SD-1 needs to filter by user-visible projects/daemons in SQL, not in the controller, which is a meaningful design shift. Captured as conditional on U-3.
- **Modal vs page duplication**: the two paths show overlapping data with slightly different renderers. A future consolidation (single audit-grid component used by both modal and page) is plausible but not in this HLPS — it's the kind of refactor that wants its own scope.
