using System.Reflection;
using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Dorc.Core.Tests
{
    /// <summary>
    /// Pins where the two reference-data entities live and what their columns are
    /// called, plus the tag-column widths.
    ///
    /// EF and the SSDT project are two independent descriptions of the same tables,
    /// so a disagreement between them only surfaces at runtime against a real
    /// database. These assertions turn one class of disagreement — schema, table and
    /// column NAMES, plus the two tag widths — into a build-time failure.
    ///
    /// Deliberately NOT asserted, because EF and SSDT already disagree and
    /// reconciling them is separate work: the non-tag max lengths (EF says 50/32
    /// where the columns are NVARCHAR(250)) and the uniqueness and filter on
    /// IX_Database_ServerName_Name (EF declares both, the table declares neither).
    /// The project uses SSDT rather than EF migrations, so the SSDT side is
    /// authoritative and these divergences are latent rather than active.
    ///
    /// Builds the model offline — the constructor's EnsureCreated call is skipped by
    /// pre-setting its private static once-flag, so no database is touched.
    /// </summary>
    [TestClass]
    public class ReferenceDataSchemaMappingTests
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
            // field, as confirmed by domain review — and
            // stays at its original width. Name and ServerName are the two columns of
            // IX_Database_ServerName_Name.
            Assert.AreEqual(50, entity.FindProperty(nameof(Database.ArrayName))!.GetMaxLength());
            Assert.AreEqual(50, entity.FindProperty(nameof(Database.Name))!.GetMaxLength());
            Assert.AreEqual(50, entity.FindProperty(nameof(Database.ServerName))!.GetMaxLength());
        }
    }
}
