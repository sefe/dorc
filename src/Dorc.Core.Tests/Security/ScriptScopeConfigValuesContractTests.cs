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
            Assert.IsFalse(sut.IsWithheld((string?)null));
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
                new ConfigValueApiModel { Key = "dorc_proddeploypassword", Secure = true, VisibleToScripts = true },
                new ConfigValueApiModel { Key = "DORC_NonProdDeployPassword", Secure = true, VisibleToScripts = true },
                new ConfigValueApiModel { Key = "dorc_nonproddeploypassword", Secure = true, VisibleToScripts = true },
                new ConfigValueApiModel { Key = "TerraformSecret", Secure = true, VisibleToScripts = true },
                new ConfigValueApiModel { Key = "HiddenVaultSecret", Secure = true, VisibleToScripts = false },
                new ConfigValueApiModel { Key = "ScriptRoot", Secure = false, VisibleToScripts = true },
                new ConfigValueApiModel { Key = "   ", Secure = true, VisibleToScripts = true }
            };

            var stillPublished = sut.StillPublishedSecureKeys(allConfigValues);

            CollectionAssert.AreEqual(
                new[] { "DORC_NonProdDeployPassword", "TerraformSecret" },
                stillPublished.ToArray());
        }

        [TestMethod]
        public void IsWithheldEvaluatesPerValueVisibilityAndReservedKeysIndependently()
        {
            var sut = new ScriptScopeConfigValues(new ConfigurationBuilder().Build());

            Assert.IsTrue(sut.IsWithheld(new ConfigValueApiModel
            {
                Key = "SomeCustomSecret",
                Secure = true,
                VisibleToScripts = false
            }));
            Assert.IsFalse(sut.IsWithheld(new ConfigValueApiModel
            {
                Key = "SomeCustomSecret",
                Secure = true,
                VisibleToScripts = true
            }));
            Assert.IsTrue(sut.IsWithheld(new ConfigValueApiModel
            {
                Key = "DORC_ProdDeployPassword",
                Secure = true,
                VisibleToScripts = true
            }));
            Assert.IsTrue(sut.IsWithheld((ConfigValueApiModel)null!));
        }
    }
}
