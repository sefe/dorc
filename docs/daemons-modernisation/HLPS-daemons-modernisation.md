# HLPS: Daemons Modernisation — Schema, RBAC, Audit, UX

| Field       | Value                                            |
|-------------|--------------------------------------------------|
| **Status**  | APPROVED                                         |
| **Author**  | Agent                                            |
| **Date**    | 2026-04-24                                       |
| **Issue**   | sefe/dorc#649                                    |
| **Folder**  | docs/daemons-modernisation/                      |

---

## 1. Problem Statement

The daemons feature is the last DORC area still on the legacy `dbo` schema with snake-case-uppercase naming (`dbo.SERVICE`, `dbo.SERVER_SERVICE_MAP`). Beyond the naming/schema inconsistency, the feature has accumulated four categories of defect that the rest of the modernised DORC codebase has already addressed for other reference-data areas (Projects, Scripts, Environments, Permissions, SqlPorts):

1. **Schema drift** — legacy tables in `dbo` with snake-case-uppercase columns, no alignment with the `deploy.*` / PascalCase convention established elsewhere.
2. **Correctness bugs** in the daemons CRUD path that surface as opaque `500`s or silent no-ops users cannot diagnose or recover from.
3. **No RBAC** on mutations — any authenticated user can create, edit, delete, attach, or detach daemons. Every other RefData area gates these actions on Admin/PowerUser.
4. **No audit trail** — daemon mutations leave no record of who did what, whereas Projects/Scripts/Environments all write to `deploy.RefDataAudit`.

The user-visible symptom is that daemons are effectively second-class data: harder to debug when things fail, harder to govern for compliance, and harder to evolve because the schema doesn't play nicely with the patterns every other table uses.

---

## 2. Observed Defects

Each defect below is user-visible and either silently succeeds without doing what the user asked, or produces an unactionable `500`.

### DF-1 — Update silently drops `Name`
`DaemonsPersistentSource.Update` (lines 107–132) updates `ServiceType`, `AccountName`, and `DisplayName` but not `Name`. A PUT that renames a daemon returns `200` with the renamed payload, but the DB value is unchanged.

### DF-2 — Delete collides with `SERVER_SERVICE_MAP` FK
`DaemonsPersistentSource.Delete` (lines 91–105) removes the daemon without first detaching `SERVER_SERVICE_MAP` rows. The FK constraint throws, which bubbles out as an opaque `500` with no clear message.

### DF-3 — Add with duplicate Name/DisplayName returns 500
`DaemonsPersistentSource.Add` (lines 73–89) does no pre-check against the `UC_Service_*` unique constraints. A duplicate Name or DisplayName yields a `500` from the DB exception instead of a `409 Conflict` with a readable message.

### DF-4 — Column widths are inconsistent across DB / model / UI
`Model/Daemon.cs` declares `[StringLength(50)]`; DB columns are `NVARCHAR(250)`; UI `add-daemon.ts` caps input at `50`. The three must pick a single width.

### DF-5 — `userEditable` logic is inverted on the daemon control buttons
`daemon-controls.ts` lines 76–86 disable start/stop/restart buttons **when** the user has edit rights. Users who can act cannot; users who cannot, can (until the API rejects them with the RBAC gate added in this work).

### DF-6 — Add dialog locks `AccountName` and `Type`
`add-daemon.ts` hard-codes `.readonly="${true}"` on AccountName and Type with defaults "Local System Account" / "Windows Service". Users cannot create daemons with other values.

### DF-7 — Status probe writes to the DB as a side effect
`ServiceStatus` (lines 232–255) writes rows to `SERVER_SERVICE_MAP` whenever a status GET discovers a service on a server. The read-shaped endpoint mutates persistent state, bypassing any mapping permission check and producing data changes that are hard to attribute.

### DF-8 — Probe errors are logged but not surfaced
`ServiceStatus` (lines 154, 163, 170, 193, 209, 217, 224) logs ping / service-controller errors at `LogInformation` or `LogDebug` and never surfaces them in `ServiceStatusApiModel`. The UI shows "no status" with no diagnostic.

### DF-9 — No RBAC on daemon mutations
`RefDataDaemonsController` (POST/PUT/DELETE) and `ServerDaemonsController` (attach/detach POST/DELETE) have only class-level `[Authorize]`. Every other RefData controller (Projects, Scripts, Environments, Permissions, SqlPorts) gates mutations on Admin/PowerUser via `IRolePrivilegesChecker`.

### DF-10 — No audit trail on daemon mutations
No daemon mutation writes to `deploy.RefDataAudit` or any equivalent. Projects/Scripts/Environments all audit their CRUD.

### DF-11 — UI has no Edit or Delete surface
`src/dorc-web/src/pages/page-daemons-list.ts` has an Add button but no Edit or Delete row actions, even though the API exposes `PUT` and `DELETE`. End users cannot exercise the CRUD surface from the UI.

### DF-12 — Existing `RefDataAudit` path is latent-broken
`ManageProjectsPersistentSource.InsertRefDataAudit` (used by `RefDataController` PUT/POST for project ref-data) calls `context.RefDataAuditActions.First(x => x.Action == ActionType.Create)` against a table that has **no seed mechanism** in the repo — no `HasData`, no post-deploy INSERT, no bootstrap code. If the `deploy.RefDataAuditAction` table is in fact empty (as the absence of seed suggests), every project PUT and POST throws `InvalidOperationException: Sequence contains no elements`. Confirmed by U-4 resolution: the only auto-seed code ever written lives in two dangling never-merged commits. Included in this PR per user direction so the whole audit infrastructure is load-bearing and consistent, not just the daemon subset.

---

## 3. Scope

**In scope:**
- `src/Dorc.Database/` — legacy `dbo.SERVICE` / `dbo.SERVER_SERVICE_MAP` schema, `UC_Service_*` constraints, new `deploy.Daemon` / `deploy.ServerDaemon` / `deploy.DaemonAudit` tables, pre-deploy data migration, post-deploy seed for `deploy.RefDataAuditAction`, `Dorc.Database.sqlproj` wiring.
- `src/Dorc.PersistentData/` — `Daemon`/`Server` EF configurations, `Daemon.cs` model (column mapping only — see SD-1), new `DaemonAudit` entity + configuration, `DaemonsPersistentSource`, new `DaemonAuditPersistentSource`, `RefDataAuditAction` enum extension with `Attach`/`Detach`. `ManageProjectsPersistentSource.InsertRefDataAudit` is **not** modified; the post-deploy seed makes its existing `.First(…)` path work. (See SD-5 for the rationale; this is a simplification from the Round 1 draft.)
- `src/Dorc.ApiModel/` — additive `ErrorMessage` field on `ServiceStatusApiModel`. `DaemonApiModel.ServiceType` wire name is **unchanged**. `VariableValueDaemons.ServiceType` unchanged.
- `src/Dorc.Api/Controllers/` — `RefDataDaemonsController`, `ServerDaemonsController`, `DaemonStatusController` (RBAC additions, audit hooks, 409 translation). New `DaemonAuditController` (read).
- `src/Dorc.Core/ServiceStatus.cs` — remove `PersistDiscoveredMappings` side effect (and its caller site in `GetServicesAndStatusForEnvironment`) and remove `IDaemonsPersistentSource.DiscoverAndMapDaemonsForServer` + its implementation; surface probe errors on `ServiceStatusApiModel`.
- `src/Dorc.Core/VariableScopeOptionsResolver.cs` — no logic change; relies on `DaemonApiModel.ServiceType` remaining unchanged. Included for grep-audit completeness only.
- `src/Tools.PostRestoreEndurCLI/RefreshEndur.cs` — references `ServiceType`; no logic change (wire name is preserved). Listed so a grep-audit pass can confirm.
- `src/dorc-web/src/pages/page-daemons-list.ts` — Edit and Delete row actions.
- `src/dorc-web/src/components/` — `add-daemon.ts`, `daemon-controls.ts`, `map-daemons.ts` (references `ServiceType`; confirm no change needed), plus a new edit-daemon dialog and daemon-audit view component.
- `src/dorc-web/src/apis/dorc-api/` — auto-regenerated TypeScript client for `DaemonApiModel` (no shape change expected) and `ServiceStatusApiModel` (new `ErrorMessage` field).

**Out of scope:**
- Runner-side Windows service start/stop/restart implementation itself (`ServiceController` calls, Ping behaviour) — only error-surfacing is in scope, not the probe mechanism.
- Renaming `dbo.SERVER` or any other legacy table not directly implicated by daemons. `deploy.ServerDaemon` references `dbo.SERVER` via `ServerId`.
- General RBAC refactor across other controllers.
- Renaming existing classes that violate the CLAUDE.md naming rule (grandfathered). Any **new** classes introduced by this work must comply.
- Changes to the `DaemonStatusController` PUT contract beyond additive error-surfacing on the response model.
- Renaming the C# property `Daemon.ServiceType` to `Daemon.Type` (Round 1 draft proposed this). Keeping the property name unchanged preserves the wire contract on `DaemonApiModel.ServiceType` and avoids touching 13 downstream references. Only the DB column is renamed, via `HasColumnName("Type")` in `DaemonEntityTypeConfiguration`.

---

## 4. Goals and Success Criteria

| ID    | Success Criterion |
|-------|------------------|
| SC-01 | `dbo.SERVICE` and `dbo.SERVER_SERVICE_MAP` no longer exist. Daemons and their server mappings live in `deploy.Daemon` / `deploy.ServerDaemon` with PascalCase columns (`Id`, `Name`, `DisplayName`, `AccountName`, `Type` on Daemon; `ServerId`, `DaemonId` on ServerDaemon with composite PK), matching the patterns used by `deploy.RefDataAudit` / `deploy.EnvironmentServer`. |
| SC-02 | Pre-deploy migration runs on a populated DB without data loss. Existing daemon `Id` values are preserved. Verification procedure: **(a)** snapshot `SELECT COUNT(*), MAX(Service_ID)` from `dbo.SERVICE` and `dbo.SERVER_SERVICE_MAP` on a preprod copy; **(b)** run SSDT publish; **(c)** confirm matching `COUNT(*)` and `MAX(Id)` on `deploy.Daemon` and `deploy.ServerDaemon` and a row-by-row checksum using `HASHBYTES` or an EXCEPT query on the preserved columns; **(d)** repeat publish and confirm no row-count change (idempotency proof). |
| SC-03 | Every defect in §2 has a regression test (unit or integration) at the layer the defect lives in. |
| SC-04 | `RefDataDaemonsController` POST/PUT and `ServerDaemonsController` POST/DELETE return `403` for callers without the PowerUser **or** Admin role. `RefDataDaemonsController` DELETE returns `403` for non-Admins. `DaemonStatusController` retains its existing `CanModifyEnvironment` gate unchanged (explicitly out of scope — see §3). |
| SC-05 | Every daemon create, update, delete, attach, and detach writes a row to `deploy.DaemonAudit` capturing username, action, and action-specific payload. Payload conventions: **Create** — `FromValue = null`, `ToValue = JSON(DaemonApiModel)`. **Update** — `FromValue = JSON(before)`, `ToValue = JSON(after)`, skipped when `FromValue == ToValue` (per `ScriptsAuditPersistentSource.AddRecord`). **Delete** — `FromValue = JSON(DaemonApiModel)`, `ToValue = null`. **Attach** — `FromValue = null`, `ToValue = JSON({ ServerId, DaemonId })`. **Detach** — `FromValue = JSON({ ServerId, DaemonId })`, `ToValue = null`. A read endpoint returns per-daemon history, and the details page exposes it. |
| SC-05b | Project PUT/POST against `deploy.RefDataAuditAction` succeeds because the post-deploy seed guarantees Create/Update/Delete/Attach/Detach rows exist. Two integration tests prove this: **(i)** on an empty table, the post-deploy seed populates five rows and a project PUT succeeds; **(ii)** on a pre-seeded table (rows already exist), the seed is a no-op and the PUT still succeeds with no duplicate-key error. No change to `ManageProjectsPersistentSource.InsertRefDataAudit` source code. |
| SC-06 | The daemons list page exposes an Edit action (visible to PowerUser/Admin) and a Delete action (visible to Admin). The Edit dialog allows editing `AccountName` and `Type` — not just `Name`/`DisplayName`. Hard-coded `.readonly="${true}"` on those fields is removed. Column-width caps align on 250: DB `NVARCHAR(250)`, model `[StringLength(250)]`, UI `maxlength="250"`. |
| SC-07 | Manual QA coverage: create, rename, change account/type, attach-to-server, detach, delete. Each flow succeeds, produces an audit row, and on failure returns `403`/`409` with a message the UI displays rather than a generic "Error creating daemon!". |
| SC-08 | All changes compile and the existing test suite passes on a clean build of `Dorc.sln` + `dorc-web`. |

---

## 5. Constraints

- **C-01** No data loss. All existing `SERVICE` and `SERVER_SERVICE_MAP` rows must survive the migration with `Id` values preserved.
- **C-02** No breaking change to consumers of daemon data. `DaemonApiModel.ServiceType`, `VariableValueDaemons.ServiceType`, and the TypeScript-generated bindings for these models keep their current field names and types. `ServiceStatusApiModel.ErrorMessage` is additive (new nullable field, no removals or renames). The C# property `Daemon.ServiceType` is also preserved — only the underlying column is renamed. `[StringLength(50)]` → `[StringLength(250)]` is a model-validation loosening (previously-rejected payloads are now accepted); this is additive / back-compatible per C-02's intent.
- **C-03** `dbo.SERVER` is not renamed or modified; `deploy.ServerDaemon` references it by `ServerId` with `ON DELETE NO ACTION` (explicit — see SD-1) to avoid accidental cascade paths and SQL Server error 1785.
- **C-04** The migration must be publishable by the existing SSDT pipeline with no manual DBA steps. The pre-deploy script must be idempotent: **(a)** guarded by `IF OBJECT_ID('dbo.SERVICE') IS NOT NULL`; **(b)** the row-copy uses `INSERT … WHERE NOT EXISTS` so a partial previous run that left `deploy.Daemon` populated does not fail on repeat with `IDENTITY_INSERT` PK collisions.
- **C-05** Classes introduced by this work must comply with the CLAUDE.md Naming rule (see `C:\src\dorc2\CLAUDE.md`, `.NET Coding Standard → Naming`). Existing classes are grandfathered.
- **C-06** The work ships as a single PR per user direction (U-8). The PR description must include the per-SD review checklist skeleton below, so reviewers can divide the diff cleanly. Each SD's diff must be coherent when read in isolation.

  ```markdown
  ## Reviewer Checklist (per SD)

  ### SD-1 — Schema + EF (DBA / backend reviewer)
  - [ ] `src/Dorc.Database/deploy/Tables/Daemon.sql` + `ServerDaemon.sql` + `DaemonAudit.sql` created
  - [ ] `src/Dorc.Database/Scripts/Pre-Deployment/StageServicesForMigration.sql` (+ wired into `Script.PreDeployment.sql`)
  - [ ] `src/Dorc.Database/Scripts/Post-Deployment/MigrateStagedServicesToDaemons.sql` + `SeedRefDataAuditActions.sql`
  - [ ] Legacy `dbo/Tables/SERVICE.sql`, `SERVER_SERVICE_MAP.sql`, `UC_Service_*.sql` deleted; `Dorc.Database.sqlproj` reconciled
  - [ ] `DaemonEntityTypeConfiguration` points at `deploy.Daemon` with `.HasColumnName("Type")` on `ServiceType`
  - [ ] `ServerEntityTypeConfiguration` `UsingEntity` points at `deploy.ServerDaemon` with `.HasKey("ServerId","DaemonId")`
  - [ ] `Daemon.cs` `[StringLength(250)]` on all string properties
  - [ ] Grep-audit (narrow patterns) returns zero hits in `src/`

  ### SD-2 — DaemonsPersistentSource bugs (backend reviewer)
  - [ ] `Update` persists `Name`; `Delete` clears nav collection; `Add` throws `DaemonDuplicateException` on conflict
  - [ ] `DaemonDuplicateException` lives at `src/Dorc.PersistentData/Exceptions/DaemonDuplicateException.cs`
  - [ ] `RefDataDaemonsController.Post` has per-action try/catch → 409
  - [ ] Unit/integration tests cover rename, cascade delete, duplicate 409

  ### SD-3 — ServiceStatus (backend reviewer)
  - [ ] `IDaemonsPersistentSource.DiscoverAndMapDaemonsForServer` removed (interface, impl, call site, `PersistDiscoveredMappings`)
  - [ ] `ServiceStatusApiModel.ErrorMessage` added (nullable)
  - [ ] Error catches populate `ErrorMessage`; log levels unchanged
  - [ ] TypeScript `ServiceStatusApiModel.ts` regenerated; `ErrorMessage?: string | null` present

  ### SD-4 — RBAC (backend reviewer)
  - [ ] PowerUser/Admin on `RefDataDaemons` POST/PUT; Admin-only on DELETE
  - [ ] PowerUser/Admin on `ServerDaemons` POST/DELETE
  - [ ] `DaemonStatusController` unchanged; `DaemonAuditController` read gated to authenticated
  - [ ] 403 messages match existing convention

  ### SD-5 — Audit (backend + DBA reviewer)
  - [ ] `deploy.DaemonAudit` + `DaemonAudit.cs` + `DaemonAuditEntityTypeConfiguration.cs` (fluent `.ToTable`)
  - [ ] `ActionType` extended with `Attach`, `Detach`
  - [ ] `IDaemonAuditPersistentSource` + `DaemonAuditPersistentSource` + `DaemonAuditController`
  - [ ] Audit writes wired into `RefDataDaemonsController` + `ServerDaemonsController` with SC-05 payloads
  - [ ] `Dorc.Database.IntegrationTests` project created; SC-02 and SC-05b tests pass

  ### SD-6 — UI (frontend reviewer)
  - [ ] `add-daemon.ts` maxlength 250; AccountName/Type editable (no hardcoded readonly)
  - [ ] `daemon-controls.ts` userEditable logic inverted
  - [ ] `pages/page-daemons-list.ts` Edit + Delete row actions; role-gated visibility
  - [ ] Edit dialog + daemon-audit view component
  - [ ] TypeScript bindings for `DaemonApiModel` / `ServiceStatusApiModel` up to date
  ```

- **C-07** `deploy.RefDataAuditAction` must be treated as a stable seed table. SSDT's default drop-and-recreate behaviour on a schema change to that table would reset its IDENTITY counter, breaking FK references from existing `deploy.RefDataAudit` and (after this work) `deploy.DaemonAudit` rows. Any future schema change to `RefDataAuditAction` (adding a column, changing nullability, etc.) must go through a manual migration that preserves `RefDataAuditActionId` values, not via an SSDT in-place edit. Documented here so a future implementer does not casually touch that file.

---

## 6. Proposed Solution Directions

### SD-1 — Schema modernisation (addresses SC-01, SC-02, DF-4 at DB level)
- **New tables** under `src/Dorc.Database/deploy/Tables/` (flat layout; matches `RefDataAudit.sql` and `EnvironmentServer.sql` — the same post-`Schema Objects` convention). Rationale for choosing the flat layout: the audit-related templates `RefDataAudit` and the pattern referenced by the issue live there, and `EnvironmentServer` is the closest structural parallel to `ServerDaemon`.

  - **`deploy.Daemon`**: `[Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY`, `[Name] NVARCHAR(250) NOT NULL`, `[DisplayName] NVARCHAR(250) NULL`, `[AccountName] NVARCHAR(250) NULL`, `[Type] NVARCHAR(250) NULL`. Uniqueness via **filtered unique indexes** (not `UNIQUE` constraints, since DisplayName is nullable and SQL Server treats multiple NULLs as duplicates under a plain UNIQUE — R2-M1):
    ```sql
    CREATE UNIQUE INDEX [UQ_Daemon_Name] ON [deploy].[Daemon]([Name]);
    CREATE UNIQUE INDEX [UQ_Daemon_DisplayName] ON [deploy].[Daemon]([DisplayName]) WHERE [DisplayName] IS NOT NULL;
    ```
    `Name` is NOT NULL so its index can be a standard unique. `DisplayName` keeps existing nullability behaviour: the filtered index allows multiple NULL rows to coexist while enforcing uniqueness on non-NULL values.

  - **`deploy.ServerDaemon`**: `[ServerId] INT NOT NULL`, `[DaemonId] INT NOT NULL`, `CONSTRAINT PK_ServerDaemon PRIMARY KEY CLUSTERED (ServerId, DaemonId)`. FK to `deploy.Daemon(Id)` with `ON DELETE CASCADE`; FK to `dbo.SERVER(Server_ID)` with `ON DELETE NO ACTION`. **Composite PK — no separate `Id` column**. The issue text suggested an explicit `Id` PK, but R2-H2 flagged that EF Core's existing skip-navigation pattern (`HasMany().WithMany().UsingEntity("SERVER_SERVICE_MAP", …)` in `ServerEntityTypeConfiguration.cs:35-43`) does not support a separate `Id` on the join row without introducing a full join-entity class and restructuring `Server.Services` / `Daemon.Server` nav collections — a significantly larger change. The composite PK preserves the skip-navigation shape, and no other table or audit row references individual mapping rows (audit rows reference daemons by `DaemonId`, and attach/detach payloads carry the `(ServerId, DaemonId)` tuple directly). **This is a deviation from the issue text; flagged for user confirmation in the Round 2 review.**

  - **`deploy.DaemonAudit`**: `[Id] BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY`, `[DaemonId] INT NULL` (nullable — Delete and Detach rows may outlive the daemon), `[RefDataAuditActionId] INT NOT NULL` FK to `deploy.RefDataAuditAction(RefDataAuditActionId)`, `[Username] NVARCHAR(MAX) NOT NULL`, `[Date] DATETIME NOT NULL`, `[FromValue] NVARCHAR(MAX) NULL`, `[ToValue] NVARCHAR(MAX) NULL`. No FK from `DaemonId` to `deploy.Daemon(Id)` — deletes must keep the audit history intact.

- **Data migration — staging-table pattern** (Round 2 revision). The Round 1 approach of copying `dbo.SERVICE` directly into `deploy.Daemon` in pre-deploy is broken on the first publish: pre-deploy runs **before** SSDT's schema phase, so `deploy.Daemon` does not yet exist. A guard that requires both tables to exist silently skips the migration; the subsequent schema phase then drops `dbo.SERVICE`, losing data. The fix decouples the data rescue from the table creation using a permanent staging table in `dbo`:

  **Pre-deploy** `src/Dorc.Database/Scripts/Pre-Deployment/StageServicesForMigration.sql` (wired into `Script.PreDeployment.sql` via `:r`):
  ```sql
  -- Stage dbo.SERVICE rows into a dbo staging table, then empty dbo.SERVICE and dbo.SERVER_SERVICE_MAP
  -- so SSDT's schema phase can drop them without BlockOnPossibleDataLoss fire-alarms.
  IF OBJECT_ID('dbo.SERVICE') IS NOT NULL
  BEGIN
      IF OBJECT_ID('dbo.SERVICE_MIGRATION_STAGING') IS NULL
      BEGIN
          SELECT Service_ID, Service_Name, Display_Name, Account_Name, Service_Type
          INTO [dbo].[SERVICE_MIGRATION_STAGING]
          FROM [dbo].[SERVICE];
      END

      IF OBJECT_ID('dbo.SERVER_SERVICE_MAP_MIGRATION_STAGING') IS NULL
      BEGIN
          SELECT Server_ID, Service_ID
          INTO [dbo].[SERVER_SERVICE_MAP_MIGRATION_STAGING]
          FROM [dbo].[SERVER_SERVICE_MAP];
      END

      -- Empty the legacy tables so SSDT can drop them cleanly. Map rows first (FK order).
      DELETE FROM [dbo].[SERVER_SERVICE_MAP];
      DELETE FROM [dbo].[SERVICE];
  END
  ```
  The outer `IF OBJECT_ID('dbo.SERVICE')` guard makes this a no-op on second and subsequent publishes (by then the legacy tables are gone). The inner `IF OBJECT_ID('*_STAGING') IS NULL` guards prevent re-staging if a previous pre-deploy completed but the main publish failed mid-way. Emptying the legacy tables after staging means SSDT drops them without needing `BlockOnPossibleDataLoss=False` — the default safe publish profile works.

  **Post-deploy** `src/Dorc.Database/Scripts/Post-Deployment/MigrateStagedServicesToDaemons.sql` (referenced from `Script.PostDeployment.sql`):
  ```sql
  -- Copy staged data into the new tables. Runs after deploy.Daemon / deploy.ServerDaemon have been created
  -- and after dbo.SERVICE / dbo.SERVER_SERVICE_MAP have been dropped.
  IF OBJECT_ID('dbo.SERVICE_MIGRATION_STAGING') IS NOT NULL AND OBJECT_ID('deploy.Daemon') IS NOT NULL
  BEGIN
      SET IDENTITY_INSERT [deploy].[Daemon] ON;
      INSERT INTO [deploy].[Daemon] (Id, Name, DisplayName, AccountName, Type)
      SELECT s.Service_ID, s.Service_Name, s.Display_Name, s.Account_Name, s.Service_Type
      FROM [dbo].[SERVICE_MIGRATION_STAGING] s
      WHERE NOT EXISTS (SELECT 1 FROM [deploy].[Daemon] d WHERE d.Id = s.Service_ID);
      SET IDENTITY_INSERT [deploy].[Daemon] OFF;

      INSERT INTO [deploy].[ServerDaemon] (ServerId, DaemonId)
      SELECT m.Server_ID, m.Service_ID
      FROM [dbo].[SERVER_SERVICE_MAP_MIGRATION_STAGING] m
      WHERE NOT EXISTS (SELECT 1 FROM [deploy].[ServerDaemon] sd
                        WHERE sd.ServerId = m.Server_ID AND sd.DaemonId = m.Service_ID);

      DROP TABLE [dbo].[SERVICE_MIGRATION_STAGING];
      DROP TABLE [dbo].[SERVER_SERVICE_MAP_MIGRATION_STAGING];
  END
  ```
  On a clean DB (or a DB where migration already completed), the staging tables do not exist and this is a no-op. On a successful first publish, the staging tables are cleaned up as the final step of migration. If the migration fails partway, staging tables remain so a re-publish can resume.

- **Delete** SSDT sources for `dbo.SERVICE.sql`, `dbo.SERVER_SERVICE_MAP.sql`, `UC_Service_Service_Name.sql`, `UC_Service_Display_Name.sql`, plus their `<Build Include>` entries in `Dorc.Database.sqlproj`. SSDT drops the objects on publish. Because pre-deploy emptied them, the drop is data-loss-safe under the default publish profile.

- **EF changes**:
  - `DaemonEntityTypeConfiguration`: `.ToTable("Daemon", "deploy")`. Column mappings `Id`, `Name`, `DisplayName`, `AccountName`. **`ServiceType` property maps to new column `Type` via `.HasColumnName("Type")`** — the property name does not change (preserves C-02).
  - `ServerEntityTypeConfiguration` lines 35–43: update the `UsingEntity` call from `"SERVER_SERVICE_MAP"` with `"Server_ID"` / `"Service_ID"` to `"ServerDaemon"` in schema `"deploy"` with `"ServerId"` / `"DaemonId"`. **Inside the `UsingEntity` builder lambda, call `.HasKey("ServerId", "DaemonId")` explicitly** so the shadow join entity's composite PK matches the DB-defined `PK_ServerDaemon` (otherwise EF infers a shadow PK using its own naming convention and may emit a conflicting migration). Skip-navigation pattern is otherwise preserved.
  - `Daemon.cs`: `[StringLength(50)]` → `[StringLength(250)]` on all four string properties.

- **Grep-audit** for remaining legacy references after the rename lands. Use **narrow, word-bounded regex patterns** to avoid false positives (the broad `SERVICE` pattern would match `MonitorService`, `USER_SERVICEMONITOR`, `IServiceControl`, `<Service>` MSBuild elements, etc.):
  - `\bdbo\.SERVICE\b`
  - `\bdbo\.SERVER_SERVICE_MAP\b`
  - `\bSERVER_SERVICE_MAP\b`
  - `\bService_ID\b`
  - `\bService_Name\b`
  - `\bService_Type\b` (note: this is the legacy **DB column** only — differs from the C# property `ServiceType`, which is intentionally preserved).

  Exclude paths: `**/obj/**`, `**/bin/**`, `*.csproj`, `*.sln`, `*swagger.json`. Target: zero hits in `src/` after the rename. Any hits are either false positives (confirm and exclude) or missed renames (fix). `ServiceType` is **not** in the grep list because the C# property name is preserved.

### SD-2 — DaemonsPersistentSource correctness (addresses DF-1, DF-2, DF-3)

#### What changes
- **DF-1** (persistence layer): `DaemonsPersistentSource.Update` includes `Name` in the set of copied fields.
- **DF-2** (both layers): **Persistence layer** — `DaemonsPersistentSource.Delete` calls `.Clear()` on `daemon.Server` and `SaveChanges` before deleting the daemon (EF-level defense-in-depth). **DB layer** — `ON DELETE CASCADE` on `deploy.ServerDaemon` (SD-1) is the authoritative guarantee.
- **DF-3** (both layers): **Persistence layer** — `DaemonsPersistentSource.Add` pre-checks existing `Name` and `DisplayName` against `deploy.Daemon`; on collision throws `DaemonDuplicateException` (new type at `src/Dorc.PersistentData/Exceptions/DaemonDuplicateException.cs`, consistent with the `src/Dorc.Core/Exceptions/WrongComponentsException.cs` pattern). **Controller layer** — `RefDataDaemonsController.Post` uses a per-action `try/catch (DaemonDuplicateException ex)` that returns `409 Conflict` with `ex.Message`. **Not** via `DefaultExceptionHandler` (which does not distinguish 409 semantics); per-action catch matches `RefDataEnvironmentsController`'s existing convention.
- **Minor cleanup**: `Add`'s post-save re-query (`Where(d => d.Name == …).First()`) is redundant — `SaveChanges` populates `mapToDatabase.Id` from the IDENTITY column. Simplify to `return Map(mapToDatabase)`.

#### Why it changes
Current state: rename silently drops `Name`; delete produces an opaque 500 on the FK violation; duplicate insert produces an opaque 500. Fixes restore intended behaviour and translate domain errors into HTTP semantics the UI can render (aligns with SC-07).

#### Verification intent
- DF-1: unit test on `DaemonsPersistentSource.Update` — rename a daemon, re-read by `Id`, assert new `Name`.
- DF-2: integration test — create daemon, attach to server, delete, assert (a) daemon gone, (b) mapping gone, (c) server intact.
- DF-3: controller test — two POSTs with the same `Name` → second returns 409 with a message containing the conflicting value; same for `DisplayName`.

### SD-3 — ServiceStatus hygiene (addresses DF-7, DF-8)
- **Remove the discovery side effect entirely.** Concrete changes:
  - Delete `IDaemonsPersistentSource.DiscoverAndMapDaemonsForServer` (interface method).
  - Delete `DaemonsPersistentSource.DiscoverAndMapDaemonsForServer` (implementation, lines 168–187).
  - Delete `ServiceStatus.PersistDiscoveredMappings` (lines 232–255).
  - Delete the call site in `ServiceStatus.GetServicesAndStatusForEnvironment` (the line that invokes `PersistDiscoveredMappings`).
  - Keep the `_daemonsPersistentSource` field on `ServiceStatus` (still used by `BuildServicesEnvironment` to read daemons — not a discovery/write call).

  If server-side discovery is wanted in the future, it is a separate endpoint designed under SD-4's RBAC. Not in this PR.

- **Add structured error field** to `ServiceStatusApiModel`:
  ```csharp
  public string? ErrorMessage { get; set; }
  ```
  Populated from the catch blocks that currently log at `LogInformation`/`LogDebug` in `ServiceStatus.cs` (lines 154, 163, 170, 193, 209, 217, 224). Log levels remain as they are for ops; the structured surfacing is the user-visible improvement. Test: simulate a `Ping` timeout and verify the UI receives a non-null `ErrorMessage`.

- **TypeScript regeneration**: after the C# model change, regenerate `src/dorc-web/src/apis/dorc-api/models/ServiceStatusApiModel.ts` via the repo's standard swagger-derived codegen pipeline. Confirm `ErrorMessage?: string | null` appears in the generated TS.

### SD-4 — RBAC on daemon controllers (addresses DF-9)
- Inject `IRolePrivilegesChecker` into `RefDataDaemonsController` and `ServerDaemonsController`, mirroring the pattern in `RefDataEnvironmentsController` (see `src/Dorc.Api/Controllers/RefDataEnvironmentsController.cs:86-101`).
- Gate `POST /RefDataDaemons`, `PUT /RefDataDaemons`, `POST /ServerDaemons`, `DELETE /ServerDaemons` on PowerUser **or** Admin. Gate `DELETE /RefDataDaemons` on Admin only. Return `403` with messages matching the existing convention (`"Daemons can only be deleted by Admins!"`, etc).
- `DaemonStatusController` PUT retains its existing `CanModifyEnvironment` gate unchanged. The existing gate is orthogonal to RefData RBAC (it protects environment-level writes, not daemon-level ones) and aligning the two permission models is deferred to future work — explicitly noted to pre-empt a review comment.
- `DaemonAuditController` (new — see SD-5) read endpoint is gated to any authenticated user, mirroring `RefDataProjectAuditController` (`src/Dorc.Api/Controllers/RefDataProjectAuditController.cs`). No PowerUser/Admin gate on read.

### SD-5 — Daemon audit (addresses DF-10, DF-12)

#### What changes
- **New table** `deploy.DaemonAudit` per SD-1.
- **Enum extension**: add `Attach` and `Detach` to `ActionType` in `src/Dorc.PersistentData/Model/RefDataAuditAction.cs`.
- **New models**: `src/Dorc.PersistentData/Model/DaemonAudit.cs` (no `[Table(...)]` attribute) and `src/Dorc.PersistentData/EntityTypeConfigurations/DaemonAuditEntityTypeConfiguration.cs` using fluent `.ToTable("DaemonAudit", "deploy")` (not the `[Table("schema.name")]` anti-pattern visible on `RefDataAudit`/`RefDataAuditAction`).
- **New source**: `IDaemonAuditPersistentSource` + `DaemonAuditPersistentSource` with `InsertDaemonAudit(username, action, daemonId, fromValue, toValue)` and `GetDaemonAudit(daemonId, limit, page, operators)`. The insert path uses `.First(x => x.Action == action)` per the existing convention in `ManageProjectsPersistentSource`; with the post-deploy seed (below) this is guaranteed to succeed.
- **Post-deploy seed** replacing Round 1's application-level check-then-insert (R2-M2: the check-then-insert approach is not concurrency-safe — two simultaneous first-use calls both read `null`, both try to insert, one throws `DbUpdateException`). New file `src/Dorc.Database/Scripts/Post-Deployment/SeedRefDataAuditActions.sql`, referenced from `Script.PostDeployment.sql`:
  ```sql
  INSERT INTO [deploy].[RefDataAuditAction] (Action)
  SELECT v.Action FROM (VALUES ('Create'), ('Update'), ('Delete'), ('Attach'), ('Detach')) AS v(Action)
  WHERE NOT EXISTS (SELECT 1 FROM [deploy].[RefDataAuditAction] a WHERE a.Action = v.Action);
  ```
  Runs on every publish, idempotent, race-free.
- **Wire audit writes** (controller layer):
  - `RefDataDaemonsController`: POST → Create; PUT → Update (skip when `before == after`, matching `ScriptsAuditPersistentSource.AddRecord`); DELETE → Delete.
  - `ServerDaemonsController`: POST → Attach; DELETE → Detach. Payloads per the SC-05 table.
- **New read surface**: `DaemonAuditController` with GET `/DaemonAudit?daemonId=…` returning paged history (mirroring `RefDataProjectAuditController`). Simple daemon-audit view component on the daemon details page, modelled on the project-audit view.

#### Why it changes
DF-10: daemon mutations are currently unaudited, inconsistent with every other RefData area. DF-12: the existing `ManageProjectsPersistentSource.InsertRefDataAudit` path relies on `deploy.RefDataAuditAction` rows that have no seed mechanism in the repo (U-4 dig confirmed); the same post-deploy seed that unblocks the new daemon audit also retroactively unblocks the existing project-audit path without touching `ManageProjectsPersistentSource`'s source code.

#### Dependencies
Depends on SD-1 (`deploy.DaemonAudit` table) and SD-4 (role-identity reliability for `Username` stamping). Independent of SD-2, SD-3, SD-6.

#### Verification intent
- **SC-05 payload tests** (unit/integration): one test per action type (Create, Update, Update-skipped-when-unchanged, Delete, Attach, Detach), verifying the audit row's `FromValue`/`ToValue`/`RefDataAuditActionId`/`Username` match the conventions in SC-05.
- **SC-05b test (i) — empty `RefDataAuditAction`**: runs against a CI DB that has schema deployed but no post-deploy seed run. Asserts `SELECT COUNT(*) FROM deploy.RefDataAuditAction = 0`, triggers a project PUT via the test harness, asserts it **still succeeds after the seed is executed**. Runs the seed programmatically from the test, not via SSDT publish (because SSDT-publish-mid-test-execution is not supported by the repo's test infrastructure).
- **SC-05b test (ii) — pre-seeded `RefDataAuditAction`**: same DB, post-seed, asserts that a second seed run is a no-op (row count unchanged) and that a project PUT continues to succeed.
- **Test project**: these tests live in a **new** `Dorc.Database.IntegrationTests` project (no such project exists today). It carries the migration-and-seed verification for both SC-02 and SC-05b. Creating the project is an explicit subtask of SD-5 — it is the right home because the tests exercise DB schema + SSDT script behaviour, not API/Monitor behaviour. The unit-level audit payload tests (SC-05 per-action) live in the existing `Dorc.Api.Tests` project.

### SD-6 — UI fixes and Edit/Delete surface (addresses DF-4, DF-5, DF-6, DF-11)
- **DF-4**: `src/dorc-web/src/components/add-daemon.ts` `maxFieldLength = 50` → `250`.
- **DF-5**: `src/dorc-web/src/components/grid-button-groups/daemon-controls.ts` lines 76–86 — the existing getters `startDisabled` / `stopDisabled` / `restartDisabled` read `this.userEditable === true` (inverted). **Decision: invert in-place**: `this.userEditable !== true`. The property name `userEditable` is already reasonable (readable as "user is editable" ≈ "user has edit rights") — renaming adds churn across the existing callers without a clear win. This also minimises the diff in a file not otherwise changed. Unit test covers the four states (running+editable, running+non-editable, stopped+editable, stopped+non-editable).
- **DF-6**: Remove `.readonly="${true}"` on AccountName and Type in the Add dialog. Initial values remain `"Local System Account"` / `"Windows Service"` (Round 1 decision — Linux support acknowledged as future work in §8).
- **DF-11**: Add Edit and Delete row actions to `src/dorc-web/src/pages/page-daemons-list.ts`. Visibility gated by the current user's roles (Edit: PowerUser or Admin; Delete: Admin only). Delete requires confirmation and warns if the daemon is attached to any servers (per SD-6 resolves U-7; see unknowns register).

---

## 7. Deployment Phasing and Rollback

The PR ships as one code change but the deploy is two SSDT phases (pre-deploy + post-deploy) sandwiching the schema/code deploy. The ordering matters; this section locks it in.

### Publish order
1. **Pre-deploy** (`Script.PreDeployment.sql`):
   - `StageServicesForMigration.sql` — stages `dbo.SERVICE` → `dbo.SERVICE_MIGRATION_STAGING` and `dbo.SERVER_SERVICE_MAP` → `dbo.SERVER_SERVICE_MAP_MIGRATION_STAGING`, then empties the legacy tables (map first, service second — FK order) so SSDT's schema phase can drop them without `BlockOnPossibleDataLoss` warnings. Guarded by `IF OBJECT_ID('dbo.SERVICE') IS NOT NULL` and by staging-table existence checks; idempotent.
2. **Schema deploy** (SSDT main phase):
   - Creates `deploy.Daemon`, `deploy.ServerDaemon`, `deploy.DaemonAudit`, and their indexes / FKs.
   - Drops `dbo.SERVICE` and `dbo.SERVER_SERVICE_MAP` (because their `.sql` sources are removed — SSDT reconciles). Drop is data-safe because pre-deploy emptied them.
3. **Post-deploy** (`Script.PostDeployment.sql`):
   - `MigrateStagedServicesToDaemons.sql` — copies `dbo.*_MIGRATION_STAGING` → `deploy.Daemon` / `deploy.ServerDaemon` with `IDENTITY_INSERT` preserving Ids, then drops the staging tables.
   - `SeedRefDataAuditActions.sql` — seeds `Create`, `Update`, `Delete`, `Attach`, `Detach` rows in `deploy.RefDataAuditAction` if absent.
   - Existing `CleanupOrphanedScripts.sql` continues to run.
4. **Application deploy**: Api / Monitor / Runner / UI binaries pointing at the new EF config. If the schema deploy and app deploy are not atomic, there is a brief window where the old app is pointing at `dbo.SERVICE` (which is now gone) but expecting `deploy.Daemon` (which now exists). Acceptable for a scheduled maintenance-window deploy; the deployment plan must sequence schema-first with app-second, both within the same maintenance window.

### Rollback posture
- **SD-1 alone is not individually revertible.** Reverting schema changes is a DB-level operation; once `dbo.SERVICE` is dropped, bringing it back requires restore-from-backup or a reverse-migration script (not written in this PR). Mitigation: the pre-deploy staging + post-deploy copy preserves every row with identical Ids, so a forward-only recovery is to restore the DB from pre-deploy backup if the post-deploy copy never ran. If the post-deploy staging-copy ran but the publish was rolled back before application deploy, `deploy.Daemon` and `deploy.ServerDaemon` will be populated with the rescued data — they are harmless if unused by the old application binaries.
- **SD-2, SD-3, SD-4, SD-5 (code), SD-6** are each individually revertible at the application layer — reverting the relevant commit file group reverts the behaviour without touching the DB. The DB retains the new tables and audit rows; they simply stop being written.
- **SD-5 post-deploy seed** is revertible only via DELETE of the five seed rows, which would re-break the project audit path. In practice the seed is strictly additive and low-risk; revert is not expected.
- **PR-level rollback**: the clean way to roll back this PR end-to-end is (a) `git revert` the merge commit at the app layer, (b) keep the DB schema as-is — the new tables are harmless when unused, and `dbo.SERVICE` restoration is a separate recovery action if required. This is why the single-PR decision (U-8, C-06) is acceptable: per-SD surgical rollback at the code layer is still possible; DB rollback is always all-or-nothing regardless.

### Test environment topology (SC-02 + SC-05b coherence)
- SC-02 is verified on a preprod DB with populated `dbo.SERVICE` — the realistic migration scenario.
- SC-05b test **(i)** (empty `deploy.RefDataAuditAction`) runs on a fresh CI DB before the post-deploy seed is applied — i.e., against the schema-deploy output but not yet the post-deploy.
- SC-05b test **(ii)** (pre-seeded) runs after the post-deploy has executed once.
- The tests do not conflict because SC-02 and SC-05b exercise different tables.

---

## 8. Unknowns Register

| ID  | Description | Owner | Blocking | Resolution |
|-----|-------------|-------|---------|------------|
| U-1 | How many rows exist in `dbo.SERVICE` / `dbo.SERVER_SERVICE_MAP` in prod/preprod? Informs migration rehearsal scope and SC-02 verification effort. | User | Non-blocking | **Unresolved** |
| U-2 | Consumers of `dbo.SERVICE` or `dbo.SERVER_SERVICE_MAP` outside the DORC codebase. | User | Was Blocking for SD-1 post-deploy drop | **RESOLVED 2026-04-24.** User confirmed no external consumers. **Scan method**: `Grep pattern "SERVICE\|SERVER_SERVICE_MAP\|Service_Name\|Service_ID\|Daemon\|RefDataDaemons\|ServerDaemons\|DaemonStatus" -i` across `C:\src\dorc-cli` on 2026-04-24; all 20 hits resolved to `.csproj` / `appsettings.json` (MSBuild build-task references, `"Service"` substring matches) or one unrelated `dbo.USER_SERVICEMONITOR` table. Zero references to the daemon tables / endpoints. |
| U-3 | Is there a staging/preprod DB where the pre-deploy migration can be rehearsed before production publish? | User | **Blocking for SC-02** unless fallback is taken (R2-M7) | **Unresolved — fallback documented**: if no preprod DB is available, the SC-02 verification procedure runs against a CI-ephemeral DB inside the new `Dorc.Database.IntegrationTests` project (SD-5). The CI harness seeds the DB with representative `dbo.SERVICE` and `dbo.SERVER_SERVICE_MAP` rows (e.g. 10+ rows each), executes the SSDT publish against a local SQL instance, and performs the COUNT/MAX/EXCEPT checks described in SC-02. This proves migration correctness at unit-of-work granularity; it does not prove scale behaviour on the production-sized dataset. Confirming a preprod DB exists would upgrade the verification; lacking one, the CI fallback is treated as sufficient for merge. |
| U-4 | Where are `deploy.RefDataAuditAction` rows seeded today? | User | Was Blocking for SD-5 | **RESOLVED 2026-04-24.** There is no seed in main. The only auto-seed code ever written lives in dangling commits `14cf3aaa` and `6cb9aace`. SD-5 resolves this via post-deploy seed (SQL, idempotent) rather than application-level check-then-insert (Round 1's approach — revised after R2-M2 flagged concurrency). The seed fixes both the new daemon audit and the existing project-audit path (DF-12, SC-05b). |
| U-5 | Out-of-repo daemon-endpoint consumers (Runner, Monitor, external). | User | Non-blocking | **RESOLVED 2026-04-24** (same scan as U-2). Runner/Monitor in-repo consume daemons via the EF model; no raw endpoint or table access. Rename-through-EF safe. |
| U-6 | Target column/model/UI width. | User | Was Blocking | **RESOLVED 2026-04-24 — 250.** |
| U-7 | Delete confirmation UX? | User | Non-blocking | **RESOLVED (by SD-6 decision).** Delete requires a confirmation dialog and, when the daemon has rows in `deploy.ServerDaemon`, warns with the attached server names before proceeding. |
| U-8 | Single PR vs sliced. | User | Non-blocking | **RESOLVED 2026-04-24** — one PR. |
| U-9 | `deploy.DaemonAudit` design. | User | Non-blocking | **RESOLVED 2026-04-24** — dedicated table. |
| U-10 | Composite PK vs explicit `Id` PK on `deploy.ServerDaemon`. Issue text says explicit `Id`; Round 2 HLPS pivots to composite PK `(ServerId, DaemonId)` to preserve EF's skip-navigation shape (R2-H2). Trade-off: composite is smaller-diff and structurally equivalent for this use case (no other table references individual mapping rows). | User | Was Blocking for SD-1 | **RESOLVED 2026-04-24** — composite PK `(ServerId, DaemonId)`. Deviates from the issue text; user explicitly approved ("go with recommendation" in HLPS Round 2 authoring conversation, 2026-04-24, immediately after the deviation was flagged). |
| U-11 | Is daemon-audit read data (actor username on every mutation, plus the mutated `AccountName` / `Type` values in `FromValue`/`ToValue` JSON) acceptable to expose to all authenticated users — mirroring `RefDataProjectAuditController`'s open-read posture? | User | Non-blocking for merge | **RESOLVED 2026-04-24 — open read** (authenticated users, no PowerUser/Admin gate), matching `RefDataProjectAuditController`. `AccountName` is already visible on the daemons list page, so the audit view adds the actor/time dimension but not the underlying account-name data. |

---

## 9. Out-of-Scope Risks

- **Runner-side probe reliability**: Daemon start/stop/restart ultimately calls Windows `ServiceController`. This work does not change that behaviour. If the probe itself is flaky, the new `ErrorMessage` surfacing in SD-3 will make that flakiness visible where it was silent before — some operators may read the visibility as a regression even though the underlying reliability is unchanged.
- **PR size and review fragmentation** (C-06): A single PR spanning DB + EF + controllers + UI + audit is large. Mitigation: the PR description provides a per-SD review checklist with file-path anchors so reviewers can divide the diff; each SD's diff is coherent when read in isolation; the adversarial review panel at code stage explicitly expects to assign SD-1 (DB+EF) and SD-6 (UI) to different reviewers where possible.
- **DaemonAuditController authorisation model**: The new read endpoint mirrors `RefDataProjectAuditController`'s open-to-any-authenticated-user posture. If the Daemon audit trail is later considered more sensitive than the Project audit trail, the read gate should be tightened in a follow-up. Not a blocker here.
- **Windows-daemon assumptions in UI defaults / status vocabulary** (breadcrumb for future Linux-daemon support): The schema (`deploy.Daemon`, `deploy.ServerDaemon`) is OS-agnostic, but three soft biases remain. (1) `add-daemon.ts` defaults `Type` to `"Windows Service"` and `AccountName` to `"Local System Account"`. SD-6 makes the fields editable but leaves the defaults in place. (2) `ServiceStatusApiModel.ServiceStatus` is a free string carrying Windows vocabulary (`Running` / `Stopped` / `Paused` / `StopPending` / `StartPending`) populated by `ServiceController`. A future systemd adapter will either normalise to the same vocabulary or motivate broadening the enum. (3) No `IServiceControl` abstraction exists over `ServiceStatus.cs`'s direct `ServiceController` calls. This PR deliberately does not add one — it removes the bad write-side-effect and surfaces errors, but the Windows binding inside `ServiceStatus.cs` is unchanged, and a future Linux PR will introduce the abstraction then. None of these are blockers for adding Linux support later; they are the points where that future work will be scoped.
- **DF-12 retrofit risk retired**: Round 1 proposed an application-level check-then-insert retrofit of `ManageProjectsPersistentSource.InsertRefDataAudit` and flagged concurrency + regression risk. Round 2 replaces that with a post-deploy SQL seed (SD-5), which is strictly additive, race-free, and does not modify `ManageProjectsPersistentSource`. The risk previously listed here is resolved.
