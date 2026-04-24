/*
Pre-Deployment Script: Stage dbo.SERVICE and dbo.SERVER_SERVICE_MAP rows into dbo.*_MIGRATION_STAGING
tables, then empty the legacy tables so SSDT's schema phase can drop them cleanly.

Each legacy table is guarded independently so a partial state (only one of the two still
present, left over from an interrupted prior publish) does not break the script. Staging
tables are also independently guarded — each is populated iff its source still exists and
its staging copy has not yet been made.
*/

-- ---------- dbo.SERVICE → dbo.SERVICE_MIGRATION_STAGING ----------
IF OBJECT_ID('dbo.SERVICE', 'U') IS NOT NULL
   AND OBJECT_ID('dbo.SERVICE_MIGRATION_STAGING', 'U') IS NULL
BEGIN
    SELECT Service_ID, Service_Name, Display_Name, Account_Name, Service_Type
    INTO [dbo].[SERVICE_MIGRATION_STAGING]
    FROM [dbo].[SERVICE];
    PRINT 'Staged dbo.SERVICE -> dbo.SERVICE_MIGRATION_STAGING (' + CAST(@@ROWCOUNT AS VARCHAR(20)) + ' rows)';
END
ELSE IF OBJECT_ID('dbo.SERVICE_MIGRATION_STAGING', 'U') IS NOT NULL
BEGIN
    PRINT 'dbo.SERVICE_MIGRATION_STAGING already exists - skipping re-stage';
END
GO

-- ---------- dbo.SERVER_SERVICE_MAP → dbo.SERVER_SERVICE_MAP_MIGRATION_STAGING ----------
IF OBJECT_ID('dbo.SERVER_SERVICE_MAP', 'U') IS NOT NULL
   AND OBJECT_ID('dbo.SERVER_SERVICE_MAP_MIGRATION_STAGING', 'U') IS NULL
BEGIN
    SELECT Server_ID, Service_ID
    INTO [dbo].[SERVER_SERVICE_MAP_MIGRATION_STAGING]
    FROM [dbo].[SERVER_SERVICE_MAP];
    PRINT 'Staged dbo.SERVER_SERVICE_MAP -> dbo.SERVER_SERVICE_MAP_MIGRATION_STAGING (' + CAST(@@ROWCOUNT AS VARCHAR(20)) + ' rows)';
END
ELSE IF OBJECT_ID('dbo.SERVER_SERVICE_MAP_MIGRATION_STAGING', 'U') IS NOT NULL
BEGIN
    PRINT 'dbo.SERVER_SERVICE_MAP_MIGRATION_STAGING already exists - skipping re-stage';
END
GO

-- ---------- Empty legacy tables so SSDT's schema phase can drop them ----------
-- Order: map rows (children) before service rows (parents) per the legacy FK.
IF OBJECT_ID('dbo.SERVER_SERVICE_MAP', 'U') IS NOT NULL
BEGIN
    DELETE FROM [dbo].[SERVER_SERVICE_MAP];
    PRINT 'Emptied dbo.SERVER_SERVICE_MAP (' + CAST(@@ROWCOUNT AS VARCHAR(20)) + ' rows deleted)';
END
GO

IF OBJECT_ID('dbo.SERVICE', 'U') IS NOT NULL
BEGIN
    DELETE FROM [dbo].[SERVICE];
    PRINT 'Emptied dbo.SERVICE (' + CAST(@@ROWCOUNT AS VARCHAR(20)) + ' rows deleted)';
END
GO

IF OBJECT_ID('dbo.SERVICE', 'U') IS NULL AND OBJECT_ID('dbo.SERVER_SERVICE_MAP', 'U') IS NULL
BEGIN
    PRINT 'Legacy dbo.SERVICE / dbo.SERVER_SERVICE_MAP already gone - nothing to stage';
END
GO
