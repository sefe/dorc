/*
Pre-Deployment Script: Stage dbo.SERVICE and dbo.SERVER_SERVICE_MAP rows into dbo.*_MIGRATION_STAGING
tables, then empty the legacy tables so SSDT's schema phase can drop them cleanly.

Idempotent. Guarded at three levels:
  1. Outer guard: legacy table still exists (false on re-publish after successful migration).
  2. Inner guards: staging tables do not yet exist (prevents re-staging on resume after mid-publish failure).
  3. Post-deploy's own NOT EXISTS clauses (see MigrateStagedServicesToDaemons.sql).
*/

IF OBJECT_ID('dbo.SERVICE', 'U') IS NOT NULL
BEGIN
    DECLARE @serviceCount INT, @mapCount INT;

    IF OBJECT_ID('dbo.SERVICE_MIGRATION_STAGING', 'U') IS NULL
    BEGIN
        SELECT Service_ID, Service_Name, Display_Name, Account_Name, Service_Type
        INTO [dbo].[SERVICE_MIGRATION_STAGING]
        FROM [dbo].[SERVICE];

        SET @serviceCount = @@ROWCOUNT;
        PRINT 'Staged dbo.SERVICE -> dbo.SERVICE_MIGRATION_STAGING (' + CAST(@serviceCount AS VARCHAR(20)) + ' rows)';
    END
    ELSE
    BEGIN
        PRINT 'dbo.SERVICE_MIGRATION_STAGING already exists - skipping re-stage';
    END

    IF OBJECT_ID('dbo.SERVER_SERVICE_MAP_MIGRATION_STAGING', 'U') IS NULL
    BEGIN
        SELECT Server_ID, Service_ID
        INTO [dbo].[SERVER_SERVICE_MAP_MIGRATION_STAGING]
        FROM [dbo].[SERVER_SERVICE_MAP];

        SET @mapCount = @@ROWCOUNT;
        PRINT 'Staged dbo.SERVER_SERVICE_MAP -> dbo.SERVER_SERVICE_MAP_MIGRATION_STAGING (' + CAST(@mapCount AS VARCHAR(20)) + ' rows)';
    END
    ELSE
    BEGIN
        PRINT 'dbo.SERVER_SERVICE_MAP_MIGRATION_STAGING already exists - skipping re-stage';
    END

    -- Empty the legacy tables so SSDT's schema phase can drop them without BlockOnPossibleDataLoss errors.
    -- Order matters: delete map rows (children) before service rows (parents) to respect the FK.
    DELETE FROM [dbo].[SERVER_SERVICE_MAP];
    PRINT 'Emptied dbo.SERVER_SERVICE_MAP (' + CAST(@@ROWCOUNT AS VARCHAR(20)) + ' rows deleted)';

    DELETE FROM [dbo].[SERVICE];
    PRINT 'Emptied dbo.SERVICE (' + CAST(@@ROWCOUNT AS VARCHAR(20)) + ' rows deleted)';
END
ELSE
BEGIN
    PRINT 'dbo.SERVICE does not exist - skipping daemon migration staging';
END
GO
