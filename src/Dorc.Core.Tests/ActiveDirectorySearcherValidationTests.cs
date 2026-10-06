using Dorc.Core;

namespace Dorc.Core.Tests
{
    [TestClass]
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
            Assert.IsFalse(ActiveDirectorySearcher.IsValidSearchName(null));
        }

        [TestMethod]
        public void EscapeLdapFilter_EscapesMetacharacters()
        {
            Assert.AreEqual("\\2a", ActiveDirectorySearcher.EscapeLdapFilter("*"));
            Assert.AreEqual("\\28cn=x\\29", ActiveDirectorySearcher.EscapeLdapFilter("(cn=x)"));
            Assert.AreEqual("a\\5cb", ActiveDirectorySearcher.EscapeLdapFilter("a\\b"));
        }
    }
}
