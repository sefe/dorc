using System.Reflection;
using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Dorc.Core.Tests
{
    /// <summary>
    /// Pins how the two reference-data entities map to the database: the deploy
    /// schema and the standard column names they moved to
    /// (docs/tag-schema-standardisation), and the tag-column widths
    /// (docs/tag-capacity-expansion, IS S-002). Everything else in the Database
    /// configuration must stay at its current width.
    ///
    /// EF and the SSDT project are two independent descriptions of the same tables,
    /// so a disagreement between them only ever surfaces at runtime against a real
    /// database. These assertions turn that into a build-time failure.
    ///
    /// Builds the model offline — the constructor's EnsureCreated call is skipped by
    /// pre-setting its private static once-flag, so no database is touched.
    /// </summary>
    [TestClass]
    public class DeploymentContextTagMappingTests
    {
        private static DeploymentContext CreateContextWithoutEnsureCreated()
        {
            typeof(DeploymentContext)
                .GetField("_ensuredCreated", BindingFlags.NonPublic | BindingFlags.Static)!
                .SetValue(null, true);
            return new DeploymentContext("Server=model-test-only;Database=none;Integrated Security=true;TrustServerCertificate=true");
        }

        [TestMethod]
        public void Database_MapsToTheDeploySchema()
        {
            using var context = CreateContextWithoutEnsureCreated();
            var entity = context.Model.FindEntityType(typeof(Database))!;

            Assert.AreEqual("deploy", entity.GetSchema());
            Assert.AreEqual("Database", entity.GetTableName());
        }

        [TestMethod]
        public void Server_MapsToTheDeploySchema()
        {
            using var context = CreateContextWithoutEnsureCreated();
            var entity = context.Model.FindEntityType(typeof(Server))!;

            Assert.AreEqual("deploy", entity.GetSchema());
            Assert.AreEqual("Server", entity.GetTableName());
        }

        [TestMethod]
        public void ReferenceDataColumns_UseThePropertyNamesVerbatim()
        {
            using var context = CreateContextWithoutEnsureCreated();

            // No HasColumnName remaps survive the move: the legacy DB_ID / DB_Type /
            // Application_Server_Name spellings are gone, so every column is named
            // after its property. A stray remap would silently reintroduce the
            // inconsistency this migration removed.
            foreach (var type in new[] { typeof(Database), typeof(Server) })
            {
                var entity = context.Model.FindEntityType(type)!;
                var storeObject = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());

                foreach (var property in entity.GetProperties())
                {
                    Assert.AreEqual(property.Name, property.GetColumnName(storeObject),
                        $"{type.Name}.{property.Name} must map to a column of the same name");
                }
            }
        }

        [TestMethod]
        public void ServerTags_AreWidenedToTheLimit()
        {
            using var context = CreateContextWithoutEnsureCreated();

            Assert.AreEqual(TagLimits.MaxTagStringLength,
                context.Model.FindEntityType(typeof(Server))!.FindProperty(nameof(Server.Tags))!.GetMaxLength());
        }

        [TestMethod]
        public void DatabaseTags_AreWidenedToTheLimit()
        {
            using var context = CreateContextWithoutEnsureCreated();

            Assert.AreEqual(TagLimits.MaxTagStringLength,
                context.Model.FindEntityType(typeof(Database))!.FindProperty(nameof(Database.Tags))!.GetMaxLength());
        }

        [TestMethod]
        public void DatabaseFields_KeepTheirCurrentWidths()
        {
            using var context = CreateContextWithoutEnsureCreated();
            var entity = context.Model.FindEntityType(typeof(Database))!;

            // ArrayName is the storage array the source database sits on — NOT a tag
            // field (correction recorded in the HLPS after user domain review) — and
            // stays at its original width. Name and ServerName sit under the unique
            // filtered index IX_Database_ServerName_Name.
            Assert.AreEqual(50, entity.FindProperty(nameof(Database.ArrayName))!.GetMaxLength());
            Assert.AreEqual(50, entity.FindProperty(nameof(Database.Name))!.GetMaxLength());
            Assert.AreEqual(50, entity.FindProperty(nameof(Database.ServerName))!.GetMaxLength());
        }
    }
}
