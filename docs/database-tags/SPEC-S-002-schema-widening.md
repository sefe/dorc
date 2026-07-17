# SPEC S-002 — Schema Widening, Dual-Sourced (database-tags)

**Status**: EXECUTED 2026-07-17 · **IS**: S-002 · **Gate**: DDL↔EF side-by-side.

## Changes (one step, C-1)

| Layer | File | Before | After |
|-------|------|--------|-------|
| SQL column | `src/Dorc.Database/dbo/Tables/DATABASE.sql` | `DB_Type NVARCHAR(250)` | `NVARCHAR(4000)` |
| Proc param | `src/Dorc.Database/dbo/Stored Procedures/usp_Insert_Database_Detail.sql` | `@DB_TYPE NVARCHAR(50)` | `NVARCHAR(4000)` (U-3: widen, not delete) |
| EF metadata | `DatabaseEntityTypeConfiguration.cs` | `Type HasMaxLength(50)` | `HasMaxLength(4000)` (literal, matching the `ServerEntityTypeConfiguration.ApplicationTags` convention) |

## Width-lock test (C-3)

`DeploymentContextTagWidthTests`: new `DatabaseTags_AreWidenedToTheLimit` asserts
`Type` = `TagLimits.MaxTagStringLength`; `DatabaseFields_KeepTheirCurrentWidths`
keeps `ArrayName`/`Name`/`ServerName` pinned at 50 with the ArrayName correction
comment retained.

## DDL↔EF side-by-side (gate artifact)

| Item | DDL | EF model |
|------|-----|----------|
| `DB_Type` / `Database.Type` | `NVARCHAR(4000)` | `GetMaxLength() == 4000` (test-asserted) |
| `Array_Name` / `ArrayName` | `NVARCHAR(250)` (untouched) | 50 (pinned, untouched) |
| `DB_Name` / `Name` | `NVARCHAR(250)` (untouched) | 50 (pinned, untouched) |
| `Server_Name` / `ServerName` | `NVARCHAR(250)` (untouched) | 50 (pinned, untouched) |
| `@DB_TYPE` proc param | `NVARCHAR(4000)` | n/a |

Nothing else in either file changed (git diff is the proof). The pre-existing
250-vs-50 DDL/EF drift on the three frozen fields is inherited, documented
(tag-capacity HLPS §1), and out of scope.

## Evidence routing

Dacpac build / SQL syntax validation delegated to CI (sqlproj not buildable on
Linux — TOOLCHAIN-S-001 discipline).
