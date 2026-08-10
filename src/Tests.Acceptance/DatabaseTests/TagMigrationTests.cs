using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Acceptance.DatabaseTests
{
    /// <summary>
    /// Executes the tag migration against a real SQL Server rather than reading the
    /// SQL it would generate.
    ///
    /// This is the only coverage that proves the reference-data move and the tag
    /// normalisation actually work: the refactorlog schema transfer and column
    /// renames, the table rebuild's row copy, the identity stash/restore pair, the
    /// casing script, and the split of the delimited columns into
    /// deploy.DatabaseTag / deploy.ServerTag. Every one of those is a thing that
    /// looks right in a generated script and can still fail on the engine.
    /// </summary>
    [TestClass]
    public class TagMigrationTests : DatabaseTestBase
    {
        [TestMethod]
        [TestCategory("Database")]
        public void LegacyDelimitedTags_MigrateToRows()
        {
            ExecuteEphemeral(ReadFixture("DatabaseTests/Fixtures/LegacyTagSeed.sql"));

            PublishDacpac();

            // The tables moved schema and kept their rows.
            Assert.AreEqual(4, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[Database]"),
                "All four legacy database rows should survive the move");
            Assert.AreEqual(0, ExecuteScalarInt("SELECT ISNULL(OBJECT_ID('dbo.[DATABASE]', 'U'), 0)"),
                "dbo.DATABASE should be gone once transferred to the deploy schema");

            // Multi-tag values become one row per tag, and the tags are the ones that
            // went in. Counting alone would pass just as happily on mangled text —
            // wrap the split in UPPER() and every count in this test still matches.
            // The BIN2 collate makes the comparison exact, since the column's own
            // collation is case-insensitive and would accept 'ENDUR' for 'Endur'.
            Assert.AreEqual(2, ExecuteScalarInt(
                "SELECT COUNT(*) FROM [deploy].[DatabaseTag] WHERE DatabaseId = 10 " +
                "AND Tag COLLATE Latin1_General_BIN2 IN (N'Endur', N'Reporting')"),
                "'Endur;Reporting' should split into exactly the tags Endur and Reporting");

            // Padding is removed, which the delimited form never managed reliably.
            Assert.AreEqual(1, ExecuteScalarInt(
                "SELECT COUNT(*) FROM [deploy].[DatabaseTag] WHERE DatabaseId = 20 " +
                "AND Tag COLLATE Latin1_General_BIN2 = N'Endur'"),
                "'  Endur  ' should arrive trimmed");
            Assert.AreEqual(0, ExecuteScalarInt(
                "SELECT COUNT(*) FROM [deploy].[DatabaseTag] WHERE DatabaseId = 20 AND DATALENGTH(Tag) <> DATALENGTH(LTRIM(RTRIM(Tag)))"),
                "No migrated tag should retain padding");

            // Duplicates and empty entries collapse; the primary key makes it structural.
            Assert.AreEqual(1, ExecuteScalarInt(
                "SELECT COUNT(*) FROM [deploy].[DatabaseTag] WHERE DatabaseId = 30"),
                "'Ops;Ops;;Ops' should collapse to a single Ops row");

            // A NULL tag column contributes nothing rather than an empty tag.
            Assert.AreEqual(0, ExecuteScalarInt(
                "SELECT COUNT(*) FROM [deploy].[DatabaseTag] WHERE DatabaseId = 40"),
                "An untagged database should have no tag rows");

            // Servers migrate the same way.
            Assert.AreEqual(2, ExecuteScalarInt(
                "SELECT COUNT(*) FROM [deploy].[ServerTag] WHERE ServerId = 100 " +
                "AND Tag COLLATE Latin1_General_BIN2 IN (N'appserver-node', N'WebServer')"),
                "'appserver-node;WebServer' should split into exactly those two tags");
            Assert.AreEqual(0, ExecuteScalarInt(
                "SELECT COUNT(*) FROM [deploy].[ServerTag] WHERE ServerId = 200"),
                "An untagged server should have no tag rows");
        }

        [TestMethod]
        [TestCategory("Database")]
        public void PopulatedChildren_KeepTheirRowsAndAreRePointedAtTheDeploySchema()
        {
            // The rebuild has to drop the incoming foreign keys from populated child
            // tables, copy the rows, and re-add the constraints against
            // deploy.Database(Id) / deploy.Server(Id). That is the step most likely to
            // abort a production publish, and it only happens when the children
            // actually hold rows.
            ExecuteEphemeral(ReadFixture("DatabaseTests/Fixtures/LegacyTagSeed.sql"));

            PublishDacpac();

            Assert.AreEqual(3, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[EnvironmentDatabase]"),
                "Environment-to-database links should survive the rebuild");
            Assert.AreEqual(2, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[EnvironmentServer]"),
                "Environment-to-server links should survive the rebuild");

            // Re-pointed at the new tables under the new names...
            Assert.AreEqual(1, ExecuteScalarInt(
                "SELECT COUNT(*) FROM sys.foreign_keys WHERE name = 'FK_EnvironmentDatabase_Database'"),
                "The database link should be re-created against deploy.Database");
            Assert.AreEqual(1, ExecuteScalarInt(
                "SELECT COUNT(*) FROM sys.foreign_keys WHERE name = 'FK_EnvironmentServer_Server'"),
                "The server link should be re-created against deploy.Server");

            // ...and trusted, not merely present. A constraint left WITH NOCHECK stops
            // the optimiser using it and stops the engine guaranteeing it, which is the
            // state a publish that fails after the re-add would leave behind.
            Assert.AreEqual(0, ExecuteScalarInt(
                "SELECT COUNT(*) FROM sys.foreign_keys WHERE is_not_trusted = 1 " +
                "AND name IN ('FK_EnvironmentDatabase_Database', 'FK_EnvironmentServer_Server', " +
                "'FK_DatabaseTag_Database', 'FK_ServerTag_Server')"),
                "Re-created foreign keys should be trusted");

            // The rebuild recreates the table from the model alone, so anything that
            // existed only in the estate is lost unless it is modelled. This index was
            // hand-created there.
            Assert.AreEqual(1, ExecuteScalarInt(
                "SELECT COUNT(*) FROM sys.indexes WHERE name = 'IX_Database_ServerName_Name' " +
                "AND object_id = OBJECT_ID('deploy.[Database]')"),
                "The server/name index should survive the rebuild");
            Assert.AreEqual(1, ExecuteScalarInt(
                "SELECT COUNT(*) FROM sys.foreign_keys WHERE name = 'FK_Database_AdGroup'"),
                "The AD group link should survive the rebuild");
            Assert.AreEqual(1, ExecuteScalarInt(
                "SELECT COUNT(*) FROM [deploy].[Database] WHERE [Id] = 10 AND [GroupId] = 1"),
                "Group membership should be carried over with the row");
        }

        [TestMethod]
        [TestCategory("Database")]
        public void MigrationIsIdempotent_AndPreservesIdentity()
        {
            ExecuteEphemeral(ReadFixture("DatabaseTests/Fixtures/LegacyTagSeed.sql"));

            PublishDacpac();
            var afterFirst = ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[DatabaseTag]");

            // Re-publishing must not duplicate tag rows or trip the data-loss guard.
            PublishDacpac();

            Assert.AreEqual(afterFirst, ExecuteScalarInt("SELECT COUNT(*) FROM [deploy].[DatabaseTag]"),
                "Re-publishing must not insert the same tags twice");

            // The rebuild copies rows under IDENTITY_INSERT, which leaves the counter at
            // MAX(Id) of the surviving rows rather than where it really stood. The
            // fixture deletes Id 500 before the publish, so the counter is at 500 while
            // the highest surviving row is 40: without the stash/restore pair the next
            // database takes 41, an Id deploy.DatabaseAudit.DatabaseId still points at,
            // and an old database's history is silently re-attributed to a new one.
            //
            // Asserting the new row's OWN Id, not MAX(Id). MAX(Id) > 40 is true whether
            // or not the reseed happened, so it would pass with the restore script
            // deleted outright.
            var newId = ExecuteScalarInt(
                "INSERT INTO [deploy].[Database] ([Name], [ServerName]) " +
                "OUTPUT INSERTED.[Id] VALUES (N'NEW_DB', N'srv-e')");

            Assert.IsTrue(newId > 500,
                $"A newly inserted database must take an Id above the pre-migration identity " +
                $"counter of 500, so it cannot collide with audit history; got {newId}");
        }

        [TestMethod]
        [TestCategory("Database")]
        public void OverLongTag_StopsTheDeployBeforeAnythingIsMigrated()
        {
            // A tag too long for deploy.ServerTag.Tag is reachable: the legacy column
            // allowed 1000 characters and the new one allows 400. The publish has to
            // refuse, and it has to refuse in pre-deployment — aborting later would
            // leave the estate migrated with empty tag tables, invisible to every tag
            // lookup, and re-publishing would hit the same tag and abort again.
            ExecuteEphemeral(ReadFixture("DatabaseTests/Fixtures/OverLongTagSeed.sql"));

            var thrown = Assert.Throws<Exception>(() => PublishDacpac(),
                "Publishing with an over-long tag should fail rather than drop the tag");
            StringAssert.Contains(thrown.ToString(), "400",
                "The failure should name the limit so an operator can act on it");

            // Nothing moved: the legacy table is still there and still in dbo.
            Assert.AreNotEqual(0, ExecuteScalarInt("SELECT ISNULL(OBJECT_ID('dbo.[SERVER]', 'U'), 0)"),
                "The legacy table should be untouched when the guard fires");
            Assert.AreEqual(0, ExecuteScalarInt("SELECT ISNULL(OBJECT_ID('deploy.[ServerTag]', 'U'), 0)"),
                "No part of the migration should have run");
        }
    }
}
