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

        [TestMethod]
        public void IsValidSearchTerm_AcceptsEverythingTheControllerContractAccepts()
        {
            // DirectorySearchController's documented pattern is ^[a-zA-Z0-9-_.' ()&]+$ —
            // the searcher guard must not be narrower, or valid searches silently return
            // zero rows instead of erroring.
            Assert.IsTrue(AzureEntraSearcher.IsValidSearchTerm("Smith & Co"));
            Assert.IsTrue(AzureEntraSearcher.IsValidSearchTerm("Smith (Contractor)"));
            Assert.IsTrue(AzureEntraSearcher.IsValidSearchTerm("O'Brien-Smith"));
            Assert.IsTrue(AzureEntraSearcher.IsValidSearchTerm("user_123.test"));
        }

        [TestMethod]
        public void IsValidSearchTerm_AcceptsNonAsciiNames()
        {
            Assert.IsTrue(AzureEntraSearcher.IsValidSearchTerm("Müller"));
            Assert.IsTrue(AzureEntraSearcher.IsValidSearchTerm("José"));
            Assert.IsTrue(AzureEntraSearcher.IsValidSearchTerm("Łukasz"));
        }

        [TestMethod]
        public void IsValidSearchTerm_RejectsStructuralMetacharacters()
        {
            Assert.IsFalse(AzureEntraSearcher.IsValidSearchTerm("a\\b"));
            Assert.IsFalse(AzureEntraSearcher.IsValidSearchTerm("a\"b"));
            Assert.IsFalse(AzureEntraSearcher.IsValidSearchTerm("a,b"));
            Assert.IsFalse(AzureEntraSearcher.IsValidSearchTerm("alice\n"));
            Assert.IsFalse(AzureEntraSearcher.IsValidSearchTerm(""));
            Assert.IsFalse(AzureEntraSearcher.IsValidSearchTerm(null));
        }

        [TestMethod]
        public void IsValidSearchName_RejectsTrailingNewline()
        {
            // Guards the \A..\z anchoring: with ^..$, .NET also matches immediately
            // before a trailing newline, letting "alice\n" reach the OData filter.
            Assert.IsFalse(AzureEntraSearcher.IsValidSearchName("alice\n"));
            Assert.IsFalse(AzureEntraSearcher.IsValidSearchName("alice\nbob"));
        }
    }
}
