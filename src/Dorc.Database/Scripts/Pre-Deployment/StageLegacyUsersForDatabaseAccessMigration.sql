/*
Pre-Deployment Script: Stage legacy dbo.USERS, dbo.PERMISSION, and dbo.ENVIRONMENT_USER_MAP rows
into dbo.*_MIGRATION_STAGING tables, then drop the legacy tables, helper stored procedure, and
the retired per-database deploy.DatabaseAccessExclusion table.

The publish profile does not set DropObjectsNotInSource=True, so retired objects must be dropped
explicitly. Each table is guarded independently so an interrupted publish can be resumed safely.
*/

-- ---------- dbo.USERS -> dbo.USERS_MIGRATION_STAGING ----------
IF OBJECT_ID('dbo.USERS', 'U') IS NOT NULL
   AND OBJECT_ID('dbo.USERS_MIGRATION_STAGING', 'U') IS NULL
BEGIN
    SELECT [User_ID], [Login_ID], [Display_Name], [Team], [Login_Type], [LAN_ID], [LAN_ID_Type]
    INTO [dbo].[USERS_MIGRATION_STAGING]
    FROM [dbo].[USERS];
    PRINT 'Staged dbo.USERS -> dbo.USERS_MIGRATION_STAGING (' + CAST(@@ROWCOUNT AS VARCHAR(20)) + ' rows)';
END
ELSE IF OBJECT_ID('dbo.USERS_MIGRATION_STAGING', 'U') IS NOT NULL
BEGIN
    PRINT 'dbo.USERS_MIGRATION_STAGING already exists - skipping re-stage';
END
GO

-- ---------- dbo.PERMISSION -> dbo.PERMISSION_MIGRATION_STAGING ----------
IF OBJECT_ID('dbo.PERMISSION', 'U') IS NOT NULL
   AND OBJECT_ID('dbo.PERMISSION_MIGRATION_STAGING', 'U') IS NULL
BEGIN
    SELECT [Permission_ID], [Permission_Name], [Display_Name]
    INTO [dbo].[PERMISSION_MIGRATION_STAGING]
    FROM [dbo].[PERMISSION];
    PRINT 'Staged dbo.PERMISSION -> dbo.PERMISSION_MIGRATION_STAGING (' + CAST(@@ROWCOUNT AS VARCHAR(20)) + ' rows)';
END
ELSE IF OBJECT_ID('dbo.PERMISSION_MIGRATION_STAGING', 'U') IS NOT NULL
BEGIN
    PRINT 'dbo.PERMISSION_MIGRATION_STAGING already exists - skipping re-stage';
END
GO

-- ---------- dbo.ENVIRONMENT_USER_MAP -> dbo.ENVIRONMENT_USER_MAP_MIGRATION_STAGING ----------
IF OBJECT_ID('dbo.ENVIRONMENT_USER_MAP', 'U') IS NOT NULL
   AND OBJECT_ID('dbo.ENVIRONMENT_USER_MAP_MIGRATION_STAGING', 'U') IS NULL
BEGIN
    SELECT [DB_ID], [User_ID], [Permission_ID]
    INTO [dbo].[ENVIRONMENT_USER_MAP_MIGRATION_STAGING]
    FROM [dbo].[ENVIRONMENT_USER_MAP];
    PRINT 'Staged dbo.ENVIRONMENT_USER_MAP -> dbo.ENVIRONMENT_USER_MAP_MIGRATION_STAGING (' + CAST(@@ROWCOUNT AS VARCHAR(20)) + ' rows)';
END
ELSE IF OBJECT_ID('dbo.ENVIRONMENT_USER_MAP_MIGRATION_STAGING', 'U') IS NOT NULL
BEGIN
    PRINT 'dbo.ENVIRONMENT_USER_MAP_MIGRATION_STAGING already exists - skipping re-stage';
END
GO

-- ---------- Empty legacy tables in FK order so DacFx schema phase succeeds ----------
-- Do NOT drop the tables in pre-deployment: when upgrading an estate from dbo.DATABASE to
-- deploy.Database, DacFx plans an ALTER TABLE [dbo].[ENVIRONMENT_USER_MAP] DROP CONSTRAINT
-- step in the schema phase before post-deployment runs. Emptying them here satisfies both
-- BlockOnPossibleDataLoss=True (if DropObjectsNotInSource=True) and FK drops during rebuilds;
-- MigrateStagedLegacyUsersToDatabaseAccess.sql drops the retired objects in post-deployment.
IF OBJECT_ID('deploy.DatabaseAccessExclusion', 'U') IS NOT NULL
BEGIN
    DELETE FROM [deploy].[DatabaseAccessExclusion];
    PRINT 'Emptied retired table deploy.DatabaseAccessExclusion';
END
GO

IF OBJECT_ID('deploy.DatabaseAccessMigrationIssue', 'U') IS NOT NULL
BEGIN
    DELETE FROM [deploy].[DatabaseAccessMigrationIssue];
    PRINT 'Emptied retired table deploy.DatabaseAccessMigrationIssue';
END
GO

IF OBJECT_ID('deploy.DatabaseAccessDatabaseMembership', 'U') IS NOT NULL
    DELETE FROM [deploy].[DatabaseAccessDatabaseMembership];
IF OBJECT_ID('deploy.DatabaseAccessDatabasePrincipal', 'U') IS NOT NULL
    DELETE FROM [deploy].[DatabaseAccessDatabasePrincipal];
IF OBJECT_ID('deploy.DatabaseAccessDatabaseRole', 'U') IS NOT NULL
    DELETE FROM [deploy].[DatabaseAccessDatabaseRole];
IF OBJECT_ID('deploy.DatabaseAccessSharedPrincipal', 'U') IS NOT NULL
    DELETE FROM [deploy].[DatabaseAccessSharedPrincipal];
IF OBJECT_ID('deploy.DatabaseAccessSharedRole', 'U') IS NOT NULL
    DELETE FROM [deploy].[DatabaseAccessSharedRole];
GO

IF OBJECT_ID('dbo.ENVIRONMENT_USER_MAP', 'U') IS NOT NULL
   AND OBJECT_ID('dbo.ENVIRONMENT_USER_MAP_MIGRATION_STAGING', 'U') IS NOT NULL
BEGIN
    DELETE FROM [dbo].[ENVIRONMENT_USER_MAP];
    PRINT 'Emptied legacy table dbo.ENVIRONMENT_USER_MAP';
END
GO

IF OBJECT_ID('dbo.USERS', 'U') IS NOT NULL
   AND OBJECT_ID('dbo.USERS_MIGRATION_STAGING', 'U') IS NOT NULL
BEGIN
    DELETE FROM [dbo].[USERS];
    PRINT 'Emptied legacy table dbo.USERS';
END
GO

IF OBJECT_ID('dbo.PERMISSION', 'U') IS NOT NULL
   AND OBJECT_ID('dbo.PERMISSION_MIGRATION_STAGING', 'U') IS NOT NULL
BEGIN
    DELETE FROM [dbo].[PERMISSION];
    PRINT 'Emptied legacy table dbo.PERMISSION';
END
GO
