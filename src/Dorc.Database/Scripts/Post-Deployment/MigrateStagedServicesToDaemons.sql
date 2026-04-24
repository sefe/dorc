/*
Post-Deployment Script: Copy rows from the dbo.*_MIGRATION_STAGING tables (populated by the
pre-deploy script StageServicesForMigration.sql) into deploy.Daemon and deploy.ServerDaemon,
preserving Id values via IDENTITY_INSERT. Drop the staging tables on successful completion.

Idempotent via:
  - Staging-table existence guard (runs only when there's something to migrate).
  - WHERE NOT EXISTS clauses on every INSERT (resume-safe).

Filters applied (data-quality edge cases - IS S-002):
  - Rows in staging with NULL Service_Name are skipped and logged (deploy.Daemon.Name is NOT NULL).
  - Map rows referencing a Server_ID that no longer exists in dbo.SERVER are skipped and logged.
  - Map rows whose daemon was skipped above are also skipped (FK_ServerDaemon_Daemon integrity).
*/

IF OBJECT_ID('dbo.SERVICE_MIGRATION_STAGING', 'U') IS NOT NULL
   AND OBJECT_ID('deploy.Daemon', 'U') IS NOT NULL
BEGIN
    DECLARE @stagedServices INT, @nullNameCount INT, @insertedDaemons INT;
    DECLARE @stagedMaps INT, @orphanServerCount INT, @orphanDaemonCount INT, @insertedMaps INT;

    SELECT @stagedServices = COUNT(*) FROM [dbo].[SERVICE_MIGRATION_STAGING];
    SELECT @nullNameCount  = COUNT(*) FROM [dbo].[SERVICE_MIGRATION_STAGING] WHERE Service_Name IS NULL;

    PRINT 'Daemon migration: ' + CAST(@stagedServices AS VARCHAR(20)) + ' rows staged';

    IF @nullNameCount > 0
    BEGIN
        PRINT 'Daemon migration: skipping ' + CAST(@nullNameCount AS VARCHAR(20))
            + ' rows with NULL Service_Name (deploy.Daemon.Name is NOT NULL)';
        DECLARE @nullIds NVARCHAR(MAX);
        SELECT @nullIds = STRING_AGG(CAST(Service_ID AS VARCHAR(20)), ', ')
        FROM [dbo].[SERVICE_MIGRATION_STAGING] WHERE Service_Name IS NULL;
        PRINT 'Daemon migration: skipped Service_IDs: ' + ISNULL(@nullIds, '');
    END

    SET IDENTITY_INSERT [deploy].[Daemon] ON;

    INSERT INTO [deploy].[Daemon] (Id, Name, DisplayName, AccountName, Type)
    SELECT s.Service_ID, s.Service_Name, s.Display_Name, s.Account_Name, s.Service_Type
    FROM [dbo].[SERVICE_MIGRATION_STAGING] s
    WHERE s.Service_Name IS NOT NULL
      AND NOT EXISTS (SELECT 1 FROM [deploy].[Daemon] d WHERE d.Id = s.Service_ID);

    SET @insertedDaemons = @@ROWCOUNT;

    SET IDENTITY_INSERT [deploy].[Daemon] OFF;

    PRINT 'Daemon migration: inserted ' + CAST(@insertedDaemons AS VARCHAR(20)) + ' rows into deploy.Daemon';

    -- Server-daemon mapping migration
    SELECT @stagedMaps = COUNT(*) FROM [dbo].[SERVER_SERVICE_MAP_MIGRATION_STAGING];

    SELECT @orphanServerCount = COUNT(*)
    FROM [dbo].[SERVER_SERVICE_MAP_MIGRATION_STAGING] m
    WHERE NOT EXISTS (SELECT 1 FROM [dbo].[SERVER] sv WHERE sv.Server_ID = m.Server_ID);

    SELECT @orphanDaemonCount = COUNT(*)
    FROM [dbo].[SERVER_SERVICE_MAP_MIGRATION_STAGING] m
    WHERE EXISTS (SELECT 1 FROM [dbo].[SERVER] sv WHERE sv.Server_ID = m.Server_ID)
      AND NOT EXISTS (SELECT 1 FROM [deploy].[Daemon] d WHERE d.Id = m.Service_ID);

    PRINT 'ServerDaemon migration: ' + CAST(@stagedMaps AS VARCHAR(20)) + ' map rows staged';

    IF @orphanServerCount > 0
    BEGIN
        PRINT 'ServerDaemon migration: skipping ' + CAST(@orphanServerCount AS VARCHAR(20))
            + ' rows referencing deleted Server_ID values (would violate FK_ServerDaemon_Server)';
    END

    IF @orphanDaemonCount > 0
    BEGIN
        PRINT 'ServerDaemon migration: skipping ' + CAST(@orphanDaemonCount AS VARCHAR(20))
            + ' rows whose daemon was skipped (NULL Service_Name above)';
    END

    INSERT INTO [deploy].[ServerDaemon] (ServerId, DaemonId)
    SELECT m.Server_ID, m.Service_ID
    FROM [dbo].[SERVER_SERVICE_MAP_MIGRATION_STAGING] m
    WHERE EXISTS (SELECT 1 FROM [dbo].[SERVER] sv WHERE sv.Server_ID = m.Server_ID)
      AND EXISTS (SELECT 1 FROM [deploy].[Daemon] d WHERE d.Id = m.Service_ID)
      AND NOT EXISTS (SELECT 1 FROM [deploy].[ServerDaemon] sd
                      WHERE sd.ServerId = m.Server_ID AND sd.DaemonId = m.Service_ID);

    SET @insertedMaps = @@ROWCOUNT;

    PRINT 'ServerDaemon migration: inserted ' + CAST(@insertedMaps AS VARCHAR(20)) + ' rows into deploy.ServerDaemon';

    -- Clean up the staging tables on successful completion
    DROP TABLE [dbo].[SERVER_SERVICE_MAP_MIGRATION_STAGING];
    DROP TABLE [dbo].[SERVICE_MIGRATION_STAGING];

    PRINT 'Daemon migration: staging tables dropped; migration complete';
END
ELSE
BEGIN
    PRINT 'Daemon migration: staging tables not present or deploy.Daemon missing - skipping';
END
GO
