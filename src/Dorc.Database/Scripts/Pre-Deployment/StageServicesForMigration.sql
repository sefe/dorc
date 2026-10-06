/*
Pre-Deployment Script: Stage dbo.SERVICE and dbo.SERVER_SERVICE_MAP rows into dbo.*_MIGRATION_STAGING
tables, then drop the legacy tables.

The publish profile in use does not set DropObjectsNotInSource=True, so SSDT's schema phase
will not drop tables that are absent from the project — they have to be dropped explicitly.

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

-- ---------- Drop legacy tables ----------
-- Order: map (child) before service (parent) per the legacy FK on Service_ID.
-- DROP TABLE removes outgoing FK constraints automatically. Any FK from another
-- table targeting these will surface as a loud error, which is the desired signal.
IF OBJECT_ID('dbo.SERVER_SERVICE_MAP', 'U') IS NOT NULL
BEGIN
    DROP TABLE [dbo].[SERVER_SERVICE_MAP];
    PRINT 'Dropped legacy table dbo.SERVER_SERVICE_MAP';
END
GO

IF OBJECT_ID('dbo.SERVICE', 'U') IS NOT NULL
BEGIN
    DROP TABLE [dbo].[SERVICE];
    PRINT 'Dropped legacy table dbo.SERVICE';
END
GO

IF OBJECT_ID('dbo.SERVICE', 'U') IS NULL AND OBJECT_ID('dbo.SERVER_SERVICE_MAP', 'U') IS NULL
BEGIN
    PRINT 'Legacy dbo.SERVICE / dbo.SERVER_SERVICE_MAP already gone - nothing to stage';
END
GO
