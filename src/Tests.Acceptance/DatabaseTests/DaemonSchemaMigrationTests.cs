using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;

namespace Tests.Acceptance.DatabaseTests
{
    /// <summary>
    /// DB-level integration tests for the daemons modernisation (issue sefe/dorc#649).
    /// Covers HLPS SC-01 (schema completeness), SC-02 (populated-DB migration correctness),
    /// and SC-05b (RefDataAuditAction seed idempotency).
    /// </summary>
    [TestClass]
    public class DaemonSchemaMigrationTests : DatabaseTestBase
    {
        [TestMethod]
        [TestCategory("Database")]
        public void SC01_PublishToEmptyDb_CreatesDeploySchema()
        {
            PublishDacpac();

            Assert.AreNotEqual(0, ExecuteScalarInt("SELECT ISNULL(OBJECT_ID('deploy.Daemon', 'U'), 0)"),
                "deploy.Daemon should exist after publish");
            Assert.AreNotEqual(0, ExecuteScalarInt("SELECT ISNULL(OBJECT_ID('deploy.ServerDaemon', 'U'), 0)"),
                "deploy.ServerDaemon should exist after publish");
            Assert.AreNotEqual(0, ExecuteScalarInt("SELECT ISNULL(OBJECT_ID('deploy.DaemonAudit', 'U'), 0)"),
                "deploy.DaemonAudit should exist after publish");

            Assert.AreEqual(0, ExecuteScalarInt("SELECT ISNULL(OBJECT_ID('dbo.SERVICE', 'U'), 0)"),
                "dbo.SERVICE should be gone after publish");
            Assert.AreEqual(0, ExecuteScalarInt("SELECT ISNULL(OBJECT_ID('dbo.SERVER_SERVICE_MAP', 'U'), 0)"),
                "dbo.SERVER_SERVICE_MAP should be gone after publish");

            Assert.AreEqual(5, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[RefDataAuditAction]"),
                "RefDataAuditAction seed should populate exactly 5 rows (Create/Update/Delete/Attach/Detach)");
        }

        [TestMethod]
        [TestCategory("Database")]
        public void SC02_PopulatedLegacyDb_MigratesCleanly()
        {
            // Seed legacy state before publish. Fixture creates dbo.SERVER (if not present),
            // populates it with a test server, creates dbo.SERVICE and dbo.SERVER_SERVICE_MAP
            // with 4 service rows (including one NULL Service_Name) and 4 map rows (including
            // one orphan Server_ID referencing a non-existent server).
            var fixtureSql = ReadFixture("DatabaseTests/Fixtures/LegacyDaemonSeed.sql");
            ExecuteEphemeral(fixtureSql);

            // Snapshot legacy state
            var legacyServiceCount = ExecuteScalarInt("SELECT COUNT(*) FROM [dbo].[SERVICE]");
            var legacyMaxServiceId = ExecuteScalarInt("SELECT ISNULL(MAX(Service_ID), 0) FROM [dbo].[SERVICE]");
            Assert.AreEqual(4, legacyServiceCount, "Fixture should have seeded 4 SERVICE rows");
            Assert.AreEqual(400, legacyMaxServiceId, "Fixture's highest Service_ID should be 400 (the NULL-name row)");

            // Publish — pre-deploy stages+empties legacy tables, schema phase drops them,
            // post-deploy copies staging into deploy.Daemon / deploy.ServerDaemon.
            PublishDacpac();

            // Daemon.Name is NOT NULL → the NULL-Service_Name row is filtered. 4 → 3 rows.
            var daemonCount = ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[Daemon]");
            Assert.AreEqual(3, daemonCount,
                "deploy.Daemon should have 3 rows (4 staged - 1 with NULL Service_Name filtered)");

            // Id values preserved via IDENTITY_INSERT. Max should still be 300 (NULL-name row was 400 and filtered).
            var daemonMaxId = ExecuteScalarInt("SELECT ISNULL(MAX(Id), 0) FROM [deploy].[Daemon]");
            Assert.AreEqual(300, daemonMaxId,
                "deploy.Daemon MAX(Id) should equal 300 (Ids 100/200/300 migrated; 400 filtered for NULL name)");

            // Specific rows present
            Assert.AreEqual(1, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[Daemon] WHERE Id = 100 AND Name = 'alpha-daemon'"));
            Assert.AreEqual(1, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[Daemon] WHERE Id = 200 AND Name = 'beta-daemon'"));
            Assert.AreEqual(1, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[Daemon] WHERE Id = 300 AND Name = 'gamma-daemon'"));
            Assert.AreEqual(0, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[Daemon] WHERE Id = 400"),
                "NULL-name row (Id 400) should be filtered out");

            // ServerDaemon: 4 staged - 1 orphan Server_ID filtered = 3 rows.
            var serverDaemonCount = ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[ServerDaemon]");
            Assert.AreEqual(3, serverDaemonCount,
                "deploy.ServerDaemon should have 3 rows (4 staged - 1 orphan Server_ID filtered)");

            Assert.AreEqual(0, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[ServerDaemon] WHERE ServerId = 99999"),
                "Orphan mapping (Server_ID 99999) should be filtered out");

            // Legacy tables gone
            Assert.AreEqual(0, ExecuteScalarInt("SELECT ISNULL(OBJECT_ID('dbo.SERVICE', 'U'), 0)"));
            Assert.AreEqual(0, ExecuteScalarInt("SELECT ISNULL(OBJECT_ID('dbo.SERVER_SERVICE_MAP', 'U'), 0)"));

            // Staging tables cleaned up
            Assert.AreEqual(0, ExecuteScalarInt("SELECT ISNULL(OBJECT_ID('dbo.SERVICE_MIGRATION_STAGING', 'U'), 0)"),
                "Staging table dbo.SERVICE_MIGRATION_STAGING should be dropped after successful migration");
            Assert.AreEqual(0, ExecuteScalarInt("SELECT ISNULL(OBJECT_ID('dbo.SERVER_SERVICE_MAP_MIGRATION_STAGING', 'U'), 0)"),
                "Staging table dbo.SERVER_SERVICE_MAP_MIGRATION_STAGING should be dropped after successful migration");

            // Idempotency: publish again and verify row counts don't change
            PublishDacpac();

            Assert.AreEqual(3, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[Daemon]"),
                "deploy.Daemon count unchanged after repeat publish");
            Assert.AreEqual(3, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[ServerDaemon]"),
                "deploy.ServerDaemon count unchanged after repeat publish");
            Assert.AreEqual(5, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[RefDataAuditAction]"),
                "RefDataAuditAction seed idempotent across repeat publish");
        }

        [TestMethod]
        [TestCategory("Database")]
        public void SC05b_SeedIdempotency_WorksOnEmptyAndPreSeeded()
        {
            PublishDacpac();

            // Initial post-deploy seed populated 5 rows
            Assert.AreEqual(5, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[RefDataAuditAction]"));

            var seedSql = ReadFixture("DatabaseTests/Fixtures/SeedSnippet.sql");

            // Run seed a second time against a pre-seeded table — should be no-op
            ExecuteEphemeral(seedSql);
            Assert.AreEqual(5, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[RefDataAuditAction]"),
                "Re-running seed on pre-seeded table must not duplicate rows");

            // Empty the table and re-run seed — should repopulate exactly 5 rows
            // Delete from dependent audit tables first to satisfy FKs (shouldn't have any in a fresh DB)
            ExecuteEphemeral("DELETE FROM [deploy].[RefDataAuditAction]");
            Assert.AreEqual(0, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[RefDataAuditAction]"));

            ExecuteEphemeral(seedSql);
            Assert.AreEqual(5, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[RefDataAuditAction]"),
                "Seed must fully populate from an empty table");

            // All five expected actions present
            var expected = new HashSet<string> { "Create", "Update", "Delete", "Attach", "Detach" };
            var conn = new Microsoft.Data.SqlClient.SqlConnection(EphemeralConnectionString);
            conn.Open();
            using var cmd = new Microsoft.Data.SqlClient.SqlCommand(
                "SELECT [Action] FROM [deploy].[RefDataAuditAction]", conn);
            using var reader = cmd.ExecuteReader();
            var found = new HashSet<string>();
            while (reader.Read())
                found.Add(reader.GetString(0));
            CollectionAssert.AreEquivalent(expected.ToArray(), found.ToArray(),
                "Seed must produce exactly Create/Update/Delete/Attach/Detach");
            conn.Dispose();
        }

        [TestMethod]
        [TestCategory("Database")]
        public void OBS_ObservationTableCreatedAndWritable()
        {
            PublishDacpac();

            Assert.AreNotEqual(0,
                ExecuteScalarInt("SELECT ISNULL(OBJECT_ID('deploy.DaemonObservation', 'U'), 0)"),
                "deploy.DaemonObservation should exist after publish");

            // Seed dependencies: one server row and one daemon row.
            ExecuteEphemeral(@"
                INSERT INTO [dbo].[SERVER] (Server_Name) VALUES (N'obs-test-srv');
                DECLARE @sid INT = SCOPE_IDENTITY();
                SET IDENTITY_INSERT [deploy].[Daemon] ON;
                INSERT INTO [deploy].[Daemon] (Id, Name) VALUES (500, N'obs-test-daemon');
                SET IDENTITY_INSERT [deploy].[Daemon] OFF;
                INSERT INTO [deploy].[DaemonObservation] (ServerId, DaemonId, ObservedAt, ObservedStatus)
                VALUES (@sid, 500, SYSDATETIME(), N'Running');");

            Assert.AreEqual(1,
                ExecuteScalarInt(
                    "SELECT COUNT(*) FROM [deploy].[DaemonObservation] WHERE DaemonId = 500 AND ObservedStatus = 'Running'"),
                "Observation round-trip insert+read failed");

            // The two indexes and two FKs must exist.
            Assert.AreNotEqual(0,
                ExecuteScalarInt(@"SELECT COUNT(*) FROM sys.indexes
                    WHERE name = 'IX_DaemonObservation_DaemonId_ObservedAt'
                      AND object_id = OBJECT_ID('deploy.DaemonObservation')"),
                "Expected IX_DaemonObservation_DaemonId_ObservedAt index");

            Assert.AreNotEqual(0,
                ExecuteScalarInt(@"SELECT COUNT(*) FROM sys.foreign_keys
                    WHERE name IN ('FK_DaemonObservation_Server', 'FK_DaemonObservation_Daemon')"),
                "Expected two FKs on deploy.DaemonObservation");
        }
    }
}
