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
        public void TagProperties_AreWidenedToTheLimit()
        {
            using var context = CreateContextWithoutEnsureCreated();
            var model = context.Model;

            Assert.AreEqual(TagLimits.MaxTagStringLength,
                model.FindEntityType(typeof(Server))!.FindProperty(nameof(Server.ApplicationTags))!.GetMaxLength());
            Assert.AreEqual(TagLimits.MaxTagStringLength,
                model.FindEntityType(typeof(Database))!.FindProperty(nameof(Database.ArrayName))!.GetMaxLength());
        }

        [TestMethod]
        public void OtherDatabaseFields_KeepTheirCurrentWidths()
        {
            using var context = CreateContextWithoutEnsureCreated();
            var entity = context.Model.FindEntityType(typeof(Database))!;

            // Name and ServerName sit under the unique filtered index
            // IX_DATABASE_Server_Name_DB_Name — widening them would overflow the
            // 1700-byte index key limit on EnsureCreated databases.
            Assert.AreEqual(50, entity.FindProperty(nameof(Database.Name))!.GetMaxLength());
            Assert.AreEqual(50, entity.FindProperty(nameof(Database.Type))!.GetMaxLength());
            Assert.AreEqual(50, entity.FindProperty(nameof(Database.ServerName))!.GetMaxLength());
        }
    }
}
