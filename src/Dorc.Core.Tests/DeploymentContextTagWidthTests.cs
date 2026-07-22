using System.Reflection;
using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;

namespace Dorc.Core.Tests
{
    /// <summary>
    /// SC-2's EF-layer check (docs/tag-capacity-expansion, IS S-002): the model's
    /// declared max lengths must equal the tag limit for the widened properties and
    /// stay at their current widths for everything else in the Database configuration.
    /// Builds the model offline — the constructor's EnsureCreated call is skipped by
    /// pre-setting its private static once-flag, so no database is touched.
    /// </summary>
    [TestClass]
    public class DeploymentContextTagWidthTests
    {
        private static DeploymentContext CreateContextWithoutEnsureCreated()
        {
            typeof(DeploymentContext)
                .GetField("_ensuredCreated", BindingFlags.NonPublic | BindingFlags.Static)!
                .SetValue(null, true);
            return new DeploymentContext("Server=model-test-only;Database=none;Integrated Security=true;TrustServerCertificate=true");
        }

        [TestMethod]
        public void ServerTags_AreWidenedToTheLimit()
        {
            using var context = CreateContextWithoutEnsureCreated();

            Assert.AreEqual(TagLimits.MaxTagStringLength,
                context.Model.FindEntityType(typeof(Server))!.FindProperty(nameof(Server.ApplicationTags))!.GetMaxLength());
        }

        [TestMethod]
        public void DatabaseTags_AreWidenedToTheLimit()
        {
            // DB_Type is the database tags column (docs/database-tags, IS S-002).
            using var context = CreateContextWithoutEnsureCreated();

            Assert.AreEqual(TagLimits.MaxTagStringLength,
                context.Model.FindEntityType(typeof(Database))!.FindProperty(nameof(Database.Type))!.GetMaxLength());
        }

        [TestMethod]
        public void DatabaseFields_KeepTheirCurrentWidths()
        {
            using var context = CreateContextWithoutEnsureCreated();
            var entity = context.Model.FindEntityType(typeof(Database))!;

            // ArrayName is the storage array the source database sits on — NOT a tag
            // field (correction recorded in the HLPS after user domain review) — and
            // stays at its original width. Name and ServerName sit under the unique
            // filtered index IX_DATABASE_Server_Name_DB_Name.
            Assert.AreEqual(50, entity.FindProperty(nameof(Database.ArrayName))!.GetMaxLength());
            Assert.AreEqual(50, entity.FindProperty(nameof(Database.Name))!.GetMaxLength());
            Assert.AreEqual(50, entity.FindProperty(nameof(Database.ServerName))!.GetMaxLength());
        }
    }
}
