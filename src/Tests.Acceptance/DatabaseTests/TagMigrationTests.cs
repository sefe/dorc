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

            // Multi-tag values become one row per tag.
            Assert.AreEqual(2, ExecuteScalarInt(
                "SELECT COUNT(*) FROM [deploy].[DatabaseTag] WHERE DatabaseId = 10"),
                "'Endur;Reporting' should split into two rows");

            // Padding is removed, which the delimited form never managed reliably.
            Assert.AreEqual(1, ExecuteScalarInt(
                "SELECT COUNT(*) FROM [deploy].[DatabaseTag] WHERE DatabaseId = 20 AND Tag = N'Endur'"),
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
                "SELECT COUNT(*) FROM [deploy].[ServerTag] WHERE ServerId = 100"),
                "'appserver-node;WebServer' should split into two rows");
            Assert.AreEqual(0, ExecuteScalarInt(
                "SELECT COUNT(*) FROM [deploy].[ServerTag] WHERE ServerId = 200"),
                "An untagged server should have no tag rows");
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

            // The rebuild copies rows under IDENTITY_INSERT and does not reseed, so
            // without the stash/restore pair the next database would reuse an Id that
            // deploy.DatabaseAudit still refers to.
            ExecuteEphemeral(
                "INSERT INTO [deploy].[Database] ([Name], [ServerName]) VALUES (N'NEW_DB', N'srv-e')");

            Assert.IsTrue(
                ExecuteScalarInt("SELECT MAX([Id]) FROM [deploy].[Database]") > 40,
                "A newly inserted database must take an Id above the highest legacy one");
        }
    }
}
