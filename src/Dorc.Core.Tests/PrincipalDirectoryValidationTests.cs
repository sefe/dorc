namespace Dorc.Core.Tests
{
    /// <summary>
    /// Successor to ActiveDirectorySearcherValidationTests. The AD→Graph migration deleted
    /// ActiveDirectorySearcher, so the search-term validation it guarded now lives on
    /// PrincipalDirectory. The LDAP-escaping cases are gone with the LDAP filter; the
    /// validation cases are transport-agnostic and are retained verbatim so the regex
    /// character-range fix cannot silently regress.
    /// </summary>
    [TestClass]
    public class PrincipalDirectoryValidationTests
    {
        [TestMethod]
        public void IsValidSearchName_AcceptsRealisticNames()
        {
            Assert.IsTrue(PrincipalDirectory.IsValidSearchName("John O'Brien-Smith"));
            Assert.IsTrue(PrincipalDirectory.IsValidSearchName("user_123.test"));
            Assert.IsTrue(PrincipalDirectory.IsValidSearchName("Bob Smith (External)"));
        }

        [TestMethod]
        public void IsValidSearchName_RejectsInjectionMetacharacters()
        {
            // These all passed the old broken "'-_" range regex.
            Assert.IsFalse(PrincipalDirectory.IsValidSearchName("*"));
            Assert.IsFalse(PrincipalDirectory.IsValidSearchName("a)(objectClass=*"));
            Assert.IsFalse(PrincipalDirectory.IsValidSearchName("(cn=admin)"));
            Assert.IsFalse(PrincipalDirectory.IsValidSearchName("a\\b"));
            Assert.IsFalse(PrincipalDirectory.IsValidSearchName(""));
            Assert.IsFalse(PrincipalDirectory.IsValidSearchName(null));
        }

        [TestMethod]
        public void IsValidSearchTerm_AcceptsEverythingTheControllerContractAccepts()
        {
            // DirectorySearchController's documented pattern is ^[a-zA-Z0-9-_.' ()&]+$ —
            // the searcher guard must not be narrower, or valid searches silently return
            // zero rows instead of erroring.
            Assert.IsTrue(PrincipalDirectory.IsValidSearchTerm("Smith & Co"));
            Assert.IsTrue(PrincipalDirectory.IsValidSearchTerm("Smith (Contractor)"));
            Assert.IsTrue(PrincipalDirectory.IsValidSearchTerm("O'Brien-Smith"));
            Assert.IsTrue(PrincipalDirectory.IsValidSearchTerm("user_123.test"));
        }

        [TestMethod]
        public void IsValidSearchTerm_AcceptsNonAsciiNames()
        {
            Assert.IsTrue(PrincipalDirectory.IsValidSearchTerm("Müller"));
            Assert.IsTrue(PrincipalDirectory.IsValidSearchTerm("José"));
            Assert.IsTrue(PrincipalDirectory.IsValidSearchTerm("Łukasz"));
        }

        [TestMethod]
        public void IsValidSearchTerm_RejectsStructuralMetacharacters()
        {
            Assert.IsFalse(PrincipalDirectory.IsValidSearchTerm("a\\b"));
            Assert.IsFalse(PrincipalDirectory.IsValidSearchTerm("a\"b"));
            Assert.IsFalse(PrincipalDirectory.IsValidSearchTerm("a,b"));
            Assert.IsFalse(PrincipalDirectory.IsValidSearchTerm("alice\n"));
            Assert.IsFalse(PrincipalDirectory.IsValidSearchTerm(""));
            Assert.IsFalse(PrincipalDirectory.IsValidSearchTerm(null));
        }

        [TestMethod]
        public void IsValidSearchName_RejectsTrailingNewline()
        {
            // Guards the \A..\z anchoring: with ^..$, .NET also matches immediately
            // before a trailing newline, letting "alice\n" reach the OData filter.
            Assert.IsFalse(PrincipalDirectory.IsValidSearchName("alice\n"));
            Assert.IsFalse(PrincipalDirectory.IsValidSearchName("alice\nbob"));
        }
    }
}
