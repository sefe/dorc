using Dorc.ApiModel;
using Dorc.PersistentData.Security;
using Microsoft.Extensions.Configuration;

namespace Dorc.Monitor.Tests
{
    /// <summary>
    /// Every secure configuration value is decrypted and injected into every deployment whose
    /// production flag matches, with no exclusion for the credentials DOrc itself needs to
    /// operate. An estate inventory found the production deployment password reaching all 1,083
    /// non-production environments — the lowest-trust population, and the easiest place to land
    /// a component that reads it.
    ///
    /// The estate's secure keys are two classes, and that is the whole difficulty. A completed
    /// production share scan found three with zero consumers and three that scripts genuinely
    /// depend on — one of which is how scripts reach target servers at all, in roughly eighty
    /// call sites. Withholding "every secure value" would close the weakness and break the
    /// estate, so the withheld set is the evidenced three, and what remains published is
    /// reported rather than assumed.
    /// </summary>
    [TestClass]
    public class ScriptScopeConfigValuesTests
    {
        private static ScriptScopeConfigValues Default() =>
            Build(new Dictionary<string, string?>());

        private static ScriptScopeConfigValues Build(Dictionary<string, string?> settings) =>
            new(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());

        /// <summary>
        /// The three the scan showed no script consumes. Withholding them breaks nothing, which
        /// is why they are the default rather than a setting someone has to discover.
        /// </summary>
        [TestMethod]
        [DataRow("DORC_ProdDeployPassword")]
        [DataRow("DORC_WebDeployPassword")]
        [DataRow("DorcApiAccessPassword")]
        public void WithholdsTheOperatorOnlyKeysByDefault(string key)
        {
            Assert.IsTrue(Default().IsWithheld(key));
        }

        /// <summary>
        /// The three scripts genuinely depend on. Withholding these would break roughly ninety
        /// call sites, so they are published until their consumers are migrated.
        /// </summary>
        [TestMethod]
        [DataRow("DeploymentServiceAccountPassword")]
        [DataRow("ProgetAccountPassword")]
        [DataRow("DorcCliSecret")]
        public void DoesNotWithholdTheKeysScriptsDependOn(string key)
        {
            Assert.IsFalse(Default().IsWithheld(key),
                $"'{key}' has confirmed consumers; withholding it breaks deployments.");
        }

        /// <summary>
        /// Usernames are identities rather than credentials — already visible in target-server
        /// access control lists, process listings and event logs — and scripts legitimately
        /// consume them to grant logon rights.
        /// </summary>
        [TestMethod]
        [DataRow("DORC_ProdDeployUsername")]
        [DataRow("DORC_NonProdDeployUsername")]
        public void DoesNotWithholdUsernames(string key)
        {
            Assert.IsFalse(Default().IsWithheld(key));
        }

        [TestMethod]
        public void MatchesKeysWithoutRegardToCaseOrSurroundingSpace()
        {
            var values = Default();

            Assert.IsTrue(values.IsWithheld("dorc_proddeploypassword"));
            Assert.IsTrue(values.IsWithheld("  DORC_ProdDeployPassword  "));
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public void WithholdsNothingForAnAbsentKey(string? key)
        {
            Assert.IsFalse(Default().IsWithheld(key));
        }

        /// <summary>
        /// The list is configurable so that S-013 can extend it as consumers are migrated,
        /// without a code change per key.
        /// </summary>
        [TestMethod]
        public void TakesTheConfiguredListWhenOneIsGiven()
        {
            var configured = Build(new Dictionary<string, string?>
            {
                [ScriptScopeConfigValues.WithheldKeysSetting + ":0"] = "DORC_NonProdDeployPassword"
            });

            Assert.IsTrue(configured.IsWithheld("DORC_NonProdDeployPassword"));
            Assert.IsFalse(configured.IsWithheld("DORC_ProdDeployPassword"),
                "A configured list replaces the default rather than adding to it.");
        }

        /// <summary>
        /// Derived from what is actually in the estate, not from a list in code, so a secure
        /// value added after this was written is reported rather than silently overlooked.
        /// </summary>
        [TestMethod]
        public void ReportsTheSecureValuesThatStillReachScripts()
        {
            var estate = new[]
            {
                Secure("DORC_ProdDeployPassword"),
                Secure("DeploymentServiceAccountPassword"),
                Secure("SomeKeyAddedLater"),
                NotSecure("DORC_ProdDeployUsername")
            };

            CollectionAssert.AreEqual(
                new[] { "DeploymentServiceAccountPassword", "SomeKeyAddedLater" },
                Default().StillPublishedSecureKeys(estate).ToArray());
        }

        [TestMethod]
        public void ReportsNothingWhenEverySecureValueIsWithheld()
        {
            var estate = new[] { Secure("DORC_ProdDeployPassword"), NotSecure("PlainSetting") };

            Assert.AreEqual(0, Default().StillPublishedSecureKeys(estate).Count);
        }

        private static ConfigValueApiModel Secure(string key) =>
            new() { Key = key, Value = "x", Secure = true };

        private static ConfigValueApiModel NotSecure(string key) =>
            new() { Key = key, Value = "x", Secure = false };
    }
}
