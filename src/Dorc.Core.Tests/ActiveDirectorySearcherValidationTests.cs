using System.DirectoryServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Dorc.Core;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Dorc.Core.Tests
{
    [TestClass]
    [SupportedOSPlatform("windows")]
    public class ActiveDirectorySearcherValidationTests
    {
        [TestMethod]
        public void IsValidSearchName_AcceptsRealisticNames()
        {
            Assert.IsTrue(ActiveDirectorySearcher.IsValidSearchName("John O'Brien-Smith"));
            Assert.IsTrue(ActiveDirectorySearcher.IsValidSearchName("user_123.test"));
            Assert.IsTrue(ActiveDirectorySearcher.IsValidSearchName("Bob Smith (External)"));
        }

        [TestMethod]
        public void IsValidSearchName_RejectsLdapMetacharacters()
        {
            // These all passed the old broken "'-_" range regex.
            Assert.IsFalse(ActiveDirectorySearcher.IsValidSearchName("*"));
            Assert.IsFalse(ActiveDirectorySearcher.IsValidSearchName("a)(objectClass=*"));
            Assert.IsFalse(ActiveDirectorySearcher.IsValidSearchName("(cn=admin)"));
            Assert.IsFalse(ActiveDirectorySearcher.IsValidSearchName("a\\b"));
            Assert.IsFalse(ActiveDirectorySearcher.IsValidSearchName(""));
            Assert.IsFalse(ActiveDirectorySearcher.IsValidSearchName(null!));
        }

        [TestMethod]
        public void LdapFilterEncode_EscapesMetacharacters()
        {
            Assert.AreEqual("\\2a", ActiveDirectorySearcher.LdapFilterEncode("*"));
            Assert.AreEqual("\\28cn=x\\29", ActiveDirectorySearcher.LdapFilterEncode("(cn=x)"));
            Assert.AreEqual("a\\5cb", ActiveDirectorySearcher.LdapFilterEncode("a\\b"));
            Assert.AreEqual("\\5c\\2a\\28\\29\\00\\2f", ActiveDirectorySearcher.LdapFilterEncode("\\*()\0/"));
        }

        [TestMethod]
        public void LdapFilterEncode_PreventsFilterBreakoutAndInjectedEscapes()
        {
            Assert.AreEqual("a\\29\\28objectClass=\\2a\\29", ActiveDirectorySearcher.LdapFilterEncode("a)(objectClass=*)"));
            Assert.AreEqual("\\5c2a", ActiveDirectorySearcher.LdapFilterEncode("\\2a"));
        }

        [TestMethod]
        public void LdapFilterEncode_PreservesNameText()
        {
            Assert.AreEqual("John O'Brien-Smith", ActiveDirectorySearcher.LdapFilterEncode("John O'Brien-Smith"));
            Assert.AreEqual("user_123.test", ActiveDirectorySearcher.LdapFilterEncode("user_123.test"));
            Assert.AreEqual("Bob Smith \\28External\\29", ActiveDirectorySearcher.LdapFilterEncode("Bob Smith (External)"));
            Assert.AreEqual("", ActiveDirectorySearcher.LdapFilterEncode(""));
        }

        [TestMethod]
        public void EncodeLdapBytesAndIsActive_HandleBinarySidsGroupsAndDisabledUsers()
        {
            Assert.AreEqual("\\01\\05\\00\\FF", ActiveDirectorySearcher.EncodeLdapBytes([0x01, 0x05, 0x00, 0xFF]));

            // Groups lack userAccountControl (null) -> treated as active; enabled/disabled user flags -> respected
            Assert.IsTrue(ActiveDirectorySearcher.IsActiveUserAccountControl(null));
            Assert.IsTrue(ActiveDirectorySearcher.IsActiveUserAccountControl(512));
            Assert.IsFalse(ActiveDirectorySearcher.IsActiveUserAccountControl(514));

            using var localGroup = new DirectoryEntry("WinNT://127.0.0.1/Administrators,group");
            Assert.IsTrue(ActiveDirectorySearcher.IsActive(localGroup));
        }

        [TestMethod]
        public void SearchAndLookupMethods_BuildEncodedFiltersAgainstConfiguredRootBeforeBind()
        {
            using var unreachableRoot = new DirectoryEntry(
                "LDAP://127.0.0.1:1/DC=invalid,DC=local",
                null,
                null,
                AuthenticationTypes.Anonymous);
            var searcher = new ActiveDirectorySearcher(unreachableRoot, Substitute.For<ILogger<ActiveDirectorySearcher>>());

            Assert.ThrowsExactly<COMException>(() => searcher.Search("Bob (External)"));
            Assert.ThrowsExactly<COMException>(() => searcher.GetUserDataById("S-1-5-18"));
            Assert.ThrowsExactly<COMException>(() => searcher.GetUserData("Bob Smith (External)"));
            Assert.ThrowsExactly<COMException>(() => searcher.GetSidsForUser("bob_smith"));
        }
    }
}
