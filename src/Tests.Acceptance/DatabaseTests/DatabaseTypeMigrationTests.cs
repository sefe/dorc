using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Acceptance.DatabaseTests
{
    [TestClass]
    public class DatabaseTypeMigrationTests : DatabaseTestBase
    {
        [TestMethod]
        [TestCategory("Database")]
        public void Republish_NeverInfersDatabaseTypeFromTags()
        {
            PublishDacpac();
            ExecuteEphemeral("""
                INSERT INTO [deploy].[Database] ([Name], [ServerName], [DatabaseType], [Tags]) VALUES
                    (N'sql', N'sql-server', N'SqlServer', N'ClickHouse;CosmosDB'),
                    (N'postgres', N'project/pg', N'PostgreSql', N'ClickHouse');
                INSERT INTO [deploy].[DatabaseTag] ([DatabaseId], [Tag])
                SELECT [Id], N'ClickHouse' FROM [deploy].[Database];
                INSERT INTO [deploy].[DatabaseTag] ([DatabaseId], [Tag])
                SELECT [Id], N'CosmosDB' FROM [deploy].[Database] WHERE [Name] = N'sql';
                """);

            PublishDacpac();
            PublishDacpac();

            Assert.AreEqual(1, ExecuteScalarInt(
                "SELECT COUNT(*) FROM [deploy].[Database] WHERE [Name] = N'sql' AND [DatabaseType] = N'SqlServer'"));
            Assert.AreEqual(1, ExecuteScalarInt(
                "SELECT COUNT(*) FROM [deploy].[Database] WHERE [Name] = N'postgres' AND [DatabaseType] = N'PostgreSql'"));
        }

        [TestMethod]
        [TestCategory("Database")]
        public void AivenScopeMigration_UsesEnvironmentCloudEvidence_AndPreservesLinksOnRepublish()
        {
            PublishDacpac();
            SeedAivenDatabases(sharedDatabase: false);

            PublishDacpac();
            PublishDacpac();

            Assert.AreEqual(1, ExecuteScalarInt("""
                SELECT COUNT(*) FROM [deploy].[Database] d
                JOIN [deploy].[EnvironmentDatabase] ed ON ed.[DbId] = d.[Id]
                JOIN [deploy].[Environment] e ON e.[Id] = ed.[EnvId]
                WHERE e.[Name] = N'env-a' AND d.[ServerName] = N'project-a/shared-service'
                  AND d.[DatabaseType] = N'ClickHouse'
                """));
            Assert.AreEqual(1, ExecuteScalarInt("""
                SELECT COUNT(*) FROM [deploy].[Database] d
                JOIN [deploy].[EnvironmentDatabase] ed ON ed.[DbId] = d.[Id]
                JOIN [deploy].[Environment] e ON e.[Id] = ed.[EnvId]
                WHERE e.[Name] = N'env-b' AND d.[ServerName] = N'project-b/shared-service'
                  AND d.[DatabaseType] = N'ClickHouse'
                """));
            Assert.AreEqual(2, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[Database]"));
        }

        [TestMethod]
        [TestCategory("Database")]
        public void AivenScopeMigration_AmbiguousLegacyIdentity_RejectsRatherThanChoosingAProject()
        {
            PublishDacpac();
            SeedAivenDatabases(sharedDatabase: true);

            var error = Assert.Throws<Exception>(() => PublishDacpac());

            StringAssert.Contains(error.ToString(), "Aiven database scope migration is ambiguous");
            Assert.AreEqual(1, ExecuteScalarInt(
                "SELECT COUNT(*) FROM [deploy].[Database] WHERE [ServerName] = N'shared-service'"));
        }

        [TestMethod]
        [TestCategory("Database")]
        public void AivenScopeMigration_DoesNotReclassifyExplicitSqlServerEvenWithCloudEvidence()
        {
            PublishDacpac();
            SeedAivenDatabases(sharedDatabase: true);
            ExecuteEphemeral("UPDATE [deploy].[Database] SET [DatabaseType] = N'SqlServer'");

            PublishDacpac();

            Assert.AreEqual(1, ExecuteScalarInt("""
                SELECT COUNT(*) FROM [deploy].[Database]
                WHERE [ServerName] = N'shared-service' AND [DatabaseType] = N'SqlServer'
                """));
        }

        private void SeedAivenDatabases(bool sharedDatabase)
        {
            ExecuteEphemeral("""
                INSERT INTO [deploy].[Environment] ([Name]) VALUES (N'env-a'), (N'env-b');
                INSERT INTO [deploy].[Database] ([Name], [ServerName], [DatabaseType])
                VALUES (N'analytics', N'shared-service', N'ClickHouse');
                INSERT INTO [deploy].[CloudResource] ([Name], [Provider], [ResourceType], [ResourceIdentifier]) VALUES
                    (N'analytics-a', N'Aiven', N'aiven_clickhouse_database', N'project-a/shared-service/analytics'),
                    (N'analytics-b', N'Aiven', N'aiven_clickhouse_database', N'project-b/shared-service/analytics');
                INSERT INTO [deploy].[EnvironmentCloudResource] ([EnvId], [CloudResourceId])
                SELECT e.[Id], c.[Id] FROM [deploy].[Environment] e
                JOIN [deploy].[CloudResource] c ON
                    (e.[Name] = N'env-a' AND c.[Name] = N'analytics-a')
                    OR (e.[Name] = N'env-b' AND c.[Name] = N'analytics-b');
                INSERT INTO [deploy].[EnvironmentDatabase] ([EnvId], [DbId])
                SELECT e.[Id], d.[Id] FROM [deploy].[Environment] e
                CROSS JOIN [deploy].[Database] d WHERE e.[Name] = N'env-a';
                """);
            if (!sharedDatabase)
            {
                ExecuteEphemeral("""
                    INSERT INTO [deploy].[Database] ([Name], [ServerName], [DatabaseType])
                    VALUES (N'analytics', N'shared-service', N'ClickHouse');
                    """);
            }
            ExecuteEphemeral("""
                INSERT INTO [deploy].[EnvironmentDatabase] ([EnvId], [DbId])
                SELECT e.[Id], (SELECT MAX([Id]) FROM [deploy].[Database])
                FROM [deploy].[Environment] e WHERE e.[Name] = N'env-b';
                """);
        }
    }
}
