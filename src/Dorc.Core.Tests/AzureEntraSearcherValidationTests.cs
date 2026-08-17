namespace Dorc.Core.Tests
{
    /// <summary>
    /// Successor to ActiveDirectorySearcherValidationTests. The AD→Graph migration deleted
    /// ActiveDirectorySearcher, so the search-term validation it guarded now lives on
    /// AzureEntraSearcher. The LDAP-escaping cases are gone with the LDAP filter; the
    /// validation cases are transport-agnostic and are retained verbatim so the regex
    /// character-range fix cannot silently regress.
    /// </summary>
    [TestClass]
    public class AzureEntraSearcherValidationTests
    {
        [TestMethod]
        public void IsValidSearchName_AcceptsRealisticNames()
        {
            Assert.IsTrue(AzureEntraSearcher.IsValidSearchName("John O'Brien-Smith"));
            Assert.IsTrue(AzureEntraSearcher.IsValidSearchName("user_123.test"));
            Assert.IsTrue(AzureEntraSearcher.IsValidSearchName("Bob Smith (External)"));
        }

        [TestMethod]
        public void IsValidSearchName_RejectsInjectionMetacharacters()
        {
            // These all passed the old broken "'-_" range regex.
            Assert.IsFalse(AzureEntraSearcher.IsValidSearchName("*"));
            Assert.IsFalse(AzureEntraSearcher.IsValidSearchName("a)(objectClass=*"));
            Assert.IsFalse(AzureEntraSearcher.IsValidSearchName("(cn=admin)"));
            Assert.IsFalse(AzureEntraSearcher.IsValidSearchName("a\\b"));
            Assert.IsFalse(AzureEntraSearcher.IsValidSearchName(""));
            Assert.IsFalse(AzureEntraSearcher.IsValidSearchName(null));
        }
    }
}
