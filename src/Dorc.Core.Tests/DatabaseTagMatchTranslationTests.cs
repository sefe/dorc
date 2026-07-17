using System.Reflection;
using Dorc.PersistentData;
using Dorc.PersistentData.Contexts;
using Microsoft.EntityFrameworkCore;

namespace Dorc.Core.Tests
{
    /// <summary>
    /// SC-3's EF-translation artifact (docs/database-tags, IS S-003): the delimiter-
    /// wrap tag-membership pattern must translate to SQL Server SQL. The offline
    /// context (EnsureCreated skipped) compiles the query without a connection;
    /// ToQueryString is the evidence — captured verbatim in SPEC-S-003.
    /// </summary>
    [TestClass]
    public class DatabaseTagMatchTranslationTests
    {
        private static DeploymentContext CreateContextWithoutEnsureCreated()
        {
            typeof(DeploymentContext)
                .GetField("_ensuredCreated", BindingFlags.NonPublic | BindingFlags.Static)!
                .SetValue(null, true);
            return new DeploymentContext("Server=model-test-only;Database=none;Integrated Security=true;TrustServerCertificate=true");
        }

        [TestMethod]
        public void HasTag_TranslatesToServerSideSql()
        {
            using var context = CreateContextWithoutEnsureCreated();

            var sql = context.Databases
                .Where(DatabaseTagMatch.HasTag("Endur"))
                .ToQueryString();

            // Translated server-side (LIKE/CHARINDEX over the concatenated column),
            // with the nullable column coalesced so a NULL Type matches nothing.
            StringAssert.Contains(sql, "DB_Type");
            StringAssert.Contains(sql, "COALESCE");
            Assert.IsTrue(sql.Contains("LIKE") || sql.Contains("CHARINDEX"),
                $"Expected a server-side containment operator in:\n{sql}");
        }
    }
}
