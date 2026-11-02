/*
Pre-Deployment Script: stage the per-database deploy.DatabaseAccessPrincipal / Role / Membership rows (keyed by
name, duplicated per database) and empty those tables. MigrateStagedDatabaseAccess.sql (post-deployment) loads the
staged rows into the shared principal/role catalogues and per-database link tables, then drops the old tables.

Emptying the old tables means the schema phase can drop them under BlockOnPossibleDataLoss=True if the publish
profile drops objects not in source. Every step is guarded so a re-run after an interrupted publish is safe.
*/
IF OBJECT_ID('deploy.DatabaseAccessPrincipal', 'U') IS NOT NULL
   AND OBJECT_ID('deploy.DatabaseAccessPrincipal_NORMALISATION_STAGING', 'U') IS NULL
BEGIN
    SELECT [DatabaseId], [Name], [Kind], [LoginName], [DirectoryId], [Present], [LegacyUserId]
    INTO [deploy].[DatabaseAccessPrincipal_NORMALISATION_STAGING]
    FROM [deploy].[DatabaseAccessPrincipal];
    PRINT 'Staged deploy.DatabaseAccessPrincipal (' + CAST(@@ROWCOUNT AS VARCHAR(20)) + ' rows)';
END
GO

IF OBJECT_ID('deploy.DatabaseAccessRole', 'U') IS NOT NULL
   AND OBJECT_ID('deploy.DatabaseAccessRole_NORMALISATION_STAGING', 'U') IS NULL
BEGIN
    SELECT [DatabaseId], [Name], [Managed], [Present]
    INTO [deploy].[DatabaseAccessRole_NORMALISATION_STAGING]
    FROM [deploy].[DatabaseAccessRole];
    PRINT 'Staged deploy.DatabaseAccessRole (' + CAST(@@ROWCOUNT AS VARCHAR(20)) + ' rows)';
END
GO

IF OBJECT_ID('deploy.DatabaseAccessMembership', 'U') IS NOT NULL
   AND OBJECT_ID('deploy.DatabaseAccessMembership_NORMALISATION_STAGING', 'U') IS NULL
BEGIN
    SELECT [DatabaseId], [Principal], [Role], [Present]
    INTO [deploy].[DatabaseAccessMembership_NORMALISATION_STAGING]
    FROM [deploy].[DatabaseAccessMembership];
    PRINT 'Staged deploy.DatabaseAccessMembership (' + CAST(@@ROWCOUNT AS VARCHAR(20)) + ' rows)';
END
GO

-- Empty child before parents (FK order) only once every table has been staged.
IF OBJECT_ID('deploy.DatabaseAccessMembership_NORMALISATION_STAGING', 'U') IS NOT NULL
   AND OBJECT_ID('deploy.DatabaseAccessPrincipal_NORMALISATION_STAGING', 'U') IS NOT NULL
   AND OBJECT_ID('deploy.DatabaseAccessRole_NORMALISATION_STAGING', 'U') IS NOT NULL
BEGIN
    IF OBJECT_ID('deploy.DatabaseAccessMembership', 'U') IS NOT NULL DELETE FROM [deploy].[DatabaseAccessMembership];
    IF OBJECT_ID('deploy.DatabaseAccessPrincipal', 'U') IS NOT NULL DELETE FROM [deploy].[DatabaseAccessPrincipal];
    IF OBJECT_ID('deploy.DatabaseAccessRole', 'U') IS NOT NULL DELETE FROM [deploy].[DatabaseAccessRole];
END
GO
