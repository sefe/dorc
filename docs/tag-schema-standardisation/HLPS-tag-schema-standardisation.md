# HLPS: Reference-Data Schema Standardisation and a Single Name for Tags

| Field       | Value                                                              |
|-------------|--------------------------------------------------------------------|
| **Status**  | **DRAFT** — pending adversarial panel                              |
| **Author**  | Agent                                                              |
| **Date**    | 2026-08-10                                                         |
| **Folder**  | docs/tag-schema-standardisation/                                   |
| **Branch**  | claude/pr-774-schema-review-fhlt66 (rework of PR #774)             |
| **Origin**  | User review of PR #774: "the underlying DB table doesn't meet the standards we're trying to move to with the new deploy schema" and "the naming of the tag column throughout is inconsistent, it should have a consistent name throughout the codebase and DB" |

---

## 1. Problem Statement

PR #774 made database and server tags real — per-tag membership matching, per-tag
deployment variables, a 4000-character capacity, a chip editor. That semantic work is
sound and is retained. Two structural defects were rejected at review.

### Defect 1 — the tags landed on legacy tables

`DATABASE` and `SERVER` are the last two reference-data tables still sitting in the
`dbo` schema in the pre-modernisation shape:

```
dbo.DATABASE (DB_ID, DB_Name, DB_Type, Server_Name, Group_ID, Array_Name)
dbo.SERVER   (Server_ID, Server_Name, OS_Version, Application_Server_Name)
```

Everything else has moved to the `deploy` schema and its convention — PascalCase table
names, `Id`, `Name`, `PK_<Table>` / `FK_<Child>_<Parent>` / `IX_<Table>_<Columns>`. Six
`deploy`-schema tables already reach *back* into `dbo` for their foreign keys
(`EnvironmentDatabase`, `EnvironmentServer`, `ServerDaemon`, `DaemonObservation`,
`Project`, plus `dbo.ENVIRONMENT_USER_MAP`). Widening `DB_Type` in place would have
entrenched the legacy shape at exactly the moment the column was being reworked.

### Defect 2 — seven names for one concept

| Layer | Name before |
|---|---|
| `DATABASE` column | `DB_Type` |
| `SERVER` column | `Application_Server_Name` |
| `Database` entity / `DatabaseApiModel` | `Type` |
| `Server` entity / `ServerApiModel` | `ApplicationTags` |
| `UserDbPermissionApiModel` | `DbType` |
| `DatabaseDefinition` (deployment variable) | `Type` |
| `VariableValueServers` (deployment variable) | `ApplicationServerName` |
| Lookup method / route / query params | `GetDatabaseByType`, `ByType`, `type`, `dbType` |
| UI label | **Tags** |

`Application_Server_Name` is the sharpest case: a column whose name says it holds a
server's name, which in fact holds that server's tag list. Every reader has to be told
this out of band, and PR #774 added new code that had to be told it too.

## 2. Scope

**In scope**

- `dbo.DATABASE` → `deploy.Database`, `dbo.SERVER` → `deploy.Server`, with
  deploy-convention column and constraint names, data preserved.
- Repointing every foreign key, stored procedure, operational script, acceptance
  fixture and EF mapping that referenced the old tables.
- One name — `Tags` — for the concept at every layer: column, entity, API model,
  swagger, generated TypeScript client, UI, deployment-variable payload, and the
  lookup members named after it.

**Out of scope**

- `dbo.ENVIRONMENT_USER_MAP`, `dbo.AD_GROUP`, `dbo.PERMISSION`, `dbo.USERS`,
  `dbo.SQL_PORTS` and the `dbo` stored procedures. They are legacy too, but they are
  not what this PR touches; migrating them is separate work with its own risk.
- The tag *semantics* delivered by PR #774 (membership matching, per-tag variables,
  capacity, chip editor, normalisation). Retained unchanged apart from the renames.
- `Array_Name` → `ArrayName` is a mechanical part of the table move, but `ArrayName`
  remains storage-array metadata and is **not** a tag field. Its width stays at 50.

## 3. Constraints

- **C-1 — no data loss.** `deploy.Database` and `deploy.Server` are live production
  reference data. Rows, identity values and the foreign keys pointing at them must
  survive the deployment.
- **C-2 — the deployment is a dacpac publish.** SqlPackage generates its script by
  diffing the dacpac model against the *pre-migration* target and then runs
  `[pre-deploy] [generated schema changes] [post-deploy]`. A pre-deployment rename
  therefore cannot work: the generated script would still contain the `CREATE TABLE`
  the diff decided on, and it would collide with the renamed table. Renames have to be
  recorded in the refactorlog, which SqlPackage consumes at *generation* time.
- **C-3 — no SQL Server in the dev sandbox.** Verification has to be offline.
- **C-4 — compatibility level 100.** No `STRING_SPLIT`, no `THROW`.
- **C-5 — the model collation is case-insensitive** (`1033,CI`). DacFx treats
  `DATABASE` and `Database` as the same name.

## 4. Success Criteria

| # | Criterion | Evidence form |
|---|---|---|
| SC-1 | The generated deployment script moves and renames rather than dropping and recreating | `sqlpackage /a:Script` new dacpac vs. a dacpac built from `main`; inspect for `ALTER SCHEMA TRANSFER`, `sp_rename`, and the absence of any unguarded `DROP TABLE` |
| SC-2 | Redeploying the same dacpac is a no-op | Script new dacpac against itself; zero schema-change statements |
| SC-3 | The dacpac model is internally consistent | `dotnet build` of the sqlproj resolves every cross-object reference (SQL71501 would fail the build) |
| SC-4 | EF and the SSDT project agree | `DeploymentContextTagMappingTests` asserts schema, table name and column-name-equals-property-name for both entities |
| SC-5 | No occurrence of the old names survives | Repository-wide search for `DB_Type`, `Application_Server_Name`, `ApplicationTags`, `DbType`, `GetDatabaseByType` |
| SC-6 | Behaviour is unchanged | The PR #774 test suites pass unmodified except for the renames |

## 5. Unknowns Register

| # | Unknown | Status | Resolution |
|---|---|---|---|
| U-1 | Does `MSBuild.Sdk.SqlProj` 4.2.0 honour the refactorlog? | **RESOLVED** | Yes. `Dorc.Database.sqlproj` already declares `<RefactorLog Include="Dorc.Database.refactorlog" />` and the build passes `--refactorlog` to DacpacTool. Confirmed in the generated script, which emits the recorded operations. |
| U-2 | What XML shape does DacFx expect for a schema move? | **RESOLVED** | `Operation Name="Move Schema"` with `ElementName` / `ElementType` / `NewSchema` / `IsNewSchemaExternal`. Verified empirically — the generated script contains `ALTER SCHEMA [deploy] TRANSFER [dbo].[DATABASE]`. |
| U-3 | Will DacFx emit the table rename `DATABASE` → `Database`? | **RESOLVED — NO** | Under the case-insensitive model collation DacFx reports "Rename refactoring operation … is skipped … will not be renamed". Chaining through a staging name does not help; DacFx collapses the chain. Handled by a guarded post-deployment script instead (see §6). |
| U-4 | Does the table rebuild preserve identity values? | **RESOLVED** | Yes. The generated script wraps the swap in a serialisable transaction and copies rows under `SET IDENTITY_INSERT … ON` ordered by `Id`. |
| U-5 | Is renaming the public API surface acceptable? | **RESOLVED — user decision** | The user chose `Tags` everywhere in full knowledge that `DatabaseApiModel.Type`, `ServerApiModel.ApplicationTags` and `UserDbPermissionApiModel.DbType` are public fields. See §7. |
| U-6 | Are there in-repo consumers of the `ByType` route or the `usp_Insert_*` parameter names? | **RESOLVED — none** | Repository search finds only tests. External consumers are covered by §7. |
| U-7 | Can the change be executed against a real database here? | **OPEN — accepted** | C-3. Offline script generation (SC-1, SC-2) plus line-by-line review stands in for execution. The generated script should be reviewed before the production publish. **Not blocking**: it constrains confidence, not correctness of the design. |

No blocking unknowns.

## 6. Design Positions

**P-1 — the refactorlog is the mechanism, not a pre-deployment script.** Forced by
C-2. The refactorlog records the schema move and every column rename; SqlPackage turns
them into `ALTER SCHEMA TRANSFER` and `sp_rename` and then diffs against the *renamed*
picture, so what remains is the genuine change (widen `Tags`, restandardise constraint
names) rather than a create-and-drop.

**P-2 — a post-deployment script closes the casing gap.** U-3 means the physical tables
would keep the names `DATABASE` and `SERVER` inside the `deploy` schema. In practice
`deploy.Database` arrives correctly cased anyway, because the constraint renames push
DacFx down its table-rebuild path and the rebuild ends in `sp_rename` to the model's
spelling — but that is an implementation detail of the diff, not a contract, and
`deploy.Server` gets no rebuild at all. `ApplyDeployCasingToRefDataTables.sql` renames
both tables and both primary keys where the physical casing is still legacy. Each guard
compares under `Latin1_General_BIN2` so it matches on exact casing only and is a no-op
once correct; each rename goes via a staging name differing by more than case, so no
single `sp_rename` is asked to distinguish two names the server considers identical.

**P-3 — constraint and index names move to the convention too.** `PK_Database`,
`FK_Database_AdGroup`, `IX_Database_ServerName_Name`, `PK_Server`,
`FK_EnvironmentDatabase_Database`, `FK_EnvironmentServer_Server`,
`FK_EnvironmentUserMap_Database`. Leaving names like
`EnvironmentDatabase_DATABASE_DB_ID_fk` behind would preserve the old table name in the
schema in the one place nobody thinks to look.

**P-4 — `Tags`, not `DatabaseTags` / `ServerTags`.** Matches the `deploy` convention of
short unqualified column names (`Name`, `Owner`, `Description`), the UI label already in
use, and the `Tags` columns arriving with #773.

## 7. Consumer Impact

These are breaking changes for consumers outside this repository. They are the
consequence of the user's explicit decision under U-5, recorded here so the rollout
plan can carry them.

| Surface | Before | After |
|---|---|---|
| `DatabaseApiModel` field | `Type` | `Tags` |
| `ServerApiModel` field | `ApplicationTags` | `Tags` |
| `UserDbPermissionApiModel` field | `DbType` | `Tags` |
| Route | `GET /RefDataDatabases/ByType?type=` | `GET /RefDataDatabases/ByTag?tag=` |
| Query parameter | `GET /RefDataDatabaseUsers/GetDbUsersPermissions?dbType=` | `?tag=` |
| `DatabasePermissions` deployment variable | `Database.Type` | `Database.Tags` |
| `EnvironmentServers` deployment variable | `ApplicationServerName` | `Tags` |
| `usp_Insert_Database_Detail` parameter | `@DB_TYPE` | `@TAGS` |
| `usp_Insert_Server_Detail` parameter | `@APPLICATION_SERVER_NAME` | `@TAGS` |

Deploy scripts reading `$DatabasePermissions[…].Database.Type` or an
`EnvironmentServers[…].ApplicationServerName` are the highest-traffic of these and
should be swept before the release goes out.
