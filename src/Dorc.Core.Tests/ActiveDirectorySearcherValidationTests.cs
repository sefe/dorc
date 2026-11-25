using Dorc.Core;
using Dorc.Core.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

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

        [TestMethod]
        public void FormatSidForLdapFilter_EncodesEveryByteAsHexEscape()
        {
            var bytes = new byte[] { 0x01, 0x05, 0x00, 0xFF, 0x15 };
            Assert.AreEqual("\\01\\05\\00\\FF\\15", ActiveDirectorySearcher.FormatSidForLdapFilter(bytes));
        }

        [TestMethod]
        public void AzureEntraSearcher_GetUserData_RejectsInvalidUsernameAndAcceptsEmailAndUpnBeforeGraphCall()
        {
            var config = Substitute.For<IConfigurationSettings>();
            var searcher = new AzureEntraSearcher(config, NullLogger<AzureEntraSearcher>.Instance);

            Assert.IsTrue(AzureEntraSearcher.IsValidEntraSearchName("alice@example.com"));
            Assert.IsTrue(AzureEntraSearcher.IsValidEntraSearchName("bob.smith_123@sefe.eu (External)"));
            Assert.IsFalse(AzureEntraSearcher.IsValidEntraSearchName("invalid*user"));
            Assert.IsFalse(AzureEntraSearcher.IsValidEntraSearchName(null!));

            Assert.Throws<ArgumentException>(() => searcher.GetUserData("invalid*user"));
        }
    }
}
