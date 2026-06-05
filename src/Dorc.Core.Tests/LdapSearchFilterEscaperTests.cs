using Dorc.Core;

namespace Dorc.Core.Tests
{
    [TestClass]
    public class LdapSearchFilterEscaperTests
    {
        [TestMethod]
        public void Escape_OrdinaryAccountName_Unchanged()
        {
            Assert.AreEqual("jsmith", LdapSearchFilterEscaper.Escape("jsmith"));
            Assert.AreEqual("DOMAIN-svc_account.01", LdapSearchFilterEscaper.Escape("DOMAIN-svc_account.01"));
        }

        [TestMethod]
        public void Escape_Asterisk_IsEncoded()
        {
            Assert.AreEqual("\\2a", LdapSearchFilterEscaper.Escape("*"));
        }

        [TestMethod]
        public void Escape_Parentheses_AreEncoded()
        {
            Assert.AreEqual("\\28External\\29", LdapSearchFilterEscaper.Escape("(External)"));
        }

        [TestMethod]
        public void Escape_Backslash_IsEncoded()
        {
            Assert.AreEqual("a\\5cb", LdapSearchFilterEscaper.Escape("a\\b"));
        }

        [TestMethod]
        public void Escape_NullCharacter_IsEncoded()
        {
            Assert.AreEqual("a\\00b", LdapSearchFilterEscaper.Escape("a\0b"));
        }

        [TestMethod]
        public void Escape_InjectionPayload_NeutralisesAllMetacharacters()
        {
            // A classic LDAP filter-injection payload that would otherwise turn
            // (sAMAccountName={input}) into an always-true / structure-altering clause.
            var escaped = LdapSearchFilterEscaper.Escape("*)(objectClass=*))(|(sAMAccountName=*");

            Assert.IsFalse(escaped.Contains('*'), "Raw '*' must not survive escaping.");
            Assert.IsFalse(escaped.Contains('('), "Raw '(' must not survive escaping.");
            Assert.IsFalse(escaped.Contains(')'), "Raw ')' must not survive escaping.");
        }

        [TestMethod]
        public void Escape_NullOrEmpty_ReturnedAsIs()
        {
            Assert.AreEqual(string.Empty, LdapSearchFilterEscaper.Escape(string.Empty));
            Assert.IsNull(LdapSearchFilterEscaper.Escape(null!));
        }
    }
}
