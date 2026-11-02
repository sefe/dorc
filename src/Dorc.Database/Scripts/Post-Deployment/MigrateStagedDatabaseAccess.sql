/*
Post-Deployment Script: load rows staged by StageDatabaseAccessForNormalisation.sql into the shared principal/role
catalogues (one row per identity / role name) and the per-database link tables, then drop the old per-database
tables and the staging tables. No-op once the staging tables are gone.
*/
IF OBJECT_ID('deploy.DatabaseAccessPrincipal_NORMALISATION_STAGING', 'U') IS NOT NULL
   AND OBJECT_ID('deploy.DatabaseAccessRole_NORMALISATION_STAGING', 'U') IS NOT NULL
   AND OBJECT_ID('deploy.DatabaseAccessMembership_NORMALISATION_STAGING', 'U') IS NOT NULL
BEGIN
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    INSERT [deploy].[DatabaseAccessSharedPrincipal] ([Name], [Kind], [LoginName], [DirectoryId])
    SELECT DISTINCT s.[Name], s.[Kind], s.[LoginName], s.[DirectoryId]
    FROM [deploy].[DatabaseAccessPrincipal_NORMALISATION_STAGING] s
    WHERE NOT EXISTS (SELECT 1 FROM [deploy].[DatabaseAccessSharedPrincipal] p
        WHERE p.[Kind] = s.[Kind] AND p.[Name] = s.[Name]
          AND EXISTS (SELECT p.[LoginName], p.[DirectoryId] INTERSECT SELECT s.[LoginName], s.[DirectoryId]));

    INSERT [deploy].[DatabaseAccessSharedRole] ([Name])
    SELECT DISTINCT s.[Name]
    FROM [deploy].[DatabaseAccessRole_NORMALISATION_STAGING] s
    WHERE NOT EXISTS (SELECT 1 FROM [deploy].[DatabaseAccessSharedRole] r WHERE r.[Name] = s.[Name]);

    INSERT [deploy].[DatabaseAccessDatabasePrincipal] ([DatabaseId], [PrincipalId], [Present], [LegacyUserId])
    SELECT s.[DatabaseId], p.[Id], s.[Present], s.[LegacyUserId]
    FROM [deploy].[DatabaseAccessPrincipal_NORMALISATION_STAGING] s
    JOIN [deploy].[DatabaseAccessSharedPrincipal] p ON p.[Kind] = s.[Kind] AND p.[Name] = s.[Name]
        AND EXISTS (SELECT p.[LoginName], p.[DirectoryId] INTERSECT SELECT s.[LoginName], s.[DirectoryId])
    WHERE NOT EXISTS (SELECT 1 FROM [deploy].[DatabaseAccessDatabasePrincipal] d
        WHERE d.[DatabaseId] = s.[DatabaseId] AND d.[PrincipalId] = p.[Id]);

    INSERT [deploy].[DatabaseAccessDatabaseRole] ([DatabaseId], [RoleId], [Managed], [Present])
    SELECT s.[DatabaseId], r.[Id], s.[Managed], s.[Present]
    FROM [deploy].[DatabaseAccessRole_NORMALISATION_STAGING] s
    JOIN [deploy].[DatabaseAccessSharedRole] r ON r.[Name] = s.[Name]
    WHERE NOT EXISTS (SELECT 1 FROM [deploy].[DatabaseAccessDatabaseRole] d
        WHERE d.[DatabaseId] = s.[DatabaseId] AND d.[RoleId] = r.[Id]);

    INSERT [deploy].[DatabaseAccessDatabaseMembership] ([DatabaseId], [PrincipalId], [RoleId], [Present])
    SELECT s.[DatabaseId], dp.[PrincipalId], dr.[RoleId], s.[Present]
    FROM [deploy].[DatabaseAccessMembership_NORMALISATION_STAGING] s
    JOIN [deploy].[DatabaseAccessDatabasePrincipal] dp ON dp.[DatabaseId] = s.[DatabaseId]
    JOIN [deploy].[DatabaseAccessSharedPrincipal] p ON p.[Id] = dp.[PrincipalId] AND p.[Name] = s.[Principal]
    JOIN [deploy].[DatabaseAccessDatabaseRole] dr ON dr.[DatabaseId] = s.[DatabaseId]
    JOIN [deploy].[DatabaseAccessSharedRole] r ON r.[Id] = dr.[RoleId] AND r.[Name] = s.[Role]
    WHERE NOT EXISTS (SELECT 1 FROM [deploy].[DatabaseAccessDatabaseMembership] d
        WHERE d.[DatabaseId] = s.[DatabaseId] AND d.[PrincipalId] = dp.[PrincipalId] AND d.[RoleId] = dr.[RoleId]);

    IF OBJECT_ID('deploy.DatabaseAccessMembership', 'U') IS NOT NULL DROP TABLE [deploy].[DatabaseAccessMembership];
    IF OBJECT_ID('deploy.DatabaseAccessPrincipal', 'U') IS NOT NULL DROP TABLE [deploy].[DatabaseAccessPrincipal];
    IF OBJECT_ID('deploy.DatabaseAccessRole', 'U') IS NOT NULL DROP TABLE [deploy].[DatabaseAccessRole];
    DROP TABLE [deploy].[DatabaseAccessMembership_NORMALISATION_STAGING];
    DROP TABLE [deploy].[DatabaseAccessPrincipal_NORMALISATION_STAGING];
    DROP TABLE [deploy].[DatabaseAccessRole_NORMALISATION_STAGING];
    COMMIT TRANSACTION;
    PRINT 'Migrated staged database access rows into the shared catalogue and per-database link tables';
END
GO
