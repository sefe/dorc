using System.Reflection;
using Dorc.PersistentData;
using Dorc.PersistentData.Contexts;
using Microsoft.EntityFrameworkCore;

namespace Dorc.Core.Tests
{
    /// <summary>
    /// The EF-translation contract: the delimiter-wrap tag-membership pattern must
    /// translate to SQL Server SQL rather than falling back to client evaluation.
    /// The offline context (EnsureCreated skipped) compiles the query without a
    /// connection, and ToQueryString is the evidence.
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

            // Translated server-side as EXISTS against the join table, which seeks
            // IX_DatabaseTag_Tag. The delimited form this replaced could only be
            // matched with a leading-wildcard LIKE, which forced a scan - so the
            // absence of LIKE here is the point of the change, not an accident.
            StringAssert.Contains(sql, "[deploy].[Database]");
            StringAssert.Contains(sql, "[deploy].[DatabaseTag]");
            StringAssert.Contains(sql, "EXISTS");
            Assert.IsFalse(sql.Contains("LIKE"),
                $"Tag membership must not degrade to a LIKE scan:\n{sql}");
        }
    }
}
