using System.Reflection;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;

namespace Dorc.Core.Tests
{
    /// <summary>
    /// Builds the EF model without touching a database, so configuration errors
    /// (bad navigations, missing keys, join-table mismatches) fail here instead of
    /// at first runtime query. Added for IS step S-002 (env-details-component-tabs).
    /// </summary>
    [TestClass]
    public class DeploymentContextModelTests
    {
        private static DeploymentContext CreateContextWithoutEnsureCreated()
        {
            // The constructor runs Database.EnsureCreated() exactly once per process,
            // guarded by a private static flag; pre-setting it keeps this test offline.
            typeof(DeploymentContext)
                .GetField("_ensuredCreated", BindingFlags.NonPublic | BindingFlags.Static)!
                .SetValue(null, true);
            return new DeploymentContext("Server=model-test-only;Database=none;Integrated Security=true;TrustServerCertificate=true");
        }

        [TestMethod]
        public void Model_Builds_WithNewComponentEntities()
        {
            using var context = CreateContextWithoutEnsureCreated();

            var model = context.Model;

            foreach (var (clrType, table, schema, keyProps) in new (Type, string, string, string[])[]
            {
                (typeof(Container), "Container", "deploy", new[] { "Id" }),
                (typeof(CloudResource), "CloudResource", "deploy", new[] { "Id" }),
                (typeof(ApiRegistration), "ApiRegistration", "deploy", new[] { "Id" }),
                (typeof(ContainerAudit), "ContainerAudit", "deploy", new[] { "Id" }),
                (typeof(CloudResourceAudit), "CloudResourceAudit", "deploy", new[] { "Id" }),
                (typeof(ApiRegistrationAudit), "ApiRegistrationAudit", "deploy", new[] { "Id" })
            })
            {
                var entity = model.FindEntityType(clrType);
                Assert.IsNotNull(entity, $"{clrType.Name} not in model");
                Assert.AreEqual(table, entity.GetTableName());
                Assert.AreEqual(schema, entity.GetSchema());
                CollectionAssert.AreEqual(keyProps,
                    entity.FindPrimaryKey()!.Properties.Select(p => p.Name).ToArray());
            }
        }

        [TestMethod]
        public void Model_JoinTables_HaveCompositeKeys()
        {
            using var context = CreateContextWithoutEnsureCreated();

            var model = context.Model;

            foreach (var (joinTable, keyColumns) in new (string, string[])[]
            {
                ("EnvironmentContainer", new[] { "EnvId", "ContainerId" }),
                ("EnvironmentCloudResource", new[] { "EnvId", "CloudResourceId" }),
                ("EnvironmentApiRegistration", new[] { "EnvId", "ApiRegistrationId" })
            })
            {
                var join = model.GetEntityTypes()
                    .SingleOrDefault(e => e.GetTableName() == joinTable && e.GetSchema() == "deploy");
                Assert.IsNotNull(join, $"join entity for {joinTable} not in model");
                CollectionAssert.AreEqual(keyColumns,
                    join.FindPrimaryKey()!.Properties.Select(p => p.Name).ToArray(),
                    $"composite PK mismatch on {joinTable}");
            }
        }
    }
}
