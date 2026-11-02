/*
 Post-Deployment Script: give the migrated reference-data objects their
 deploy-schema casing.

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
 a transaction inside TRY/CATCH: any failure rolls back and re-raises, leaving
 the object under its ORIGINAL name where the first guard will find it again on
 the next publish.

 TRY/CATCH rather than SET XACT_ABORT ON, for two reasons. sp_rename reports
 failures with RAISERROR, and XACT_ABORT explicitly does not act on
 RAISERROR-raised errors — so a failed second hop would fall through to COMMIT
 and persist a half-rename while printing success. And XACT_ABORT is a
 connection-level setting that would leak into every post-deployment script
 after this one, none of which were written under it.

 The second branch of each block completes a rename that is already stranded
 under its staging name. It cannot fire during a normal publish — DacFx would
 have generated a CREATE TABLE for the missing modelled table in the main body,
 which runs first and fails on the staging table's constraint names. It is there
 so that an operator can run this script standalone to repair such an estate,
 which is the supported recovery path if one ever arises.

 Each guard compares under Latin1_General_BIN2 so it matches on exact casing
 only: once an object is correctly cased both guards are false and the script is
 a no-op, which is what makes re-running it safe.
*/

-- ---------- deploy.DATABASE -> deploy.Database ----------
IF EXISTS (SELECT 1
           FROM sys.tables t
           JOIN sys.schemas s ON s.schema_id = t.schema_id
           WHERE s.name = 'deploy'
             AND t.name COLLATE Latin1_General_BIN2 = N'DATABASE' COLLATE Latin1_General_BIN2)
BEGIN
    BEGIN TRY
        BEGIN TRANSACTION;
        EXEC sp_rename N'[deploy].[DATABASE]', N'DatabaseCasingStage';
        EXEC sp_rename N'[deploy].[DatabaseCasingStage]', N'Database';
        COMMIT TRANSACTION;
        PRINT 'Renamed deploy.DATABASE -> deploy.Database';
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        DECLARE @dbErr NVARCHAR(2048) = ERROR_MESSAGE();
        RAISERROR('Failed to rename deploy.DATABASE -> deploy.Database: %s', 16, 1, @dbErr);
    END CATCH
END
ELSE IF EXISTS (SELECT 1
                FROM sys.tables t
                JOIN sys.schemas s ON s.schema_id = t.schema_id
                WHERE s.name = 'deploy'
                  AND t.name COLLATE Latin1_General_BIN2 = N'DatabaseCasingStage' COLLATE Latin1_General_BIN2)
BEGIN
    EXEC sp_rename N'[deploy].[DatabaseCasingStage]', N'Database';
    PRINT 'Completed a stranded rename: deploy.DatabaseCasingStage -> deploy.Database';
END
GO

-- ---------- deploy.SERVER -> deploy.Server ----------
IF EXISTS (SELECT 1
           FROM sys.tables t
           JOIN sys.schemas s ON s.schema_id = t.schema_id
           WHERE s.name = 'deploy'
             AND t.name COLLATE Latin1_General_BIN2 = N'SERVER' COLLATE Latin1_General_BIN2)
BEGIN
    BEGIN TRY
        BEGIN TRANSACTION;
        EXEC sp_rename N'[deploy].[SERVER]', N'ServerCasingStage';
        EXEC sp_rename N'[deploy].[ServerCasingStage]', N'Server';
        COMMIT TRANSACTION;
        PRINT 'Renamed deploy.SERVER -> deploy.Server';
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        DECLARE @svErr NVARCHAR(2048) = ERROR_MESSAGE();
        RAISERROR('Failed to rename deploy.SERVER -> deploy.Server: %s', 16, 1, @svErr);
    END CATCH
END
ELSE IF EXISTS (SELECT 1
                FROM sys.tables t
                JOIN sys.schemas s ON s.schema_id = t.schema_id
                WHERE s.name = 'deploy'
                  AND t.name COLLATE Latin1_General_BIN2 = N'ServerCasingStage' COLLATE Latin1_General_BIN2)
BEGIN
    EXEC sp_rename N'[deploy].[ServerCasingStage]', N'Server';
    PRINT 'Completed a stranded rename: deploy.ServerCasingStage -> deploy.Server';
END
GO

-- ---------- PK_DATABASE -> PK_Database ----------
IF EXISTS (SELECT 1
           FROM sys.key_constraints k
           JOIN sys.schemas s ON s.schema_id = k.schema_id
           WHERE s.name = 'deploy'
             AND k.name COLLATE Latin1_General_BIN2 = N'PK_DATABASE' COLLATE Latin1_General_BIN2)
BEGIN
    BEGIN TRY
        BEGIN TRANSACTION;
        EXEC sp_rename N'[deploy].[PK_DATABASE]', N'PK_DatabaseCasingStage', N'OBJECT';
        EXEC sp_rename N'[deploy].[PK_DatabaseCasingStage]', N'PK_Database', N'OBJECT';
        COMMIT TRANSACTION;
        PRINT 'Renamed deploy.PK_DATABASE -> PK_Database';
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        DECLARE @pkDbErr NVARCHAR(2048) = ERROR_MESSAGE();
        RAISERROR('Failed to rename deploy.PK_DATABASE -> PK_Database: %s', 16, 1, @pkDbErr);
    END CATCH
END
ELSE IF EXISTS (SELECT 1
                FROM sys.key_constraints k
                JOIN sys.schemas s ON s.schema_id = k.schema_id
                WHERE s.name = 'deploy'
                  AND k.name COLLATE Latin1_General_BIN2 = N'PK_DatabaseCasingStage' COLLATE Latin1_General_BIN2)
BEGIN
    EXEC sp_rename N'[deploy].[PK_DatabaseCasingStage]', N'PK_Database', N'OBJECT';
    PRINT 'Completed a stranded rename: PK_DatabaseCasingStage -> PK_Database';
END
GO

-- ---------- PK_SERVER -> PK_Server ----------
IF EXISTS (SELECT 1
           FROM sys.key_constraints k
           JOIN sys.schemas s ON s.schema_id = k.schema_id
           WHERE s.name = 'deploy'
             AND k.name COLLATE Latin1_General_BIN2 = N'PK_SERVER' COLLATE Latin1_General_BIN2)
BEGIN
    BEGIN TRY
        BEGIN TRANSACTION;
        EXEC sp_rename N'[deploy].[PK_SERVER]', N'PK_ServerCasingStage', N'OBJECT';
        EXEC sp_rename N'[deploy].[PK_ServerCasingStage]', N'PK_Server', N'OBJECT';
        COMMIT TRANSACTION;
        PRINT 'Renamed deploy.PK_SERVER -> PK_Server';
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        DECLARE @pkSvErr NVARCHAR(2048) = ERROR_MESSAGE();
        RAISERROR('Failed to rename deploy.PK_SERVER -> PK_Server: %s', 16, 1, @pkSvErr);
    END CATCH
END
ELSE IF EXISTS (SELECT 1
                FROM sys.key_constraints k
                JOIN sys.schemas s ON s.schema_id = k.schema_id
                WHERE s.name = 'deploy'
                  AND k.name COLLATE Latin1_General_BIN2 = N'PK_ServerCasingStage' COLLATE Latin1_General_BIN2)
BEGIN
    EXEC sp_rename N'[deploy].[PK_ServerCasingStage]', N'PK_Server', N'OBJECT';
    PRINT 'Completed a stranded rename: PK_ServerCasingStage -> PK_Server';
END
GO
