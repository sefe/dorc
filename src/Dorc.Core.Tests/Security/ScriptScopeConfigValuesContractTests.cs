using System.Text;
using Dorc.ApiModel;
using Dorc.PersistentData.Security;
using Microsoft.Extensions.Configuration;

namespace Dorc.Core.Tests.Security
{
    [TestClass]
    public class ScriptScopeConfigValuesContractTests
    {
        [TestMethod]
        public void JsonArrayConfigurationExtendsBuiltInDefaultSetAndTrimsEntries()
        {
            const string json = """
                {
                  "AppSettings": {
                    "ConfigKeysWithheldFromScripts": [
                      "  DORC_ProdDeployPassword  ",
                      "",
                      "   ",
                      "CustomVaultToken"
                    ]
                  }
                }
                """;
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();

            var sut = new ScriptScopeConfigValues(configuration);

            Assert.IsTrue(sut.IsWithheld("dorc_proddeploypassword"));
            Assert.IsTrue(sut.IsWithheld("dorc_webdeploypassword"));
            Assert.IsTrue(sut.IsWithheld("DorcApiAccessPassword"));
            Assert.IsTrue(sut.IsWithheld("CUSTOMVAULTTOKEN"));
            Assert.IsFalse(sut.IsWithheld(null));
            Assert.IsFalse(sut.IsWithheld("   "));
        }

        [TestMethod]
        [DataRow("[]")]
        [DataRow("[\"\"]")]
        [DataRow("[\"   \"]")]
        public void EmptyOrWhitespaceJsonArrayPreservesAllBuiltInOperatorKeys(string arrayJson)
        {
            var json = $$"""
                {
                  "AppSettings": {
                    "ConfigKeysWithheldFromScripts": {{arrayJson}}
                  }
                }
                """;
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();

            var sut = new ScriptScopeConfigValues(configuration);

            Assert.IsTrue(sut.IsWithheld("DORC_ProdDeployPassword"));
            Assert.IsTrue(sut.IsWithheld("DORC_WebDeployPassword"));
            Assert.IsTrue(sut.IsWithheld("DorcApiAccessPassword"));
        }

        [TestMethod]
        public void StillPublishedSecureKeysDeduplicatesCaseInsensitivelyAndExcludesWithheld()
        {
            var sut = new ScriptScopeConfigValues(new ConfigurationBuilder().Build());
            var allConfigValues = new[]
            {
                new ConfigValueApiModel { Key = "dorc_proddeploypassword", Secure = true },
                new ConfigValueApiModel { Key = "DORC_NonProdDeployPassword", Secure = true },
                new ConfigValueApiModel { Key = "dorc_nonproddeploypassword", Secure = true },
                new ConfigValueApiModel { Key = "TerraformSecret", Secure = true },
                new ConfigValueApiModel { Key = "ScriptRoot", Secure = false },
                new ConfigValueApiModel { Key = "   ", Secure = true }
            };

            var stillPublished = sut.StillPublishedSecureKeys(allConfigValues);

            CollectionAssert.AreEqual(
                new[] { "DORC_NonProdDeployPassword", "TerraformSecret" },
                stillPublished.ToArray());
        }
    }
}
