using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Acceptance.DatabaseTests
{
    [TestClass]
    public class DatabaseAccessMigrationTests : DatabaseTestBase
    {
        [TestMethod]
        [TestCategory("Database")]
        public void NormalisesStagedDatabaseAccessRows_AndPreservesCatalogueOnRepublish()
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

                CREATE TABLE [deploy].[DatabaseAccessPrincipal] (
                    [DatabaseId] INT NOT NULL,
                    [Name] NVARCHAR(128) NOT NULL,
                    [Kind] NVARCHAR(20) NOT NULL,
                    [LoginName] NVARCHAR(128) NULL,
                    [DirectoryId] NVARCHAR(256) NULL,
                    [Present] BIT NOT NULL,
                    [LegacyUserId] INT NULL,
                    CONSTRAINT [PK_DatabaseAccessPrincipal] PRIMARY KEY ([DatabaseId], [Name])
                );

                CREATE TABLE [deploy].[DatabaseAccessRole] (
                    [DatabaseId] INT NOT NULL,
                    [Name] NVARCHAR(128) NOT NULL,
                    [Managed] BIT NOT NULL,
                    [Present] BIT NOT NULL,
                    CONSTRAINT [PK_DatabaseAccessRole] PRIMARY KEY ([DatabaseId], [Name])
                );

                CREATE TABLE [deploy].[DatabaseAccessMembership] (
                    [DatabaseId] INT NOT NULL,
                    [Principal] NVARCHAR(128) NOT NULL,
                    [Role] NVARCHAR(128) NOT NULL,
                    [Present] BIT NOT NULL,
                    CONSTRAINT [PK_DatabaseAccessMembership] PRIMARY KEY ([DatabaseId], [Principal], [Role])
                );

                INSERT INTO [deploy].[DatabaseAccessPrincipal]
                    ([DatabaseId], [Name], [Kind], [LoginName], [DirectoryId], [Present], [LegacyUserId])
                VALUES
                    (@dbA, N'app_login', N'Native', N'app_login', NULL, 1, 10),
                    (@dbB, N'app_login', N'Native', N'app_login', NULL, 1, 11);

                INSERT INTO [deploy].[DatabaseAccessRole]
                    ([DatabaseId], [Name], [Managed], [Present])
                VALUES
                    (@dbA, N'db_datareader', 0, 1),
                    (@dbB, N'db_datareader', 0, 1);

                INSERT INTO [deploy].[DatabaseAccessMembership]
                    ([DatabaseId], [Principal], [Role], [Present])
                VALUES
                    (@dbA, N'app_login', N'db_datareader', 1),
                    (@dbB, N'app_login', N'db_datareader', 1);
                """);

            PublishDacpac();
            PublishDacpac();

            Assert.AreEqual(1, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[DatabaseAccessSharedPrincipal] WHERE [Name] = N'app_login'"));
            Assert.AreEqual(1, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[DatabaseAccessSharedRole] WHERE [Name] = N'db_datareader'"));
            Assert.AreEqual(2, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[DatabaseAccessDatabasePrincipal]"));
            Assert.AreEqual(2, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[DatabaseAccessDatabaseRole]"));
            Assert.AreEqual(2, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[DatabaseAccessDatabaseMembership]"));
            Assert.AreEqual(0, ExecuteScalarInt("SELECT CASE WHEN OBJECT_ID(N'deploy.DatabaseAccessPrincipal', N'U') IS NULL THEN 0 ELSE 1 END"));
            Assert.AreEqual(0, ExecuteScalarInt("SELECT CASE WHEN OBJECT_ID(N'deploy.DatabaseAccessPrincipal_NORMALISATION_STAGING', N'U') IS NULL THEN 0 ELSE 1 END"));
        }
    }
}
