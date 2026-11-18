/*
Post-Deployment Script: Perform global deployment migration from staged legacy
dbo.USERS_MIGRATION_STAGING, dbo.PERMISSION_MIGRATION_STAGING, and
dbo.ENVIRONMENT_USER_MAP_MIGRATION_STAGING into deploy.DatabaseAccess*, resolving
both Native SQL logins and Windows Active Directory / SAM SIDs via SUSER_SID, and
emitting a plain-text migration report to the SQL / dacpac deployment log for any
anomalies or privileged memberships requiring human review.
*/
IF OBJECT_ID('dbo.USERS_MIGRATION_STAGING', 'U') IS NOT NULL
   OR OBJECT_ID('dbo.PERMISSION_MIGRATION_STAGING', 'U') IS NOT NULL
   OR OBJECT_ID('dbo.ENVIRONMENT_USER_MAP_MIGRATION_STAGING', 'U') IS NOT NULL
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    DECLARE @now DATETIME2 = SYSUTCDATETIME();
    DECLARE @stagedUserCount INT = 0;
    DECLARE @stagedPermissionCount INT = 0;

    IF OBJECT_ID('tempdb..#StagedUsers') IS NOT NULL DROP TABLE #StagedUsers;
    CREATE TABLE #StagedUsers (
        [User_ID] INT NOT NULL PRIMARY KEY,
        [Login_ID] NVARCHAR(256) NULL,
        [Display_Name] NVARCHAR(256) NULL,
        [Team] NVARCHAR(256) NULL,
        [Login_Type] NVARCHAR(64) NULL,
        [LAN_ID] NVARCHAR(256) NULL,
        [LAN_ID_Type] NVARCHAR(64) NULL
    );

    IF OBJECT_ID('dbo.USERS_MIGRATION_STAGING', 'U') IS NOT NULL
    BEGIN
        INSERT INTO #StagedUsers ([User_ID], [Login_ID], [Display_Name], [Team], [Login_Type], [LAN_ID], [LAN_ID_Type])
        SELECT [User_ID], [Login_ID], [Display_Name], [Team], [Login_Type], [LAN_ID], [LAN_ID_Type]
        FROM [dbo].[USERS_MIGRATION_STAGING];
        SET @stagedUserCount = @@ROWCOUNT;
    END

    IF OBJECT_ID('tempdb..#StagedPermissions') IS NOT NULL DROP TABLE #StagedPermissions;
    CREATE TABLE #StagedPermissions (
        [Permission_ID] INT NOT NULL PRIMARY KEY,
        [Permission_Name] NVARCHAR(256) NULL,
        [Display_Name] NVARCHAR(256) NULL
    );

    IF OBJECT_ID('dbo.PERMISSION_MIGRATION_STAGING', 'U') IS NOT NULL
    BEGIN
        INSERT INTO #StagedPermissions ([Permission_ID], [Permission_Name], [Display_Name])
        SELECT [Permission_ID], [Permission_Name], [Display_Name]
        FROM [dbo].[PERMISSION_MIGRATION_STAGING];
        SET @stagedPermissionCount = @@ROWCOUNT;
    END

    IF OBJECT_ID('tempdb..#EvaluatedAssignments') IS NOT NULL DROP TABLE #EvaluatedAssignments;
    CREATE TABLE #EvaluatedAssignments (
        [RowId] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [LegacyDatabaseId] INT NULL,
        [DatabaseId] INT NULL,
        [ServerName] NVARCHAR(256) NULL,
        [DatabaseName] NVARCHAR(256) NULL,
        [DatabaseType] NVARCHAR(32) NULL,
        [EnvironmentNames] NVARCHAR(1024) NULL,
        [LegacyUserId] INT NULL,
        [LegacyLoginName] NVARCHAR(256) NULL,
        [LegacyDisplayName] NVARCHAR(256) NULL,
        [LegacyLoginType] NVARCHAR(64) NULL,
        [LegacyLanId] NVARCHAR(256) NULL,
        [LegacyLanIdType] NVARCHAR(64) NULL,
        [LegacyPermissionId] INT NULL,
        [LegacyPermissionName] NVARCHAR(256) NULL,
        [EffectiveLogin] NVARCHAR(256) NULL,
        [NormalizedLoginType] NVARCHAR(64) NULL,
        [NormalizedLanIdType] NVARCHAR(64) NULL,
        [PrincipalKind] NVARCHAR(32) NULL,
        [ResolvedDirectoryId] NVARCHAR(256) NULL,
        [EffectiveRole] NVARCHAR(256) NULL,
        [IssueCode] NVARCHAR(64) NULL,
        [Detail] NVARCHAR(1024) NULL
    );

    IF OBJECT_ID('dbo.ENVIRONMENT_USER_MAP_MIGRATION_STAGING', 'U') IS NOT NULL
    BEGIN
        ;WITH DistinctMappings AS (
            SELECT DISTINCT [DB_ID], [User_ID], [Permission_ID]
            FROM [dbo].[ENVIRONMENT_USER_MAP_MIGRATION_STAGING]
        )
        INSERT INTO #EvaluatedAssignments (
            [LegacyDatabaseId], [DatabaseId], [ServerName], [DatabaseName], [DatabaseType], [EnvironmentNames],
            [LegacyUserId], [LegacyLoginName], [LegacyDisplayName], [LegacyLoginType], [LegacyLanId], [LegacyLanIdType],
            [LegacyPermissionId], [LegacyPermissionName],
            [EffectiveLogin], [NormalizedLoginType], [NormalizedLanIdType], [EffectiveRole]
        )
        SELECT
            m.[DB_ID],
            d.[Id],
            d.[ServerName],
            d.[Name],
            d.[DatabaseType],
            LEFT(ISNULL(STUFF((
                SELECT N', ' + env.[Name]
                FROM [deploy].[EnvironmentDatabase] ed
                JOIN [deploy].[Environment] env ON env.[Id] = ed.[EnvId]
                WHERE ed.[DbId] = d.[Id]
                ORDER BY env.[Name]
                FOR XML PATH(N''), TYPE
            ).value(N'.', N'NVARCHAR(MAX)'), 1, 2, N''), N'(unattached)'), 1024),
            m.[User_ID],
            NULLIF(LTRIM(RTRIM(u.[Login_ID])), N''),
            NULLIF(LTRIM(RTRIM(u.[Display_Name])), N''),
            NULLIF(LTRIM(RTRIM(u.[Login_Type])), N''),
            NULLIF(LTRIM(RTRIM(u.[LAN_ID])), N''),
            NULLIF(LTRIM(RTRIM(u.[LAN_ID_Type])), N''),
            m.[Permission_ID],
            NULLIF(LTRIM(RTRIM(p.[Permission_Name])), N''),
            COALESCE(NULLIF(LTRIM(RTRIM(u.[Login_ID])), N''), NULLIF(LTRIM(RTRIM(u.[LAN_ID])), N'')),
            LOWER(ISNULL(LTRIM(RTRIM(u.[Login_Type])), N'')),
            LOWER(ISNULL(LTRIM(RTRIM(u.[LAN_ID_Type])), N'')),
            NULLIF(LTRIM(RTRIM(p.[Permission_Name])), N'')
        FROM DistinctMappings m
        LEFT JOIN [deploy].[Database] d ON d.[Id] = m.[DB_ID]
        LEFT JOIN #StagedUsers u ON u.[User_ID] = m.[User_ID]
        LEFT JOIN #StagedPermissions p ON p.[Permission_ID] = m.[Permission_ID];

        -- 1. Orphaned database, user, or permission references
        UPDATE e
        SET [IssueCode] = N'OrphanedAssignment',
            [Detail] = CONCAT(
                N'Legacy mapping (DB_ID=', COALESCE(CAST(e.[LegacyDatabaseId] AS NVARCHAR(20)), N'NULL'),
                N', User_ID=', COALESCE(CAST(e.[LegacyUserId] AS NVARCHAR(20)), N'NULL'),
                N', Permission_ID=', COALESCE(CAST(e.[LegacyPermissionId] AS NVARCHAR(20)), N'NULL'),
                N') references a missing database, user, or permission record.')
        FROM #EvaluatedAssignments e
        LEFT JOIN #StagedUsers u ON u.[User_ID] = e.[LegacyUserId]
        LEFT JOIN #StagedPermissions p ON p.[Permission_ID] = e.[LegacyPermissionId]
        WHERE e.[IssueCode] IS NULL
          AND (e.[DatabaseId] IS NULL OR u.[User_ID] IS NULL OR p.[Permission_ID] IS NULL);

        -- 2. Non-SQL Server databases
        UPDATE #EvaluatedAssignments
        SET [IssueCode] = N'UnsupportedProvider',
            [Detail] = CONCAT(N'Database uses engine ''', [DatabaseType], N''', which is not managed by SQL Server database role reconciliation.')
        WHERE [IssueCode] IS NULL
          AND [DatabaseType] <> N'SqlServer';

        -- 3. Invalid or protected roles (excluding db_owner which has its own review code)
        UPDATE #EvaluatedAssignments
        SET [IssueCode] = N'ProtectedRoleBlocked',
            [Detail] = N'Roles public, db_securityadmin, db_accessadmin, unrecognized db_* fixed roles, and invalid SQL identifiers cannot be managed automatically.'
        WHERE [IssueCode] IS NULL
          AND (
              [EffectiveRole] IS NULL
              OR LEN([EffectiveRole]) > 128
              OR PATINDEX(N'%[' + CHAR(1) + N'-' + CHAR(31) + CHAR(127) + N']%', [EffectiveRole]) > 0
              OR CHARINDEX(NCHAR(0), [EffectiveRole] COLLATE Latin1_General_100_BIN2) > 0
              OR LOWER([EffectiveRole]) IN (N'public', N'db_securityadmin', N'db_accessadmin')
              OR (LOWER([EffectiveRole]) LIKE N'db[_]%'
                  AND LOWER([EffectiveRole]) NOT IN (
                      N'db_owner', N'db_datareader', N'db_datawriter', N'db_ddladmin',
                      N'db_backupoperator', N'db_denydatareader', N'db_denydatawriter'))
          );

        -- 4. Unsupported login types
        UPDATE #EvaluatedAssignments
        SET [IssueCode] = N'UnsupportedLoginType',
            [Detail] = CONCAT(N'Unsupported legacy Login_Type ''', COALESCE([LegacyLoginType], N'(empty)'), N'''. Grant access manually in DOrc Database Access if still required.')
        WHERE [IssueCode] IS NULL
          AND [NormalizedLoginType] NOT IN (N'sql', N'endur', N'windows');

        -- 5. Missing, protected, or invalid principal identifiers
        UPDATE #EvaluatedAssignments
        SET [IssueCode] = N'ProtectedPrincipalBlocked',
            [Detail] = N'System principals (dbo, guest, sys, INFORMATION_SCHEMA, public, sa, ##*) and invalid SQL identifiers cannot be managed automatically.'
        WHERE [IssueCode] IS NULL
          AND (
              [EffectiveLogin] IS NULL
              OR LEN([EffectiveLogin]) > 128
              OR PATINDEX(N'%[' + CHAR(1) + N'-' + CHAR(31) + CHAR(127) + N']%', [EffectiveLogin]) > 0
              OR CHARINDEX(NCHAR(0), [EffectiveLogin] COLLATE Latin1_General_100_BIN2) > 0
              OR LOWER([EffectiveLogin]) IN (N'dbo', N'guest', N'sys', N'information_schema', N'public', N'sa')
              OR LEFT([EffectiveLogin], 2) = N'##'
          );

        -- 6. Native login records where Login_ID and LAN_ID both exist and disagree
        UPDATE #EvaluatedAssignments
        SET [IssueCode] = N'ConflictingPrincipal',
            [Detail] = CONCAT(N'Legacy user has differing Login_ID (''', [LegacyLoginName], N''') and LAN_ID (''', [LegacyLanId], N'''). Grant the intended login manually.')
        WHERE [IssueCode] IS NULL
          AND [NormalizedLoginType] IN (N'sql', N'endur')
          AND [LegacyLoginName] IS NOT NULL
          AND [LegacyLanId] IS NOT NULL
          AND [LegacyLoginName] <> [LegacyLanId];

        -- 7. Assign PrincipalKind and resolve Windows SIDs via SUSER_SID
        UPDATE #EvaluatedAssignments
        SET [PrincipalKind] = N'Native'
        WHERE [IssueCode] IS NULL
          AND [NormalizedLoginType] IN (N'sql', N'endur');

        UPDATE #EvaluatedAssignments
        SET [PrincipalKind] = CASE
                WHEN [NormalizedLanIdType] = N'user' THEN N'DirectoryUser'
                WHEN [NormalizedLanIdType] = N'group' THEN N'DirectoryGroup'
                ELSE NULL
            END
        WHERE [IssueCode] IS NULL
          AND [NormalizedLoginType] = N'windows';

        UPDATE #EvaluatedAssignments
        SET [IssueCode] = N'InvalidWindowsIdentityType',
            [Detail] = CONCAT(N'Windows account has unrecognized LAN_ID_Type ''', COALESCE([LegacyLanIdType], N'(empty)'), N''' (expected User or Group). Grant access manually in DOrc Database Access.')
        WHERE [IssueCode] IS NULL
          AND [NormalizedLoginType] = N'windows'
          AND [PrincipalKind] IS NULL;

        UPDATE e
        SET [ResolvedDirectoryId] = CASE
                WHEN b.[SidBin] IS NULL OR DATALENGTH(b.[SidBin]) < 8 THEN NULL
                ELSE CONCAT(
                    N'S-',
                    CAST(CAST(SUBSTRING(b.[SidBin], 1, 1) AS TINYINT) AS NVARCHAR(10)),
                    N'-',
                    CAST(CAST(SUBSTRING(b.[SidBin], 3, 6) AS BIGINT) AS NVARCHAR(30)),
                    CASE WHEN b.[SubCount] > 0  AND DATALENGTH(b.[SidBin]) >= 12 THEN CONCAT(N'-', CAST(CAST(CAST(SUBSTRING(b.[SidBin],  9, 1) AS TINYINT) AS BIGINT) + CAST(CAST(SUBSTRING(b.[SidBin], 10, 1) AS TINYINT) AS BIGINT) * 256 + CAST(CAST(SUBSTRING(b.[SidBin], 11, 1) AS TINYINT) AS BIGINT) * 65536 + CAST(CAST(SUBSTRING(b.[SidBin], 12, 1) AS TINYINT) AS BIGINT) * 16777216 AS NVARCHAR(20))) ELSE N'' END,
                    CASE WHEN b.[SubCount] > 1  AND DATALENGTH(b.[SidBin]) >= 16 THEN CONCAT(N'-', CAST(CAST(CAST(SUBSTRING(b.[SidBin], 13, 1) AS TINYINT) AS BIGINT) + CAST(CAST(SUBSTRING(b.[SidBin], 14, 1) AS TINYINT) AS BIGINT) * 256 + CAST(CAST(SUBSTRING(b.[SidBin], 15, 1) AS TINYINT) AS BIGINT) * 65536 + CAST(CAST(SUBSTRING(b.[SidBin], 16, 1) AS TINYINT) AS BIGINT) * 16777216 AS NVARCHAR(20))) ELSE N'' END,
                    CASE WHEN b.[SubCount] > 2  AND DATALENGTH(b.[SidBin]) >= 20 THEN CONCAT(N'-', CAST(CAST(CAST(SUBSTRING(b.[SidBin], 17, 1) AS TINYINT) AS BIGINT) + CAST(CAST(SUBSTRING(b.[SidBin], 18, 1) AS TINYINT) AS BIGINT) * 256 + CAST(CAST(SUBSTRING(b.[SidBin], 19, 1) AS TINYINT) AS BIGINT) * 65536 + CAST(CAST(SUBSTRING(b.[SidBin], 20, 1) AS TINYINT) AS BIGINT) * 16777216 AS NVARCHAR(20))) ELSE N'' END,
                    CASE WHEN b.[SubCount] > 3  AND DATALENGTH(b.[SidBin]) >= 24 THEN CONCAT(N'-', CAST(CAST(CAST(SUBSTRING(b.[SidBin], 21, 1) AS TINYINT) AS BIGINT) + CAST(CAST(SUBSTRING(b.[SidBin], 22, 1) AS TINYINT) AS BIGINT) * 256 + CAST(CAST(SUBSTRING(b.[SidBin], 23, 1) AS TINYINT) AS BIGINT) * 65536 + CAST(CAST(SUBSTRING(b.[SidBin], 24, 1) AS TINYINT) AS BIGINT) * 16777216 AS NVARCHAR(20))) ELSE N'' END,
                    CASE WHEN b.[SubCount] > 4  AND DATALENGTH(b.[SidBin]) >= 28 THEN CONCAT(N'-', CAST(CAST(CAST(SUBSTRING(b.[SidBin], 25, 1) AS TINYINT) AS BIGINT) + CAST(CAST(SUBSTRING(b.[SidBin], 26, 1) AS TINYINT) AS BIGINT) * 256 + CAST(CAST(SUBSTRING(b.[SidBin], 27, 1) AS TINYINT) AS BIGINT) * 65536 + CAST(CAST(SUBSTRING(b.[SidBin], 28, 1) AS TINYINT) AS BIGINT) * 16777216 AS NVARCHAR(20))) ELSE N'' END,
                    CASE WHEN b.[SubCount] > 5  AND DATALENGTH(b.[SidBin]) >= 32 THEN CONCAT(N'-', CAST(CAST(CAST(SUBSTRING(b.[SidBin], 29, 1) AS TINYINT) AS BIGINT) + CAST(CAST(SUBSTRING(b.[SidBin], 30, 1) AS TINYINT) AS BIGINT) * 256 + CAST(CAST(SUBSTRING(b.[SidBin], 31, 1) AS TINYINT) AS BIGINT) * 65536 + CAST(CAST(SUBSTRING(b.[SidBin], 32, 1) AS TINYINT) AS BIGINT) * 16777216 AS NVARCHAR(20))) ELSE N'' END,
                    CASE WHEN b.[SubCount] > 6  AND DATALENGTH(b.[SidBin]) >= 36 THEN CONCAT(N'-', CAST(CAST(CAST(SUBSTRING(b.[SidBin], 33, 1) AS TINYINT) AS BIGINT) + CAST(CAST(SUBSTRING(b.[SidBin], 34, 1) AS TINYINT) AS BIGINT) * 256 + CAST(CAST(SUBSTRING(b.[SidBin], 35, 1) AS TINYINT) AS BIGINT) * 65536 + CAST(CAST(SUBSTRING(b.[SidBin], 36, 1) AS TINYINT) AS BIGINT) * 16777216 AS NVARCHAR(20))) ELSE N'' END,
                    CASE WHEN b.[SubCount] > 7  AND DATALENGTH(b.[SidBin]) >= 40 THEN CONCAT(N'-', CAST(CAST(CAST(SUBSTRING(b.[SidBin], 37, 1) AS TINYINT) AS BIGINT) + CAST(CAST(SUBSTRING(b.[SidBin], 38, 1) AS TINYINT) AS BIGINT) * 256 + CAST(CAST(SUBSTRING(b.[SidBin], 39, 1) AS TINYINT) AS BIGINT) * 65536 + CAST(CAST(SUBSTRING(b.[SidBin], 40, 1) AS TINYINT) AS BIGINT) * 16777216 AS NVARCHAR(20))) ELSE N'' END,
                    CASE WHEN b.[SubCount] > 8  AND DATALENGTH(b.[SidBin]) >= 44 THEN CONCAT(N'-', CAST(CAST(CAST(SUBSTRING(b.[SidBin], 41, 1) AS TINYINT) AS BIGINT) + CAST(CAST(SUBSTRING(b.[SidBin], 42, 1) AS TINYINT) AS BIGINT) * 256 + CAST(CAST(SUBSTRING(b.[SidBin], 43, 1) AS TINYINT) AS BIGINT) * 65536 + CAST(CAST(SUBSTRING(b.[SidBin], 44, 1) AS TINYINT) AS BIGINT) * 16777216 AS NVARCHAR(20))) ELSE N'' END,
                    CASE WHEN b.[SubCount] > 9  AND DATALENGTH(b.[SidBin]) >= 48 THEN CONCAT(N'-', CAST(CAST(CAST(SUBSTRING(b.[SidBin], 45, 1) AS TINYINT) AS BIGINT) + CAST(CAST(SUBSTRING(b.[SidBin], 46, 1) AS TINYINT) AS BIGINT) * 256 + CAST(CAST(SUBSTRING(b.[SidBin], 47, 1) AS TINYINT) AS BIGINT) * 65536 + CAST(CAST(SUBSTRING(b.[SidBin], 48, 1) AS TINYINT) AS BIGINT) * 16777216 AS NVARCHAR(20))) ELSE N'' END,
                    CASE WHEN b.[SubCount] > 10 AND DATALENGTH(b.[SidBin]) >= 52 THEN CONCAT(N'-', CAST(CAST(CAST(SUBSTRING(b.[SidBin], 49, 1) AS TINYINT) AS BIGINT) + CAST(CAST(SUBSTRING(b.[SidBin], 50, 1) AS TINYINT) AS BIGINT) * 256 + CAST(CAST(SUBSTRING(b.[SidBin], 51, 1) AS TINYINT) AS BIGINT) * 65536 + CAST(CAST(SUBSTRING(b.[SidBin], 52, 1) AS TINYINT) AS BIGINT) * 16777216 AS NVARCHAR(20))) ELSE N'' END,
                    CASE WHEN b.[SubCount] > 11 AND DATALENGTH(b.[SidBin]) >= 56 THEN CONCAT(N'-', CAST(CAST(CAST(SUBSTRING(b.[SidBin], 53, 1) AS TINYINT) AS BIGINT) + CAST(CAST(SUBSTRING(b.[SidBin], 54, 1) AS TINYINT) AS BIGINT) * 256 + CAST(CAST(SUBSTRING(b.[SidBin], 55, 1) AS TINYINT) AS BIGINT) * 65536 + CAST(CAST(SUBSTRING(b.[SidBin], 56, 1) AS TINYINT) AS BIGINT) * 16777216 AS NVARCHAR(20))) ELSE N'' END,
                    CASE WHEN b.[SubCount] > 12 AND DATALENGTH(b.[SidBin]) >= 60 THEN CONCAT(N'-', CAST(CAST(CAST(SUBSTRING(b.[SidBin], 57, 1) AS TINYINT) AS BIGINT) + CAST(CAST(SUBSTRING(b.[SidBin], 58, 1) AS TINYINT) AS BIGINT) * 256 + CAST(CAST(SUBSTRING(b.[SidBin], 59, 1) AS TINYINT) AS BIGINT) * 65536 + CAST(CAST(SUBSTRING(b.[SidBin], 60, 1) AS TINYINT) AS BIGINT) * 16777216 AS NVARCHAR(20))) ELSE N'' END,
                    CASE WHEN b.[SubCount] > 13 AND DATALENGTH(b.[SidBin]) >= 64 THEN CONCAT(N'-', CAST(CAST(CAST(SUBSTRING(b.[SidBin], 61, 1) AS TINYINT) AS BIGINT) + CAST(CAST(SUBSTRING(b.[SidBin], 62, 1) AS TINYINT) AS BIGINT) * 256 + CAST(CAST(SUBSTRING(b.[SidBin], 63, 1) AS TINYINT) AS BIGINT) * 65536 + CAST(CAST(SUBSTRING(b.[SidBin], 64, 1) AS TINYINT) AS BIGINT) * 16777216 AS NVARCHAR(20))) ELSE N'' END,
                    CASE WHEN b.[SubCount] > 14 AND DATALENGTH(b.[SidBin]) >= 68 THEN CONCAT(N'-', CAST(CAST(CAST(SUBSTRING(b.[SidBin], 65, 1) AS TINYINT) AS BIGINT) + CAST(CAST(SUBSTRING(b.[SidBin], 66, 1) AS TINYINT) AS BIGINT) * 256 + CAST(CAST(SUBSTRING(b.[SidBin], 67, 1) AS TINYINT) AS BIGINT) * 65536 + CAST(CAST(SUBSTRING(b.[SidBin], 68, 1) AS TINYINT) AS BIGINT) * 16777216 AS NVARCHAR(20))) ELSE N'' END
                )
            END
        FROM #EvaluatedAssignments e
        CROSS APPLY (
            SELECT
                SUSER_SID(e.[EffectiveLogin]) AS [SidBin],
                ISNULL(CAST(SUBSTRING(SUSER_SID(e.[EffectiveLogin]), 2, 1) AS TINYINT), 0) AS [SubCount]
        ) b
        WHERE e.[IssueCode] IS NULL
          AND e.[NormalizedLoginType] = N'windows';

        UPDATE #EvaluatedAssignments
        SET [IssueCode] = N'UnresolvedWindowsAccount',
            [Detail] = N'Windows account could not be resolved to an Active Directory / SAM SID via SUSER_SID. Verify the account and grant access manually in DOrc Database Access.'
        WHERE [IssueCode] IS NULL
          AND [NormalizedLoginType] = N'windows'
          AND [ResolvedDirectoryId] IS NULL;

        -- 8. db_owner assignments require explicit post-release owner/admin review & grant
        UPDATE #EvaluatedAssignments
        SET [IssueCode] = N'PrivilegeReviewRequired',
            [Detail] = N'Full database control (db_owner) is not auto-granted during deployment migration. An owner or administrator of every attached environment must review and grant db_owner or a least-privilege role in DOrc Database Access.'
        WHERE [IssueCode] IS NULL
          AND LOWER([EffectiveRole]) = N'db_owner';

        -- 9. Detect per-database conflicts among remaining candidate assignments:
        --    a) Multiple distinct LegacyUserIds resolving to the same EffectiveLogin or DirectoryId on the same database
        UPDATE e
        SET [IssueCode] = N'ConflictingPrincipal',
            [Detail] = N'Multiple legacy user records resolve to the same principal or SID on this database. Grant the intended account/role manually in DOrc Database Access.'
        FROM #EvaluatedAssignments e
        WHERE e.[IssueCode] IS NULL
          AND EXISTS (
              SELECT 1
              FROM #EvaluatedAssignments other
              WHERE other.[IssueCode] IS NULL
                AND other.[DatabaseId] = e.[DatabaseId]
                AND other.[LegacyUserId] <> e.[LegacyUserId]
                AND (
                    other.[EffectiveLogin] = e.[EffectiveLogin]
                    OR (e.[ResolvedDirectoryId] IS NOT NULL AND other.[ResolvedDirectoryId] = e.[ResolvedDirectoryId])
                )
          );

        --    b) Principal name collides with a role name on the same database (staged or existing)
        UPDATE e
        SET [IssueCode] = N'ConflictingPrincipal',
            [Detail] = N'Principal name collides with a database role of the same name on this database. Grant a non-colliding user/role manually.'
        FROM #EvaluatedAssignments e
        WHERE e.[IssueCode] IS NULL
          AND (
              EXISTS (
                  SELECT 1 FROM #EvaluatedAssignments r
                  WHERE r.[IssueCode] IS NULL
                    AND r.[DatabaseId] = e.[DatabaseId]
                    AND r.[EffectiveRole] = e.[EffectiveLogin]
              )
              OR EXISTS (
                  SELECT 1
                  FROM [deploy].[DatabaseAccessRoleBinding] dr
                  JOIN [deploy].[DatabaseAccessRoleCatalog] sr ON sr.[Id] = dr.[RoleId]
                  WHERE dr.[DatabaseId] = e.[DatabaseId] AND sr.[Name] = e.[EffectiveLogin]
              )
              OR EXISTS (
                  SELECT 1
                  FROM [deploy].[DatabaseAccessPrincipalBinding] dp
                  JOIN [deploy].[DatabaseAccessPrincipalCatalog] sp ON sp.[Id] = dp.[PrincipalId]
                  WHERE dp.[DatabaseId] = e.[DatabaseId] AND sp.[Name] = e.[EffectiveRole]
              )
          );

        --    c) Conflicts with pre-existing DatabaseAccess state on this database
        UPDATE e
        SET [IssueCode] = N'ConflictingPrincipal',
            [Detail] = N'Conflicts with pre-existing Database Access desired state on this database (marked absent or bound to a different identity).'
        FROM #EvaluatedAssignments e
        WHERE e.[IssueCode] IS NULL
          AND (
              EXISTS (
                  SELECT 1
                  FROM [deploy].[DatabaseAccessPrincipalBinding] dp
                  JOIN [deploy].[DatabaseAccessPrincipalCatalog] sp ON sp.[Id] = dp.[PrincipalId]
                  WHERE dp.[DatabaseId] = e.[DatabaseId]
                    AND (
                        (sp.[Name] = e.[EffectiveLogin]
                         AND (dp.[Present] = 0
                              OR sp.[Kind] <> e.[PrincipalKind]
                              OR (e.[PrincipalKind] = N'Native' AND ISNULL(sp.[LoginName], N'') <> e.[EffectiveLogin])
                              OR (e.[PrincipalKind] <> N'Native' AND ISNULL(sp.[DirectoryId], N'') <> ISNULL(e.[ResolvedDirectoryId], N''))))
                        OR (e.[PrincipalKind] <> N'Native'
                            AND sp.[DirectoryId] = e.[ResolvedDirectoryId]
                            AND sp.[Name] <> e.[EffectiveLogin])
                    )
              )
              OR EXISTS (
                  SELECT 1
                  FROM [deploy].[DatabaseAccessRoleBinding] dr
                  JOIN [deploy].[DatabaseAccessRoleCatalog] sr ON sr.[Id] = dr.[RoleId]
                  WHERE dr.[DatabaseId] = e.[DatabaseId]
                    AND sr.[Name] = e.[EffectiveRole]
                    AND dr.[Present] = 0
              )
              OR EXISTS (
                  SELECT 1
                  FROM [deploy].[DatabaseAccessRoleMembership] dm
                  JOIN [deploy].[DatabaseAccessPrincipalBinding] dp ON dp.[DatabaseId] = dm.[DatabaseId] AND dp.[PrincipalId] = dm.[PrincipalId]
                  JOIN [deploy].[DatabaseAccessPrincipalCatalog] sp ON sp.[Id] = dp.[PrincipalId]
                  JOIN [deploy].[DatabaseAccessRoleBinding] dr ON dr.[DatabaseId] = dm.[DatabaseId] AND dr.[RoleId] = dm.[RoleId]
                  JOIN [deploy].[DatabaseAccessRoleCatalog] sr ON sr.[Id] = dr.[RoleId]
                  WHERE dm.[DatabaseId] = e.[DatabaseId]
                    AND sp.[Name] = e.[EffectiveLogin]
                    AND sr.[Name] = e.[EffectiveRole]
                    AND dm.[Present] = 0
              )
          );

        -- Ensure DatabaseAccessConfiguration exists (and bump Revision on existing configs gaining new rows)
        UPDATE c
        SET c.[Revision] = c.[Revision] + 1
        FROM [deploy].[DatabaseAccessConfiguration] c
        WHERE EXISTS (
            SELECT 1 FROM #EvaluatedAssignments e
            WHERE e.[IssueCode] IS NULL
              AND e.[DatabaseId] = c.[DatabaseId]
              AND NOT EXISTS (
                  SELECT 1
                  FROM [deploy].[DatabaseAccessRoleMembership] dm
                  JOIN [deploy].[DatabaseAccessPrincipalCatalog] sp ON sp.[Id] = dm.[PrincipalId]
                  JOIN [deploy].[DatabaseAccessRoleCatalog] sr ON sr.[Id] = dm.[RoleId]
                  WHERE dm.[DatabaseId] = e.[DatabaseId]
                    AND sp.[Kind] = e.[PrincipalKind]
                    AND sp.[Name] = e.[EffectiveLogin]
                    AND sr.[Name] = e.[EffectiveRole]
              )
        );

        INSERT INTO [deploy].[DatabaseAccessConfiguration] ([DatabaseId], [Provider], [Revision])
        SELECT
            e.[DatabaseId],
            N'sql-server',
            CASE WHEN MAX(CASE WHEN e.[IssueCode] IS NULL THEN 1 ELSE 0 END) = 1 THEN 1 ELSE 0 END
        FROM #EvaluatedAssignments e
        WHERE e.[DatabaseId] IS NOT NULL
          AND e.[DatabaseType] = N'SqlServer'
          AND (e.[IssueCode] IS NULL OR e.[IssueCode] <> N'OrphanedAssignment')
          AND NOT EXISTS (
              SELECT 1 FROM [deploy].[DatabaseAccessConfiguration] c WHERE c.[DatabaseId] = e.[DatabaseId]
          )
        GROUP BY e.[DatabaseId];

        -- Populate principal and role catalogs for clean Native and Windows assignments
        INSERT INTO [deploy].[DatabaseAccessPrincipalCatalog] ([Name], [Kind], [LoginName], [DirectoryId])
        SELECT DISTINCT
            e.[EffectiveLogin],
            e.[PrincipalKind],
            CASE WHEN e.[PrincipalKind] = N'Native' THEN e.[EffectiveLogin] ELSE NULL END,
            e.[ResolvedDirectoryId]
        FROM #EvaluatedAssignments e
        WHERE e.[IssueCode] IS NULL
          AND NOT EXISTS (
              SELECT 1 FROM [deploy].[DatabaseAccessPrincipalCatalog] sp
              WHERE sp.[Kind] = e.[PrincipalKind]
                AND sp.[Name] = e.[EffectiveLogin]
                AND ISNULL(sp.[LoginName], N'') = CASE WHEN e.[PrincipalKind] = N'Native' THEN e.[EffectiveLogin] ELSE N'' END
                AND ISNULL(sp.[DirectoryId], N'') = ISNULL(e.[ResolvedDirectoryId], N'')
          );

        INSERT INTO [deploy].[DatabaseAccessRoleCatalog] ([Name])
        SELECT DISTINCT e.[EffectiveRole]
        FROM #EvaluatedAssignments e
        WHERE e.[IssueCode] IS NULL
          AND NOT EXISTS (
              SELECT 1 FROM [deploy].[DatabaseAccessRoleCatalog] sr WHERE sr.[Name] = e.[EffectiveRole]
          );

        -- Bind principals, roles (reference-only Managed = 0), and memberships per database
        INSERT INTO [deploy].[DatabaseAccessPrincipalBinding] ([DatabaseId], [PrincipalId], [Present], [LegacyUserId])
        SELECT DISTINCT e.[DatabaseId], sp.[Id], 1, e.[LegacyUserId]
        FROM #EvaluatedAssignments e
        JOIN [deploy].[DatabaseAccessPrincipalCatalog] sp
            ON sp.[Kind] = e.[PrincipalKind]
            AND sp.[Name] = e.[EffectiveLogin]
            AND ISNULL(sp.[LoginName], N'') = CASE WHEN e.[PrincipalKind] = N'Native' THEN e.[EffectiveLogin] ELSE N'' END
            AND ISNULL(sp.[DirectoryId], N'') = ISNULL(e.[ResolvedDirectoryId], N'')
        WHERE e.[IssueCode] IS NULL
          AND NOT EXISTS (
              SELECT 1 FROM [deploy].[DatabaseAccessPrincipalBinding] dp
              WHERE dp.[DatabaseId] = e.[DatabaseId] AND dp.[PrincipalId] = sp.[Id]
          );

        INSERT INTO [deploy].[DatabaseAccessRoleBinding] ([DatabaseId], [RoleId], [Managed], [Present])
        SELECT DISTINCT e.[DatabaseId], sr.[Id], 0, 1
        FROM #EvaluatedAssignments e
        JOIN [deploy].[DatabaseAccessRoleCatalog] sr ON sr.[Name] = e.[EffectiveRole]
        WHERE e.[IssueCode] IS NULL
          AND NOT EXISTS (
              SELECT 1 FROM [deploy].[DatabaseAccessRoleBinding] dr
              WHERE dr.[DatabaseId] = e.[DatabaseId] AND dr.[RoleId] = sr.[Id]
          );

        INSERT INTO [deploy].[DatabaseAccessRoleMembership] ([DatabaseId], [PrincipalId], [RoleId], [Present])
        SELECT DISTINCT e.[DatabaseId], sp.[Id], sr.[Id], 1
        FROM #EvaluatedAssignments e
        JOIN [deploy].[DatabaseAccessPrincipalCatalog] sp
            ON sp.[Kind] = e.[PrincipalKind]
            AND sp.[Name] = e.[EffectiveLogin]
            AND ISNULL(sp.[LoginName], N'') = CASE WHEN e.[PrincipalKind] = N'Native' THEN e.[EffectiveLogin] ELSE N'' END
            AND ISNULL(sp.[DirectoryId], N'') = ISNULL(e.[ResolvedDirectoryId], N'')
        JOIN [deploy].[DatabaseAccessRoleCatalog] sr ON sr.[Name] = e.[EffectiveRole]
        WHERE e.[IssueCode] IS NULL
          AND NOT EXISTS (
              SELECT 1 FROM [deploy].[DatabaseAccessRoleMembership] dm
              WHERE dm.[DatabaseId] = e.[DatabaseId] AND dm.[PrincipalId] = sp.[Id] AND dm.[RoleId] = sr.[Id]
          );

        -- Write per-database audit trail summarizing global migration outcome
        INSERT INTO [deploy].[DatabaseAccessAudit] ([DatabaseId], [CreatedUtc], [Actor], [Action], [Status], [Detail])
        SELECT
            e.[DatabaseId],
            @now,
            N'DeploymentMigration',
            N'GlobalMigrate',
            CASE WHEN SUM(CASE WHEN e.[IssueCode] IS NOT NULL THEN 1 ELSE 0 END) > 0 THEN N'PartialWithIssues' ELSE N'Succeeded' END,
            CONCAT(
                N'Global deployment migration processed ', COUNT(*), N' legacy assignment(s): ',
                SUM(CASE WHEN e.[IssueCode] IS NULL THEN 1 ELSE 0 END), N' migrated automatically, ',
                SUM(CASE WHEN e.[IssueCode] IS NOT NULL THEN 1 ELSE 0 END), N' skipped for manual review (see dacpac deployment log).')
        FROM #EvaluatedAssignments e
        WHERE e.[DatabaseId] IS NOT NULL
        GROUP BY e.[DatabaseId];
    END

    -- Emit structured plain-text migration report to SQL output / dacpac deployment log
    DECLARE @totalAssignments INT = (SELECT COUNT(*) FROM #EvaluatedAssignments);
    DECLARE @migratedTotal INT = (SELECT COUNT(*) FROM #EvaluatedAssignments WHERE [IssueCode] IS NULL);
    DECLARE @migratedNative INT = (SELECT COUNT(*) FROM #EvaluatedAssignments WHERE [IssueCode] IS NULL AND [PrincipalKind] = N'Native');
    DECLARE @migratedDirectory INT = (SELECT COUNT(*) FROM #EvaluatedAssignments WHERE [IssueCode] IS NULL AND [PrincipalKind] <> N'Native');
    DECLARE @migratedDbCount INT = (SELECT COUNT(DISTINCT [DatabaseId]) FROM #EvaluatedAssignments WHERE [IssueCode] IS NULL);
    DECLARE @issueTotal INT = (SELECT COUNT(*) FROM #EvaluatedAssignments WHERE [IssueCode] IS NOT NULL);
    DECLARE @reportLine NVARCHAR(2000);

    RAISERROR(N'================================================================================', 0, 1) WITH NOWAIT;
    RAISERROR(N'DOrc Legacy Database Access Migration Report', 0, 1) WITH NOWAIT;
    RAISERROR(N'================================================================================', 0, 1) WITH NOWAIT;

    SET @reportLine = CONCAT(
        N'Staged legacy records: Users=', @stagedUserCount,
        N', Permissions=', @stagedPermissionCount,
        N', Distinct assignments=', @totalAssignments);
    RAISERROR(N'%s', 0, 1, @reportLine) WITH NOWAIT;

    SET @reportLine = CONCAT(
        N'Migrated automatically: ', @migratedTotal,
        N' assignment(s) across ', @migratedDbCount,
        N' database(s) (Native SQL=', @migratedNative,
        N', Windows AD/SAM=', @migratedDirectory, N')');
    RAISERROR(N'%s', 0, 1, @reportLine) WITH NOWAIT;

    SET @reportLine = CONCAT(N'Skipped for human review/patching: ', @issueTotal, N' assignment(s)');
    RAISERROR(N'%s', 0, 1, @reportLine) WITH NOWAIT;

    IF @issueTotal > 0
    BEGIN
        RAISERROR(N'--------------------------------------------------------------------------------', 0, 1) WITH NOWAIT;
        RAISERROR(N'Issue Summary by Category:', 0, 1) WITH NOWAIT;

        DECLARE @catCode NVARCHAR(64);
        DECLARE @catCount INT;
        DECLARE category_cursor CURSOR LOCAL FAST_FORWARD FOR
            SELECT [IssueCode], COUNT(*)
            FROM #EvaluatedAssignments
            WHERE [IssueCode] IS NOT NULL
            GROUP BY [IssueCode]
            ORDER BY [IssueCode];

        OPEN category_cursor;
        FETCH NEXT FROM category_cursor INTO @catCode, @catCount;
        WHILE @@FETCH_STATUS = 0
        BEGIN
            SET @reportLine = CONCAT(N'  - ', @catCode, N': ', @catCount);
            RAISERROR(N'%s', 0, 1, @reportLine) WITH NOWAIT;
            FETCH NEXT FROM category_cursor INTO @catCode, @catCount;
        END
        CLOSE category_cursor;
        DEALLOCATE category_cursor;

        RAISERROR(N'--------------------------------------------------------------------------------', 0, 1) WITH NOWAIT;
        RAISERROR(N'Detailed Unmigrated Assignments (review and patch in DOrc Database Access):', 0, 1) WITH NOWAIT;

        DECLARE @issueIdx INT = 0;
        DECLARE @iCode NVARCHAR(64);
        DECLARE @iDbLabel NVARCHAR(600);
        DECLARE @iEnvs NVARCHAR(1024);
        DECLARE @iUserLabel NVARCHAR(600);
        DECLARE @iRoleLabel NVARCHAR(300);
        DECLARE @iDetail NVARCHAR(1024);

        DECLARE issue_cursor CURSOR LOCAL FAST_FORWARD FOR
            SELECT
                [IssueCode],
                CASE
                    WHEN [DatabaseId] IS NULL THEN CONCAT(N'(missing DB_ID=', COALESCE(CAST([LegacyDatabaseId] AS NVARCHAR(20)), N'NULL'), N')')
                    ELSE CONCAT(COALESCE([ServerName], N'?'), N'/', COALESCE([DatabaseName], N'?'), N' (Id=', [DatabaseId], N')')
                END,
                COALESCE([EnvironmentNames], N'(none)'),
                CONCAT(
                    COALESCE([EffectiveLogin], [LegacyDisplayName], N'(unknown)'),
                    N' [LegacyUserId=', COALESCE(CAST([LegacyUserId] AS NVARCHAR(20)), N'NULL'),
                    N', Type=', COALESCE([LegacyLoginType], N'NULL'),
                    N'/', COALESCE([LegacyLanIdType], N'NULL'), N']'),
                CONCAT(
                    COALESCE([EffectiveRole], N'(empty)'),
                    N' [PermissionId=', COALESCE(CAST([LegacyPermissionId] AS NVARCHAR(20)), N'NULL'), N']'),
                COALESCE([Detail], N'')
            FROM #EvaluatedAssignments
            WHERE [IssueCode] IS NOT NULL
            ORDER BY
                CASE WHEN [DatabaseId] IS NULL THEN 1 ELSE 0 END,
                [ServerName],
                [DatabaseName],
                [IssueCode],
                [RowId];

        OPEN issue_cursor;
        FETCH NEXT FROM issue_cursor INTO @iCode, @iDbLabel, @iEnvs, @iUserLabel, @iRoleLabel, @iDetail;
        WHILE @@FETCH_STATUS = 0
        BEGIN
            SET @issueIdx = @issueIdx + 1;
            SET @reportLine = LEFT(CONCAT(
                N'[ISSUE #', @issueIdx, N'] [', @iCode,
                N'] Database=', @iDbLabel,
                N' | Envs=', @iEnvs,
                N' | Account=', @iUserLabel,
                N' | Role=', @iRoleLabel,
                N' | ', @iDetail), 2000);
            RAISERROR(N'%s', 0, 1, @reportLine) WITH NOWAIT;
            FETCH NEXT FROM issue_cursor INTO @iCode, @iDbLabel, @iEnvs, @iUserLabel, @iRoleLabel, @iDetail;
        END
        CLOSE issue_cursor;
        DEALLOCATE issue_cursor;
    END

    RAISERROR(N'================================================================================', 0, 1) WITH NOWAIT;
    RAISERROR(N'End of DOrc Legacy Database Access Migration Report', 0, 1) WITH NOWAIT;
    RAISERROR(N'================================================================================', 0, 1) WITH NOWAIT;

    IF OBJECT_ID('dbo.ENVIRONMENT_USER_MAP_MIGRATION_STAGING', 'U') IS NOT NULL
        DROP TABLE [dbo].[ENVIRONMENT_USER_MAP_MIGRATION_STAGING];
    IF OBJECT_ID('dbo.USERS_MIGRATION_STAGING', 'U') IS NOT NULL
        DROP TABLE [dbo].[USERS_MIGRATION_STAGING];
    IF OBJECT_ID('dbo.PERMISSION_MIGRATION_STAGING', 'U') IS NOT NULL
        DROP TABLE [dbo].[PERMISSION_MIGRATION_STAGING];

    COMMIT TRANSACTION;
END
GO

-- Retire legacy procedures and tables once schema rebuilds and data migration have completed
IF OBJECT_ID('dbo.usp_Insert_Environment_User_Map', 'P') IS NOT NULL
BEGIN
    DROP PROCEDURE [dbo].[usp_Insert_Environment_User_Map];
    PRINT 'Dropped legacy procedure dbo.usp_Insert_Environment_User_Map';
END
GO

IF OBJECT_ID('deploy.DatabaseAccessExclusion', 'U') IS NOT NULL
BEGIN
    DROP TABLE [deploy].[DatabaseAccessExclusion];
    PRINT 'Dropped retired table deploy.DatabaseAccessExclusion';
END
GO

IF OBJECT_ID('deploy.DatabaseAccessMigrationIssue', 'U') IS NOT NULL
BEGIN
    DROP TABLE [deploy].[DatabaseAccessMigrationIssue];
    PRINT 'Dropped retired table deploy.DatabaseAccessMigrationIssue';
END
GO

IF OBJECT_ID('deploy.DatabaseAccessDatabaseMembership', 'U') IS NOT NULL
    DROP TABLE [deploy].[DatabaseAccessDatabaseMembership];
IF OBJECT_ID('deploy.DatabaseAccessDatabasePrincipal', 'U') IS NOT NULL
    DROP TABLE [deploy].[DatabaseAccessDatabasePrincipal];
IF OBJECT_ID('deploy.DatabaseAccessDatabaseRole', 'U') IS NOT NULL
    DROP TABLE [deploy].[DatabaseAccessDatabaseRole];
IF OBJECT_ID('deploy.DatabaseAccessSharedPrincipal', 'U') IS NOT NULL
    DROP TABLE [deploy].[DatabaseAccessSharedPrincipal];
IF OBJECT_ID('deploy.DatabaseAccessSharedRole', 'U') IS NOT NULL
    DROP TABLE [deploy].[DatabaseAccessSharedRole];
GO

IF OBJECT_ID('dbo.ENVIRONMENT_USER_MAP', 'U') IS NOT NULL
BEGIN
    DROP TABLE [dbo].[ENVIRONMENT_USER_MAP];
    PRINT 'Dropped legacy table dbo.ENVIRONMENT_USER_MAP';
END
GO

IF OBJECT_ID('dbo.USERS', 'U') IS NOT NULL
BEGIN
    DROP TABLE [dbo].[USERS];
    PRINT 'Dropped legacy table dbo.USERS';
END
GO

IF OBJECT_ID('dbo.PERMISSION', 'U') IS NOT NULL
BEGIN
    DROP TABLE [dbo].[PERMISSION];
    PRINT 'Dropped legacy table dbo.PERMISSION';
END
GO
