/*
 Post-Deployment Script: give the migrated reference-data objects their
 deploy-schema casing (docs/tag-schema-standardisation, S-001).

 The refactorlog moves dbo.DATABASE and dbo.SERVER into the deploy schema and
 renames their columns, but it cannot rename the tables themselves: the dacpac
 model collation is case-insensitive (1033,CI in Dorc.Database.sqlproj), so
 DacFx compares SERVER to Server, finds them equal and drops the rename as a
 no-op. Chaining the rename through a staging name does not help either — DacFx
 collapses the chain before it emits anything. The same applies to PK_SERVER
 vs PK_Server.

 deploy.Database usually arrives correctly cased anyway, because the constraint
 and index renames force DacFx down its table-rebuild path and the rebuild ends
 in sp_rename to the model's spelling. That is an implementation detail of the
 diff, not a contract, so this script covers both tables.

 Every rename goes via a staging name that differs by more than case, so no
 single sp_rename call is ever asked to distinguish two names a case-insensitive
 server considers identical.

 Each guard compares under Latin1_General_BIN2 so it matches on exact casing
 only: once an object is correctly cased the guard is false and the script is a
 no-op, which is what makes re-running it safe.
*/

-- ---------- deploy.DATABASE -> deploy.Database ----------
IF EXISTS (SELECT 1
           FROM sys.tables t
           JOIN sys.schemas s ON s.schema_id = t.schema_id
           WHERE s.name = 'deploy'
             AND t.name COLLATE Latin1_General_BIN2 = N'DATABASE' COLLATE Latin1_General_BIN2)
BEGIN
    EXEC sp_rename N'[deploy].[DATABASE]', N'DatabaseCasingStage';
    EXEC sp_rename N'[deploy].[DatabaseCasingStage]', N'Database';
    PRINT 'Renamed deploy.DATABASE -> deploy.Database';
END
GO

-- ---------- deploy.SERVER -> deploy.Server ----------
IF EXISTS (SELECT 1
           FROM sys.tables t
           JOIN sys.schemas s ON s.schema_id = t.schema_id
           WHERE s.name = 'deploy'
             AND t.name COLLATE Latin1_General_BIN2 = N'SERVER' COLLATE Latin1_General_BIN2)
BEGIN
    EXEC sp_rename N'[deploy].[SERVER]', N'ServerCasingStage';
    EXEC sp_rename N'[deploy].[ServerCasingStage]', N'Server';
    PRINT 'Renamed deploy.SERVER -> deploy.Server';
END
GO

-- ---------- PK_DATABASE -> PK_Database ----------
IF EXISTS (SELECT 1
           FROM sys.key_constraints k
           JOIN sys.schemas s ON s.schema_id = k.schema_id
           WHERE s.name = 'deploy'
             AND k.name COLLATE Latin1_General_BIN2 = N'PK_DATABASE' COLLATE Latin1_General_BIN2)
BEGIN
    EXEC sp_rename N'[deploy].[PK_DATABASE]', N'PK_DatabaseCasingStage', N'OBJECT';
    EXEC sp_rename N'[deploy].[PK_DatabaseCasingStage]', N'PK_Database', N'OBJECT';
    PRINT 'Renamed deploy.PK_DATABASE -> PK_Database';
END
GO

-- ---------- PK_SERVER -> PK_Server ----------
IF EXISTS (SELECT 1
           FROM sys.key_constraints k
           JOIN sys.schemas s ON s.schema_id = k.schema_id
           WHERE s.name = 'deploy'
             AND k.name COLLATE Latin1_General_BIN2 = N'PK_SERVER' COLLATE Latin1_General_BIN2)
BEGIN
    EXEC sp_rename N'[deploy].[PK_SERVER]', N'PK_ServerCasingStage', N'OBJECT';
    EXEC sp_rename N'[deploy].[PK_ServerCasingStage]', N'PK_Server', N'OBJECT';
    PRINT 'Renamed deploy.PK_SERVER -> PK_Server';
END
GO

-- ---------- Legacy IX_DATABASE_Group_ID -> IX_Database_GroupId ----------
-- Never part of the SSDT model (it exists only where it was created by hand), so
-- the diff will not touch it. Renamed here when present so the estate matches
-- what DatabaseEntityTypeConfiguration declares.
IF EXISTS (SELECT 1
           FROM sys.indexes i
           WHERE i.object_id = OBJECT_ID(N'[deploy].[Database]')
             AND i.name COLLATE Latin1_General_BIN2 = N'IX_DATABASE_Group_ID' COLLATE Latin1_General_BIN2)
BEGIN
    EXEC sp_rename N'[deploy].[Database].[IX_DATABASE_Group_ID]', N'IX_Database_GroupId', N'INDEX';
    PRINT 'Renamed index IX_DATABASE_Group_ID -> IX_Database_GroupId';
END
GO
