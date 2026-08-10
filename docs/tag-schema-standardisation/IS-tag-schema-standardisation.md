# IS: Reference-Data Schema Standardisation and a Single Name for Tags

| Field       | Value                                                    |
|-------------|----------------------------------------------------------|
| **Status**  | **DRAFT** — pending adversarial panel                    |
| **HLPS**    | HLPS-tag-schema-standardisation.md                       |
| **Date**    | 2026-08-10                                               |

Two steps. S-001 is the database move; S-002 is the rename that runs through every
layer above it. They are separate commits because the first is the one that can lose
data and wants to be reviewable on its own.

---

## S-001 — Move `DATABASE` and `SERVER` into the `deploy` schema

**Goal.** `deploy.Database` and `deploy.Server` exist with deploy-convention columns
and constraint names, holding the rows that were in `dbo.DATABASE` and `dbo.SERVER`.

**Sequence.**

1. Add `deploy/Tables/Database.sql` and `deploy/Tables/Server.sql` in the target shape;
   delete the two `dbo/Tables` definitions. The sqlproj globs `**/*.sql`, so no project
   edit is needed.
2. Repoint every foreign key that referenced the old tables, renaming the constraints
   to the convention as they move: `EnvironmentDatabase`, `EnvironmentServer`,
   `ServerDaemon`, `DaemonObservation`, `deploy.Project`, `dbo.ENVIRONMENT_USER_MAP`.
   The sqlproj build resolves cross-object references, so a missed one is a build
   error (SQL71501) rather than a runtime surprise.
3. Rewrite `usp_Insert_Database_Detail` and `usp_Insert_Server_Detail` against the new
   tables and columns.
4. Record the move in `Dorc.Database.refactorlog`: `Move Schema` per table, then the
   column renames. Per HLPS U-3 the table rename itself will be discarded by DacFx and
   is handled in step 5.
5. Add `Scripts/Post-Deployment/ApplyDeployCasingToRefDataTables.sql` and wire it first
   in `Script.PostDeployment.sql`.
6. Repoint the scripts and fixtures that read the old tables: the
   `NormalizeDatabaseTags` and `MigrateStagedServicesToDaemons` post-deployment
   scripts, `install-scripts/AuditDatabaseTags.sql`, `Tests.Acceptance` `DataAccessor`
   and `DaemonSchemaMigrationTests`. `LegacyDaemonSeed.sql` deliberately keeps creating
   `dbo.SERVER` — it seeds the *pre*-migration state before the publish under test.

**Verification (HLPS SC-1..SC-3).** Build a dacpac from `main` and one from the branch;
`sqlpackage /a:Script` the second against the first with `BlockOnPossibleDataLoss=True`
and read the result. Then script the new dacpac against itself and confirm no schema
changes. Neither needs a database.

**Exit.** The generated script transfers both tables, renames every column, rebuilds
`deploy.Database` with an `IDENTITY_INSERT` row copy inside a serialisable transaction,
recreates the five foreign keys under their new names, and contains no unguarded
`DROP TABLE`. Re-scripting yields nothing.

---

## S-002 — One name for the tag concept

**Goal.** `Tags` at every layer, and no occurrence of the superseded names anywhere in
the repository.

**Sequence.**

1. Entities and API models: `Database.Type`, `Server.ApplicationTags`,
   `DatabaseApiModel.Type`, `ServerApiModel.ApplicationTags`,
   `UserDbPermissionApiModel.DbType`, `DatabaseDefinition.Type`,
   `VariableValueServers.ApplicationServerName` → `Tags`.
2. EF configurations: drop every `HasColumnName` remap on the two entities — the
   columns are now named after their properties — and point them at the `deploy`
   schema. Take the tag width from `TagLimits.MaxTagStringLength` rather than a
   literal, so EF and the SSDT column cannot drift apart silently.
3. Members named after the concept: `GetDatabaseByType` → `GetDatabaseByTag` (both
   overloads and the interface), route `ByType` → `ByTag`, query parameters `type` and
   `dbType` → `tag`, and the validation messages that quote them.
4. `swagger.json`: the three schema properties, the path, and the two query parameter
   names. Then the generated TypeScript models and `RefDataDatabaseUsersApi`.
5. UI: the tag property reads and writes, plus the local identifiers that carried the
   old vocabulary (`DatabaseType`, `applicationTagsRenderer`, `typeFilter`,
   `applicationTagsFilter`). The grid filter and sort `path` values are **not**
   cosmetic — `GetDatabaseApiModelByPage` resolves them with
   `Expression.PropertyOrField` against the entity, so `path: 'Type'` would throw once
   the entity property is `Tags`.
6. Extend the EF-mapping test to pin schema, table name and
   column-name-equals-property-name for both entities, so a future `HasColumnName`
   cannot quietly reintroduce the divergence.
7. Update the remaining tests, the acceptance feature and its generated code, and
   `install-scripts/TestHarness.ps1`.

**Verification (HLPS SC-4..SC-6).** `dotnet build` of the solution; `Dorc.Core.Tests`,
`Dorc.Api.Tests`, `Dorc.Monitor.Tests`; `tsc --noEmit` and the `dorc-web` suite; and a
repository-wide search for the superseded names.

**Exit.** All suites green and the search clean.

---

## Sequencing note

S-001 before S-002 is not arbitrary. Once the EF configuration points at
`deploy.Database`, the `DatabaseTagMatchTranslationTests` `ToQueryString` assertion is
what proves the whole stack — entity, configuration and SSDT table — agrees on where
the data lives and what the column is called. Doing the database first means that test
fails loudly on a mismatch instead of passing against a stale mapping.
