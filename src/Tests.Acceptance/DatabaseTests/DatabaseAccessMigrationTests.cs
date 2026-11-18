using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Acceptance.DatabaseTests
{
    [TestClass]
    public class DatabaseAccessMigrationTests : DatabaseTestBase
    {
        [TestMethod]
        [TestCategory("Database")]
        public void DropsRetiredPrereleaseTables_AndPreservesCatalogOnRepublish()
        {
            PublishDacpac();

            ExecuteEphemeral("""
                INSERT INTO [deploy].[Database] ([Name], [ServerName], [DatabaseType]) VALUES
                    (N'app-a', N'sql-01', N'SqlServer'),
                    (N'app-b', N'sql-01', N'SqlServer');

                DECLARE @dbA INT = (SELECT [Id] FROM [deploy].[Database] WHERE [Name] = N'app-a');
                DECLARE @dbB INT = (SELECT [Id] FROM [deploy].[Database] WHERE [Name] = N'app-b');

                INSERT INTO [deploy].[DatabaseAccessConfiguration] ([DatabaseId], [Provider], [Revision]) VALUES
                    (@dbA, N'sql-server', 1),
                    (@dbB, N'sql-server', 1);

                INSERT INTO [deploy].[DatabaseAccessPrincipalCatalog] ([Name], [Kind], [LoginName], [DirectoryId])
                VALUES (N'app_login', N'Native', N'app_login', NULL);
                INSERT INTO [deploy].[DatabaseAccessRoleCatalog] ([Name])
                VALUES (N'db_datareader');

                DECLARE @principalId INT = (SELECT [Id] FROM [deploy].[DatabaseAccessPrincipalCatalog] WHERE [Name] = N'app_login');
                DECLARE @roleId INT = (SELECT [Id] FROM [deploy].[DatabaseAccessRoleCatalog] WHERE [Name] = N'db_datareader');

                INSERT INTO [deploy].[DatabaseAccessPrincipalBinding] ([DatabaseId], [PrincipalId], [Present], [LegacyUserId]) VALUES
                    (@dbA, @principalId, 1, 10),
                    (@dbB, @principalId, 1, 11);

                INSERT INTO [deploy].[DatabaseAccessRoleBinding] ([DatabaseId], [RoleId], [Managed], [Present]) VALUES
                    (@dbA, @roleId, 0, 1),
                    (@dbB, @roleId, 0, 1);

                INSERT INTO [deploy].[DatabaseAccessRoleMembership] ([DatabaseId], [PrincipalId], [RoleId], [Present]) VALUES
                    (@dbA, @principalId, @roleId, 1),
                    (@dbB, @principalId, @roleId, 1);

                CREATE TABLE [deploy].[DatabaseAccessExclusion] ([DatabaseId] INT NOT NULL PRIMARY KEY);
                INSERT INTO [deploy].[DatabaseAccessExclusion] VALUES (@dbA);

                CREATE TABLE [deploy].[DatabaseAccessMigrationIssue] ([Id] BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY, [Code] NVARCHAR(64) NOT NULL);
                INSERT INTO [deploy].[DatabaseAccessMigrationIssue] ([Code]) VALUES (N'PrereleaseRow');
                """);

            PublishDacpac();
            PublishDacpac();

            Assert.AreEqual(1, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[DatabaseAccessPrincipalCatalog] WHERE [Name] = N'app_login'"));
            Assert.AreEqual(1, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[DatabaseAccessRoleCatalog] WHERE [Name] = N'db_datareader'"));
            Assert.AreEqual(2, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[DatabaseAccessPrincipalBinding]"));
            Assert.AreEqual(2, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[DatabaseAccessRoleBinding]"));
            Assert.AreEqual(2, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[DatabaseAccessRoleMembership]"));
            Assert.AreEqual(0, ExecuteScalarInt("SELECT CASE WHEN OBJECT_ID(N'deploy.DatabaseAccessExclusion', N'U') IS NULL THEN 0 ELSE 1 END"));
            Assert.AreEqual(0, ExecuteScalarInt("SELECT CASE WHEN OBJECT_ID(N'deploy.DatabaseAccessMigrationIssue', N'U') IS NULL THEN 0 ELSE 1 END"));
        }

        [TestMethod]
        [TestCategory("Database")]
        public void GlobalLegacyUsersMigration_MigratesNativeAndWindowsPrincipalsAndEmitsDeploymentLogReport()
        {
            PublishDacpac();

            var probedWindowsAccounts = new (string AccountName, string ExpectedKind, string ExpectedSid)[]
            {
                (@"BUILTIN\Users", "DirectoryGroup", "S-1-5-32-545"),
                (@"NT AUTHORITY\NETWORK SERVICE", "DirectoryUser", "S-1-5-20")
            };
            var resolvedWindowsAccounts = probedWindowsAccounts
                .Where(account => ExecuteScalarInt($"SELECT CASE WHEN SUSER_SID(N'{account.AccountName}') IS NOT NULL THEN 1 ELSE 0 END") == 1)
                .ToArray();
            var resolvedWindowsCount = resolvedWindowsAccounts.Length;
            var unresolvedWindowsCount = 1 + (probedWindowsAccounts.Length - resolvedWindowsCount);

            ExecuteEphemeral("""
                INSERT INTO [deploy].[Environment] ([Name], [Secure], [IsProd], [ObjectId]) VALUES
                    (N'Prod-EU', 1, 1, NEWID()),
                    (N'Prod-US', 1, 1, NEWID());

                INSERT INTO [deploy].[Database] ([Name], [ServerName], [DatabaseType]) VALUES
                    (N'trade-db-1', N'sql-prod-01', N'SqlServer'),
                    (N'trade-db-2', N'sql-prod-01', N'SqlServer'),
                    (N'analytics-pg', N'proj/pg-01', N'PostgreSql');

                DECLARE @envEu INT = (SELECT [Id] FROM [deploy].[Environment] WHERE [Name] = N'Prod-EU');
                DECLARE @envUs INT = (SELECT [Id] FROM [deploy].[Environment] WHERE [Name] = N'Prod-US');
                DECLARE @db1 INT = (SELECT [Id] FROM [deploy].[Database] WHERE [Name] = N'trade-db-1');
                DECLARE @db2 INT = (SELECT [Id] FROM [deploy].[Database] WHERE [Name] = N'trade-db-2');
                DECLARE @dbPg INT = (SELECT [Id] FROM [deploy].[Database] WHERE [Name] = N'analytics-pg');

                INSERT INTO [deploy].[EnvironmentDatabase] ([EnvId], [DbId]) VALUES
                    (@envEu, @db1),
                    (@envUs, @db1),
                    (@envEu, @db2);

                -- Pre-existing desired state on @db2 where 'revoked_login' is explicitly marked Present = 0
                INSERT INTO [deploy].[DatabaseAccessConfiguration] ([DatabaseId], [Provider], [Revision]) VALUES (@db2, N'sql-server', 3);
                INSERT INTO [deploy].[DatabaseAccessPrincipalCatalog] ([Name], [Kind], [LoginName], [DirectoryId]) VALUES (N'revoked_login', N'Native', N'revoked_login', NULL);
                INSERT INTO [deploy].[DatabaseAccessRoleCatalog] ([Name]) VALUES (N'db_datareader');
                DECLARE @revokedPrincipalId INT = (SELECT [Id] FROM [deploy].[DatabaseAccessPrincipalCatalog] WHERE [Name] = N'revoked_login');
                DECLARE @readerRoleId INT = (SELECT [Id] FROM [deploy].[DatabaseAccessRoleCatalog] WHERE [Name] = N'db_datareader');
                INSERT INTO [deploy].[DatabaseAccessPrincipalBinding] ([DatabaseId], [PrincipalId], [Present], [LegacyUserId]) VALUES (@db2, @revokedPrincipalId, 0, 99);
                INSERT INTO [deploy].[DatabaseAccessRoleBinding] ([DatabaseId], [RoleId], [Managed], [Present]) VALUES (@db2, @readerRoleId, 0, 1);

                -- Legacy schema objects as present on an existing DOrc database
                CREATE TABLE [dbo].[USERS] (
                    [User_ID] INT IDENTITY(1,1) NOT NULL CONSTRAINT [USERS_User_ID_pk] PRIMARY KEY CLUSTERED,
                    [Login_ID] NVARCHAR(50) NULL,
                    [Display_Name] NVARCHAR(50) NULL,
                    [Team] NVARCHAR(50) NULL,
                    [Login_Type] NVARCHAR(50) NULL,
                    [LAN_ID] NVARCHAR(50) NULL,
                    [LAN_ID_Type] NVARCHAR(50) NULL
                );
                CREATE TABLE [dbo].[PERMISSION] (
                    [Permission_ID] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PERMISSION_Permission_ID_pk] PRIMARY KEY CLUSTERED,
                    [Permission_Name] NVARCHAR(50) NULL,
                    [Display_Name] NVARCHAR(50) NULL
                );
                CREATE TABLE [dbo].[ENVIRONMENT_USER_MAP] (
                    [DB_ID] INT NULL,
                    [User_ID] INT NULL,
                    [Permission_ID] INT NULL,
                    CONSTRAINT [FK_EnvironmentUserMap_Database] FOREIGN KEY ([DB_ID]) REFERENCES [deploy].[Database] ([Id]),
                    CONSTRAINT [ENVIRONMENT_USER_MAP_PERMISSION_Permission_ID_fk] FOREIGN KEY ([Permission_ID]) REFERENCES [dbo].[PERMISSION] ([Permission_ID]),
                    CONSTRAINT [ENVIRONMENT_USER_MAP_USERS_User_ID_fk] FOREIGN KEY ([User_ID]) REFERENCES [dbo].[USERS] ([User_ID])
                );
                CREATE TABLE [deploy].[DatabaseAccessExclusion] (
                    [DatabaseId] INT NOT NULL,
                    [LegacyUserId] INT NOT NULL,
                    CONSTRAINT [PK_DatabaseAccessExclusion] PRIMARY KEY ([DatabaseId], [LegacyUserId])
                );
                INSERT INTO [deploy].[DatabaseAccessExclusion] ([DatabaseId], [LegacyUserId]) VALUES (@db2, 50);
                EXEC(N'CREATE PROCEDURE [dbo].[usp_Insert_Environment_User_Map] AS SELECT 1;');

                SET IDENTITY_INSERT [dbo].[USERS] ON;
                INSERT INTO [dbo].[USERS] ([User_ID], [Login_ID], [Display_Name], [Team], [Login_Type], [LAN_ID], [LAN_ID_Type]) VALUES
                    (1, N'svc_sql', N'SQL App', N'Core', N'SQL', N'svc_sql', N'SQL'),
                    (2, NULL, N'Cmdlet App', N'Core', N'Endur', N'svc_cmdlet', N'Endur'),
                    (3, N'BUILTIN\Users', N'Local Users Group', N'Ops', N'Windows', N'BUILTIN\Users', N'GROUP'),
                    (12, N'NT AUTHORITY\NETWORK SERVICE', N'Network Service', N'Ops', N'Windows', N'NT AUTHORITY\NETWORK SERVICE', N'User'),
                    (13, N'CORP\missing_readers_954', N'Missing AD Group', N'Ops', N'Windows', N'CORP\missing_readers_954', N'GROUP'),
                    (14, N'CORP\ambig_svc', N'Ambiguous Kind', N'Ops', N'Windows', N'CORP\ambig_svc', N'Service'),
                    (4, N'svc_owner', N'Owner Login', N'Ops', N'SQL', N'svc_owner', N'SQL'),
                    (5, N'sa', N'System Admin', N'Ops', N'SQL', N'sa', N'SQL'),
                    (6, N'login_one', N'Mismatched Login', N'Ops', N'SQL', N'login_two', N'SQL'),
                    (7, N'svc_sso', N'SSO User', N'Ops', N'OAuth', N'svc_sso', N'OAuth'),
                    (8, N'dup_login', N'Dup A', N'Ops', N'SQL', N'dup_login', N'SQL'),
                    (9, N'dup_login', N'Dup B', N'Ops', N'Endur', N'dup_login', N'Endur'),
                    (10, N'revoked_login', N'Previously Revoked', N'Ops', N'SQL', N'revoked_login', N'SQL'),
                    (11, N'app_custom_role', N'Colliding User', N'Ops', N'SQL', N'app_custom_role', N'SQL');
                SET IDENTITY_INSERT [dbo].[USERS] OFF;

                SET IDENTITY_INSERT [dbo].[PERMISSION] ON;
                INSERT INTO [dbo].[PERMISSION] ([Permission_ID], [Permission_Name], [Display_Name]) VALUES
                    (1, N'db_datareader', N'Data Reader'),
                    (2, N'app_custom_role', N'Custom Role'),
                    (3, N'db_owner', N'DB Owner'),
                    (4, N'db_securityadmin', N'Security Admin');
                SET IDENTITY_INSERT [dbo].[PERMISSION] OFF;

                INSERT INTO [dbo].[ENVIRONMENT_USER_MAP] ([DB_ID], [User_ID], [Permission_ID]) VALUES
                    (@db1, 1, 1),
                    (@db1, 1, 2),
                    (@db2, 1, 1),
                    (@db1, 2, 1),
                    (@db1, 3, 1),     -- Windows group BUILTIN\Users (S-1-5-32-545 on Windows SQL Server)
                    (@db1, 12, 1),    -- Windows user NT AUTHORITY\NETWORK SERVICE (S-1-5-20 on Windows SQL Server)
                    (@db1, 13, 1),    -- UnresolvedWindowsAccount (CORP\missing_readers_954)
                    (@db1, 14, 1),    -- UnsupportedLoginType (Windows LAN_ID_Type='Service')
                    (@db1, 4, 3),     -- PrivilegeReviewRequired (db_owner)
                    (@db1, 1, 4),     -- ProtectedRoleBlocked (db_securityadmin)
                    (@db1, 5, 1),     -- ProtectedPrincipalBlocked (sa)
                    (@db1, 6, 1),     -- ConflictingPrincipal (Login_ID <> LAN_ID)
                    (@db1, 7, 1),     -- UnsupportedLoginType (OAuth)
                    (@db1, 8, 1),     -- ConflictingPrincipal (duplicate login on @db1)
                    (@db1, 9, 1),     -- ConflictingPrincipal (duplicate login on @db1)
                    (@db1, 11, 1),    -- ConflictingPrincipal (user name collides with role app_custom_role)
                    (@db2, 10, 1),    -- ConflictingPrincipal (conflicts with pre-existing absent state on @db2)
                    (@dbPg, 1, 1),    -- UnsupportedProvider (analytics-pg is PostgreSql)
                    (NULL, 1, 1);     -- OrphanedAssignment (NULL DB_ID)
                """);

            var firstRunMessages = PublishDacpacWithMessages();
            var reportText = string.Join("\n", firstRunMessages);
            var secondRunMessages = PublishDacpacWithMessages();

            // Retired objects and staging tables must all be gone (and no migration issue table exists)
            Assert.AreEqual(0, ExecuteScalarInt("SELECT ISNULL(OBJECT_ID(N'dbo.USERS', N'U'), 0)"));
            Assert.AreEqual(0, ExecuteScalarInt("SELECT ISNULL(OBJECT_ID(N'dbo.PERMISSION', N'U'), 0)"));
            Assert.AreEqual(0, ExecuteScalarInt("SELECT ISNULL(OBJECT_ID(N'dbo.ENVIRONMENT_USER_MAP', N'U'), 0)"));
            Assert.AreEqual(0, ExecuteScalarInt("SELECT ISNULL(OBJECT_ID(N'dbo.usp_Insert_Environment_User_Map', N'P'), 0)"));
            Assert.AreEqual(0, ExecuteScalarInt("SELECT ISNULL(OBJECT_ID(N'deploy.DatabaseAccessExclusion', N'U'), 0)"));
            Assert.AreEqual(0, ExecuteScalarInt("SELECT ISNULL(OBJECT_ID(N'deploy.DatabaseAccessMigrationIssue', N'U'), 0)"));
            Assert.AreEqual(0, ExecuteScalarInt("SELECT ISNULL(OBJECT_ID(N'dbo.USERS_MIGRATION_STAGING', N'U'), 0)"));
            Assert.AreEqual(0, ExecuteScalarInt("SELECT ISNULL(OBJECT_ID(N'dbo.PERMISSION_MIGRATION_STAGING', N'U'), 0)"));
            Assert.AreEqual(0, ExecuteScalarInt("SELECT ISNULL(OBJECT_ID(N'dbo.ENVIRONMENT_USER_MAP_MIGRATION_STAGING', N'U'), 0)"));

            // Clean Native SQL & LAN_ID fallback assignments migrated into catalog and per-database memberships
            Assert.AreEqual(1, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[DatabaseAccessPrincipalCatalog] WHERE [Name] = N'svc_sql' AND [Kind] = N'Native'"));
            Assert.AreEqual(1, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[DatabaseAccessPrincipalCatalog] WHERE [Name] = N'svc_cmdlet' AND [Kind] = N'Native'"));

            foreach (var account in resolvedWindowsAccounts)
            {
                Assert.AreEqual(
                    1,
                    ExecuteScalarInt($"SELECT COUNT(*) FROM [deploy].[DatabaseAccessPrincipalCatalog] WHERE [Name] = N'{account.AccountName}' AND [Kind] = N'{account.ExpectedKind}' AND [DirectoryId] = N'{account.ExpectedSid}' AND [LoginName] IS NULL"));
            }

            var expectedMigratedCount = 4 + resolvedWindowsCount;
            Assert.AreEqual(expectedMigratedCount, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[DatabaseAccessRoleMembership] WHERE [Present] = 1"));
            Assert.Contains($"Migrated automatically: {expectedMigratedCount} assignment(s) across 2 database(s) (Native SQL=4, Windows AD/SAM={resolvedWindowsCount})", reportText, reportText);
            Assert.Contains($"UnresolvedWindowsAccount: {unresolvedWindowsCount}", reportText, reportText);

            Assert.AreEqual(4, ExecuteScalarInt("SELECT [Revision] FROM [deploy].[DatabaseAccessConfiguration] c JOIN [deploy].[Database] d ON d.[Id] = c.[DatabaseId] WHERE d.[Name] = N'trade-db-2'"));
            Assert.AreEqual(3, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[DatabaseAccessAudit] WHERE [Action] = N'GlobalMigrate' AND [Status] = N'PartialWithIssues'"));

            // Verify plain-text report in SQL / dacpac deployment log contains every category, environment context, and remediation note
            Assert.Contains("DOrc Legacy Database Access Migration Report", reportText, reportText);
            Assert.Contains("Staged legacy records: Users=14, Permissions=4, Distinct assignments=19", reportText, reportText);
            Assert.Contains("Envs=Prod-EU, Prod-US", reportText, reportText);
            Assert.Contains("PrivilegeReviewRequired: 1", reportText, reportText);
            Assert.Contains("ProtectedRoleBlocked: 1", reportText, reportText);
            Assert.Contains("ProtectedPrincipalBlocked: 1", reportText, reportText);
            Assert.Contains("InvalidWindowsIdentityType: 1", reportText, reportText);
            Assert.Contains("UnsupportedLoginType: 1", reportText, reportText);
            Assert.Contains("UnsupportedProvider: 1", reportText, reportText);
            Assert.Contains("OrphanedAssignment: 1", reportText, reportText);
            Assert.Contains("ConflictingPrincipal: 5", reportText, reportText);
            Assert.Contains("CORP\\missing_readers_954", reportText, reportText);
            Assert.Contains("Role=db_owner [PermissionId=3]", reportText, reportText);

            // Second publish is a no-op because legacy tables are already retired
            Assert.DoesNotContain("DOrc Legacy Database Access Migration Report", string.Join("\n", secondRunMessages));
        }
    }
}