using Dorc.Core;

namespace Dorc.Core.Tests
{
    [TestClass]
    public class SqlUserPasswordResetTests
    {
        [TestMethod]
        public void IsValidLoginName_AcceptsRealisticLoginNames()
        {
            Assert.IsTrue(SqlUserPasswordReset.IsValidLoginName("appuser"));
            Assert.IsTrue(SqlUserPasswordReset.IsValidLoginName("app_user.svc"));
            Assert.IsTrue(SqlUserPasswordReset.IsValidLoginName("Reporting (RO)"));
            Assert.IsTrue(SqlUserPasswordReset.IsValidLoginName("team-a&b"));
        }

        [TestMethod]
        public void IsValidLoginName_RejectsInjectionMetacharacters()
        {
            // Single quote would break out of the N'...' password literal.
            Assert.IsFalse(SqlUserPasswordReset.IsValidLoginName("x' WITH PASSWORD = N'y"));
            // Square brackets would break the [...] quoted identifier.
            Assert.IsFalse(SqlUserPasswordReset.IsValidLoginName("a]b"));
            Assert.IsFalse(SqlUserPasswordReset.IsValidLoginName("a[b"));
            // Statement terminator.
            Assert.IsFalse(SqlUserPasswordReset.IsValidLoginName("a;DROP LOGIN sa"));
            Assert.IsFalse(SqlUserPasswordReset.IsValidLoginName(""));
            Assert.IsFalse(SqlUserPasswordReset.IsValidLoginName(null));
        }

        [TestMethod]
        public void BuildResetLoginSql_ProducesExpectedStatementForValidName()
        {
            var sql = SqlUserPasswordReset.BuildResetLoginSql("appuser");
            Assert.AreEqual("ALTER LOGIN [appuser] WITH PASSWORD = N'appuser'", sql);
        }

        [TestMethod]
        public void BuildResetLoginSql_EscapesIdentifierAndLiteralDefensively()
        {
            // Even if a bracket somehow reached the builder, the identifier is
            // escaped (] doubled) and the literal quote is doubled.
            var sql = SqlUserPasswordReset.BuildResetLoginSql("a]b");
            Assert.AreEqual("ALTER LOGIN [a]]b] WITH PASSWORD = N'a]b'", sql);

            var sqlQuote = SqlUserPasswordReset.BuildResetLoginSql("a'b");
            Assert.AreEqual("ALTER LOGIN [a'b] WITH PASSWORD = N'a''b'", sqlQuote);
        }
    }
}
