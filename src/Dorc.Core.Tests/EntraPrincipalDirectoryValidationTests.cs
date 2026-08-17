namespace Dorc.Core.Tests
{
    /// <summary>
    /// Successor to ActiveDirectorySearcherValidationTests. The AD→Graph migration deleted
    /// ActiveDirectorySearcher, so the search-term validation it guarded now lives on
    /// EntraPrincipalDirectory. The LDAP-escaping cases are gone with the LDAP filter; the
    /// validation cases are transport-agnostic and are retained verbatim so the regex
    /// character-range fix cannot silently regress.
    /// </summary>
    [TestClass]
    public class EntraPrincipalDirectoryValidationTests
    {
        [TestMethod]
        public void IsValidSearchName_AcceptsRealisticNames()
        {
            Assert.IsTrue(EntraPrincipalDirectory.IsValidSearchName("John O'Brien-Smith"));
            Assert.IsTrue(EntraPrincipalDirectory.IsValidSearchName("user_123.test"));
            Assert.IsTrue(EntraPrincipalDirectory.IsValidSearchName("Bob Smith (External)"));
        }

        [TestMethod]
        public void IsValidSearchName_RejectsInjectionMetacharacters()
        {
            // These all passed the old broken "'-_" range regex.
            Assert.IsFalse(EntraPrincipalDirectory.IsValidSearchName("*"));
            Assert.IsFalse(EntraPrincipalDirectory.IsValidSearchName("a)(objectClass=*"));
            Assert.IsFalse(EntraPrincipalDirectory.IsValidSearchName("(cn=admin)"));
            Assert.IsFalse(EntraPrincipalDirectory.IsValidSearchName("a\\b"));
            Assert.IsFalse(EntraPrincipalDirectory.IsValidSearchName(""));
            Assert.IsFalse(EntraPrincipalDirectory.IsValidSearchName(null));
        }

        [TestMethod]
        public void IsValidSearchTerm_AcceptsEverythingTheControllerContractAccepts()
        {
            // DirectorySearchController's documented pattern is ^[a-zA-Z0-9-_.' ()&]+$ —
            // the searcher guard must not be narrower, or valid searches silently return
            // zero rows instead of erroring.
            Assert.IsTrue(EntraPrincipalDirectory.IsValidSearchTerm("Smith & Co"));
            Assert.IsTrue(EntraPrincipalDirectory.IsValidSearchTerm("Smith (Contractor)"));
            Assert.IsTrue(EntraPrincipalDirectory.IsValidSearchTerm("O'Brien-Smith"));
            Assert.IsTrue(EntraPrincipalDirectory.IsValidSearchTerm("user_123.test"));
        }

        [TestMethod]
        public void IsValidSearchTerm_AcceptsNonAsciiNames()
        {
            Assert.IsTrue(EntraPrincipalDirectory.IsValidSearchTerm("Müller"));
            Assert.IsTrue(EntraPrincipalDirectory.IsValidSearchTerm("José"));
            Assert.IsTrue(EntraPrincipalDirectory.IsValidSearchTerm("Łukasz"));
        }

        [TestMethod]
        public void IsValidSearchTerm_RejectsStructuralMetacharacters()
        {
            Assert.IsFalse(EntraPrincipalDirectory.IsValidSearchTerm("a\\b"));
            Assert.IsFalse(EntraPrincipalDirectory.IsValidSearchTerm("a\"b"));
            Assert.IsFalse(EntraPrincipalDirectory.IsValidSearchTerm("a,b"));
            Assert.IsFalse(EntraPrincipalDirectory.IsValidSearchTerm("alice\n"));
            Assert.IsFalse(EntraPrincipalDirectory.IsValidSearchTerm(""));
            Assert.IsFalse(EntraPrincipalDirectory.IsValidSearchTerm(null));
        }

        [TestMethod]
        public void IsValidSearchName_RejectsTrailingNewline()
        {
            // Guards the \A..\z anchoring: with ^..$, .NET also matches immediately
            // before a trailing newline, letting "alice\n" reach the OData filter.
            Assert.IsFalse(EntraPrincipalDirectory.IsValidSearchName("alice\n"));
            Assert.IsFalse(EntraPrincipalDirectory.IsValidSearchName("alice\nbob"));
        }
    }
}
