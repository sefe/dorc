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

 Interruption safety. The two hops are not one operation, so each block runs in
 a transaction under SET XACT_ABORT ON, and each has a second branch that
 completes a half-finished rename. Without that branch an interruption between
 the hops would strand the object under its staging name: the first guard tests
 for the ORIGINAL name, so it would never fire again, and the next publish would
 see the modelled table missing from the target and — with DropObjectsNotInSource
 off — create a new empty one beside the stranded data.

 Each guard compares under Latin1_General_BIN2 so it matches on exact casing
 only: once an object is correctly cased both guards are false and the script is
 a no-op, which is what makes re-running it safe.
*/

-- ---------- deploy.DATABASE -> deploy.Database ----------
SET XACT_ABORT ON;

IF EXISTS (SELECT 1
           FROM sys.tables t
           JOIN sys.schemas s ON s.schema_id = t.schema_id
           WHERE s.name = 'deploy'
             AND t.name COLLATE Latin1_General_BIN2 = N'DATABASE' COLLATE Latin1_General_BIN2)
BEGIN
    BEGIN TRANSACTION;
    EXEC sp_rename N'[deploy].[DATABASE]', N'DatabaseCasingStage';
    EXEC sp_rename N'[deploy].[DatabaseCasingStage]', N'Database';
    COMMIT TRANSACTION;
    PRINT 'Renamed deploy.DATABASE -> deploy.Database';
END
ELSE IF EXISTS (SELECT 1
                FROM sys.tables t
                JOIN sys.schemas s ON s.schema_id = t.schema_id
                WHERE s.name = 'deploy'
                  AND t.name COLLATE Latin1_General_BIN2 = N'DatabaseCasingStage' COLLATE Latin1_General_BIN2)
BEGIN
    EXEC sp_rename N'[deploy].[DatabaseCasingStage]', N'Database';
    PRINT 'Completed an interrupted rename: deploy.DatabaseCasingStage -> deploy.Database';
END
GO

-- ---------- deploy.SERVER -> deploy.Server ----------
SET XACT_ABORT ON;

IF EXISTS (SELECT 1
           FROM sys.tables t
           JOIN sys.schemas s ON s.schema_id = t.schema_id
           WHERE s.name = 'deploy'
             AND t.name COLLATE Latin1_General_BIN2 = N'SERVER' COLLATE Latin1_General_BIN2)
BEGIN
    BEGIN TRANSACTION;
    EXEC sp_rename N'[deploy].[SERVER]', N'ServerCasingStage';
    EXEC sp_rename N'[deploy].[ServerCasingStage]', N'Server';
    COMMIT TRANSACTION;
    PRINT 'Renamed deploy.SERVER -> deploy.Server';
END
ELSE IF EXISTS (SELECT 1
                FROM sys.tables t
                JOIN sys.schemas s ON s.schema_id = t.schema_id
                WHERE s.name = 'deploy'
                  AND t.name COLLATE Latin1_General_BIN2 = N'ServerCasingStage' COLLATE Latin1_General_BIN2)
BEGIN
    EXEC sp_rename N'[deploy].[ServerCasingStage]', N'Server';
    PRINT 'Completed an interrupted rename: deploy.ServerCasingStage -> deploy.Server';
END
GO

-- ---------- PK_DATABASE -> PK_Database ----------
SET XACT_ABORT ON;

IF EXISTS (SELECT 1
           FROM sys.key_constraints k
           JOIN sys.schemas s ON s.schema_id = k.schema_id
           WHERE s.name = 'deploy'
             AND k.name COLLATE Latin1_General_BIN2 = N'PK_DATABASE' COLLATE Latin1_General_BIN2)
BEGIN
    BEGIN TRANSACTION;
    EXEC sp_rename N'[deploy].[PK_DATABASE]', N'PK_DatabaseCasingStage', N'OBJECT';
    EXEC sp_rename N'[deploy].[PK_DatabaseCasingStage]', N'PK_Database', N'OBJECT';
    COMMIT TRANSACTION;
    PRINT 'Renamed deploy.PK_DATABASE -> PK_Database';
END
ELSE IF EXISTS (SELECT 1
                FROM sys.key_constraints k
                JOIN sys.schemas s ON s.schema_id = k.schema_id
                WHERE s.name = 'deploy'
                  AND k.name COLLATE Latin1_General_BIN2 = N'PK_DatabaseCasingStage' COLLATE Latin1_General_BIN2)
BEGIN
    EXEC sp_rename N'[deploy].[PK_DatabaseCasingStage]', N'PK_Database', N'OBJECT';
    PRINT 'Completed an interrupted rename: PK_DatabaseCasingStage -> PK_Database';
END
GO

-- ---------- PK_SERVER -> PK_Server ----------
SET XACT_ABORT ON;

IF EXISTS (SELECT 1
           FROM sys.key_constraints k
           JOIN sys.schemas s ON s.schema_id = k.schema_id
           WHERE s.name = 'deploy'
             AND k.name COLLATE Latin1_General_BIN2 = N'PK_SERVER' COLLATE Latin1_General_BIN2)
BEGIN
    BEGIN TRANSACTION;
    EXEC sp_rename N'[deploy].[PK_SERVER]', N'PK_ServerCasingStage', N'OBJECT';
    EXEC sp_rename N'[deploy].[PK_ServerCasingStage]', N'PK_Server', N'OBJECT';
    COMMIT TRANSACTION;
    PRINT 'Renamed deploy.PK_SERVER -> PK_Server';
END
ELSE IF EXISTS (SELECT 1
                FROM sys.key_constraints k
                JOIN sys.schemas s ON s.schema_id = k.schema_id
                WHERE s.name = 'deploy'
                  AND k.name COLLATE Latin1_General_BIN2 = N'PK_ServerCasingStage' COLLATE Latin1_General_BIN2)
BEGIN
    EXEC sp_rename N'[deploy].[PK_ServerCasingStage]', N'PK_Server', N'OBJECT';
    PRINT 'Completed an interrupted rename: PK_ServerCasingStage -> PK_Server';
END
GO
