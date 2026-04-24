# Daemons modernisation — schema, RBAC, audit, UX (#649)

Modernises the daemons feature end-to-end: DB schema to `deploy.*` with PascalCase, EF aligned, RBAC on all mutations, audit trail with a new `deploy.DaemonAudit` table and post-deploy seed that also retro-fixes the previously latent-broken project-audit path, Edit/Delete surface in the UI, and structured error surfacing for status probes.

One PR, per HLPS C-06. Reviewer guidance below — please divide by SD section for tractable review.

## Planning docs (pre-existing in the branch)

- `docs/daemons-modernisation/HLPS-daemons-modernisation.md` — High-Level Problem Statement (APPROVED)
- `docs/daemons-modernisation/IS-daemons-modernisation.md` — Implementation Sequence (APPROVED)
- `docs/daemons-modernisation/SPEC-S-001..S-009*.md` — per-step JIT specs (all APPROVED)
- `docs/daemons-modernisation/FOLLOW-UPS.md` — 2 deferred items (audit UI consolidation, "last seen" tracking)

## Summary of changes

| Area | What changed |
|------|--------------|
| **DB schema (S-001)** | New `deploy.Daemon`, `deploy.ServerDaemon`, `deploy.DaemonAudit` tables with filtered unique indexes, composite PK, CASCADE/NO ACTION FKs. Legacy `dbo.SERVICE` + `dbo.SERVER_SERVICE_MAP` + `UC_Service_*` SSDT sources removed. |
| **Data migration (S-002)** | Staging-table pattern across pre/post-deploy — pre-deploy stages legacy rows to `dbo.*_MIGRATION_STAGING` + empties the legacy tables (so SSDT can drop them cleanly with `BlockOnPossibleDataLoss=True`), post-deploy copies staging → new tables with `IDENTITY_INSERT`, filters NULL-name and orphan-server rows, drops staging. Idempotent. |
| **EF (S-003)** | `DaemonEntityTypeConfiguration` → `deploy.Daemon`; `ServerEntityTypeConfiguration` skip-navigation → `deploy.ServerDaemon` with explicit `.HasKey("ServerId","DaemonId")`. `Daemon.cs` `StringLength(50)` → `(250)`. **C# property `Daemon.ServiceType` preserved** — only the DB column is renamed to `Type` via `.HasColumnName("Type")`. Wire contract unchanged. |
| **Persistence fixes (S-004)** | `Add` pre-checks for duplicate `Name`/`DisplayName` → throws typed `DaemonDuplicateException`; controller translates to 409 with readable message. `Update` now persists `Name`. `Delete` clears `.Server` nav collection + relies on DB cascade. Also **`Services` → `Daemons` rename** on `IDeploymentContext.Daemons` + `Server.Daemons` (internal only; not on the wire). |
| **ServiceStatus hygiene (S-005)** | `DiscoverAndMapDaemonsForServer` removed entirely (interface + impl + call site + `PersistDiscoveredMappings`). New `ErrorMessage` field on the daemon status model populated in probe catches and `GetDaemonStatus` blind catch. Also **`ApiServices` daemon methods extracted** into a new `DaemonStatusMapping` static helper; `DaemonStatusController` injects `IServiceStatus` directly. File casing fix `APIServices.cs` → `ApiServices.cs`. **`ServicesAndStatus` → `DaemonStatus`** + **`ServiceStatusApiModel` → `DaemonStatusApiModel`** + `.ServiceName` → `.DaemonName`, `.ServiceStatus` → `.Status` (wire-level rename; no external consumers per U-2/U-5 resolution). |
| **RBAC (S-006)** | `RefDataDaemonsController` POST/PUT gated on PowerUser\|Admin, DELETE on Admin only. `ServerDaemonsController` Attach/Detach on PowerUser\|Admin. `DaemonStatusController` unchanged. All 403s carry readable messages. |
| **Audit (S-007)** | New `DaemonAudit` entity + fluent EF config (no `[Table]` attribute anti-pattern). `ActionType` enum extended with `Attach`, `Detach`. New `IDaemonAuditPersistentSource` with Update-when-unchanged skip. Idempotent post-deploy `SeedRefDataAuditActions.sql` seeds all five action rows on every publish — **this seed also fixes DF-12 (project-audit latent-broken state)** without touching `ManageProjectsPersistentSource` source code. New `DaemonAuditController` read endpoint (authenticated, no role gate — U-11 resolution). Audit writes wired into all five mutations with payloads per HLPS SC-05. |
| **UI (S-008)** | `add-daemon.ts` maxlength → 250, readonly removed from AccountName/Type. `daemon-controls.ts` `userEditable` logic inverted. New `edit-daemon.ts` edit dialog. New `daemon-audit-view.ts` audit grid. `page-daemons-list.ts` has Audit/Edit/Delete row actions with role-gated visibility (Edit: PowerUser\|Admin; Delete: Admin). `application-daemons.ts` status cell renders ⚠ + tooltip when `ErrorMessage` is present. |
| **DB integration tests (S-009)** | Three `[TestCategory("Database")]` tests in `Tests.Acceptance/DatabaseTests/` — SC-01 migration completeness, SC-02 populated-DB migration correctness + idempotency, SC-05b seed idempotency (both branches). Uses DacFx to publish the built dacpac against an ephemeral per-test DB. `Assert.Inconclusive` on missing connection string so local dev runs without SQL don't red-fail. |

## Reviewer Checklist (per SD)

### SD-1 — Schema + EF (DBA / backend reviewer)
- [ ] `src/Dorc.Database/deploy/Tables/Daemon.sql` + `ServerDaemon.sql` + `DaemonAudit.sql` created with shapes per HLPS SD-1
- [ ] `Daemon.sql` has filtered unique index on `DisplayName WHERE IS NOT NULL`
- [ ] `ServerDaemon.sql` has composite PK `(ServerId, DaemonId)`, `FK_ServerDaemon_Daemon ON DELETE CASCADE`, `FK_ServerDaemon_Server ON DELETE NO ACTION`
- [ ] `DaemonAudit.sql` has `BIGINT` PK, nullable `DaemonId` with **no FK** to `deploy.Daemon`
- [ ] Legacy `dbo/Tables/SERVICE.sql`, `SERVER_SERVICE_MAP.sql`, `UC_Service_*.sql` deleted; `Dorc.Database.sqlproj` reconciled (net −1 `<Build Include>`)
- [ ] `DaemonEntityTypeConfiguration` points at `deploy.Daemon` with `.HasColumnName("Type")` on `ServiceType`
- [ ] `ServerEntityTypeConfiguration` `UsingEntity` points at `deploy.ServerDaemon` with `.HasKey("ServerId","DaemonId")`
- [ ] `Daemon.cs` `[StringLength(250)]` on all string properties
- [ ] Grep-audit (narrow patterns per HLPS SD-1) returns zero hits in `src/` except in the migration SQL

### SD-2 — Migration scripts (DBA)
- [ ] `Scripts/Pre-Deployment/StageServicesForMigration.sql` — triple-guarded; FK-order DELETE; PRINT row counts
- [ ] `Scripts/Post-Deployment/MigrateStagedServicesToDaemons.sql` — NULL-name filter + orphan-ServerId filter, IDENTITY_INSERT preserves Ids, WHERE NOT EXISTS idempotency, drops staging on success
- [ ] Wired into `Script.PreDeployment.sql` / `Script.PostDeployment.sql` (post-deploy order: migrate → seed → cleanup)
- [ ] `Dorc.Database.sqlproj` has the two `<None Include>` entries

### SD-3 — DaemonsPersistentSource fixes + 409 (backend reviewer)
- [ ] `Update` persists `Name`; `Delete` clears `.Server` nav collection; `Add` throws `DaemonDuplicateException` on `Name`/`DisplayName` collision
- [ ] `DaemonDuplicateException` lives at `src/Dorc.PersistentData/Exceptions/DaemonDuplicateException.cs` (new folder)
- [ ] `RefDataDaemonsController.Post` has per-action try/catch → 409 (not via DefaultExceptionHandler); also catches `DbUpdateException` with SQL 2601/2627 for race fallback

### SD-4 — ServiceStatus hygiene + ApiServices cleanup (backend reviewer)
- [ ] `IDaemonsPersistentSource.DiscoverAndMapDaemonsForServer` removed (interface, impl, `PersistDiscoveredMappings`, call site)
- [ ] `DaemonStatusApiModel.ErrorMessage` added and populated in probe catches; also in `GetDaemonStatus` blind catch
- [ ] `ApiServices` daemon methods extracted; `DaemonStatusMapping.ToApi/ToCore` static helper; `DaemonStatusController` injects `IServiceStatus` directly
- [ ] File-casing fix `APIServices.cs` → `ApiServices.cs` + `IAPIServices.cs` → `IApiServices.cs` (git mv via temp name to survive NTFS case-insensitivity)

### SD-5 — RBAC (backend reviewer)
- [ ] `RefDataDaemonsController` POST/PUT: PowerUser\|Admin; DELETE: Admin only
- [ ] `ServerDaemonsController` Attach/Detach: PowerUser\|Admin
- [ ] `DaemonStatusController` unchanged (keeps `CanModifyEnvironment` gate)
- [ ] 403 messages match existing `RefDataEnvironmentsController` convention

### SD-6 — Audit (backend + DBA reviewer)
- [ ] `deploy.DaemonAudit` entity + `DaemonAuditEntityTypeConfiguration.cs` (fluent `.ToTable`)
- [ ] `ActionType` enum extended with `Attach`, `Detach`
- [ ] `IDaemonAuditPersistentSource` + `DaemonAuditPersistentSource` + `DaemonAuditController`
- [ ] Audit writes wired into `RefDataDaemonsController` (POST/PUT/DELETE) and `ServerDaemonsController` (Attach/Detach) with SC-05 payloads; 403/404 paths write no audit
- [ ] `SeedRefDataAuditActions.sql` post-deploy seeds five rows idempotently (unblocks DF-12 too)

### SD-7 — UI (frontend reviewer)
- [ ] `add-daemon.ts` maxlength=250; AccountName/Type editable
- [ ] `daemon-controls.ts` userEditable logic inverted (four-state matrix works as expected)
- [ ] `edit-daemon.ts` — prefilled dialog, submits PUT, surfaces 403/409 body text
- [ ] `daemon-audit-view.ts` — paged audit grid, Action color-coding, JSON from/to rendering
- [ ] `pages/page-daemons-list.ts` has Audit (all authed) / Edit (PowerUser\|Admin) / Delete (Admin) row actions with role-gated visibility + Delete confirmation dialog
- [ ] `application-daemons.ts` status cell renders ⚠ + tooltip when `ErrorMessage` present
- [ ] TypeScript bindings for `DaemonApiModel`, `DaemonStatusApiModel`, `DaemonAuditApiModel`, `GetDaemonAuditListResponseDto` regenerated; `npx tsc --noEmit` passes
- [ ] `swagger.json` schemas + `/DaemonAudit` endpoint aligned with C# contracts

### SD-8 — Tests (backend + DBA reviewer)
- [ ] `Tests.Acceptance.csproj` has `Microsoft.SqlServer.DacFx` NuGet
- [ ] `DatabaseTests/DatabaseTestBase.cs` — ephemeral DB lifecycle, dacpac locator, DacFx publish wrapper, `Assert.Inconclusive` on missing connection
- [ ] `DatabaseTests/DaemonSchemaMigrationTests.cs` — three `[TestCategory("Database")]` tests (SC-01, SC-02, SC-05b)
- [ ] `DatabaseTests/Fixtures/LegacyDaemonSeed.sql` — legacy-schema fixture including NULL-name + orphan-server rows
- [ ] Existing Reqnroll feature tests unaffected (CI run green)

## Manual QA

Per HLPS SC-07, the submitter should complete the end-to-end flows and note them in the PR comments:
- Create a daemon as PowerUser → verify audit row with `Action=Create`
- Rename the daemon via Edit → verify audit row with `Action=Update` (payloads show before/after)
- Edit without changing values → verify **no** audit row (Update-when-unchanged skip)
- Change AccountName and Type → verify audit row
- Attach daemon to a server → verify audit row with `Action=Attach`
- Detach → verify audit row with `Action=Detach`
- Attempt mutations as non-PowerUser → verify 403 with readable message in UI banner
- Create two daemons with the same Name → verify second POST returns 409 with the conflicting field in the body
- Delete as Admin → confirmation dialog, then daemon + mappings gone (cascade), audit row with `Action=Delete`
- View a daemon's audit history → verify the view shows ordered rows with color-coded action

## Deploy notes

- The migration is data-loss-safe under the default SSDT publish profile (`BlockOnPossibleDataLoss=True`). No manual DBA steps required.
- The publish runs two ordered phases sandwiched around the schema deploy — see HLPS §7 Deployment Phasing.
- Recovery path: restore DB from pre-deploy backup if migration fails mid-publish. Staging tables survive between phases on a partial failure so re-publish can resume.

## Related

- Closes sefe/dorc#649
- Follow-ups (separate tickets): audit UI consolidation, daemon "last seen" tracking — see `docs/daemons-modernisation/FOLLOW-UPS.md`
