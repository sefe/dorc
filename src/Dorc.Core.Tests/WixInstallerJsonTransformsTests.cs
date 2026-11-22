using System.Xml.Linq;

namespace Dorc.Core.Tests
{
    [TestClass]
    public class WixInstallerJsonTransformsTests
    {
        private static readonly XNamespace WixNs = "http://wixtoolset.org/schemas/v4/wxs";
        private static readonly XNamespace IisNs = "http://wixtoolset.org/schemas/v4/wxs/iis";
        private static readonly XNamespace JsonNs = "http://schemas.hegsie.com/wix/JsonExtension";

        private static string GetRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Join(dir.FullName, "src", "Setup.Dorc")))
            {
                dir = dir.Parent;
            }
            Assert.IsNotNull(dir, "Could not locate repository root containing src\\Setup.Dorc");
            return dir.FullName;
        }

        private static string JoinUnderRoot(string root, params string[] relativeSegments)
        {
            foreach (var segment in relativeSegments)
            {
                Assert.IsFalse(Path.IsPathRooted(segment), $"Path segment '{segment}' must be relative.");
            }
            return Path.Join(new[] { root }.Concat(relativeSegments).ToArray());
        }

        [TestMethod]
        public void RequestApiWxs_ProdAndNonProd_HaveSymmetricJsonTransformsAndProdUserAccount()
        {
            var path = JoinUnderRoot(GetRepoRoot(), "src", "Setup.Dorc", "Web", "RequestApi", "RequestApi.wxs");
            var doc = XDocument.Load(path);

            var prodPool = doc.Descendants(IisNs + "WebAppPool")
                .Single(e => (string?)e.Attribute("Id") == "DeployApiPoolProd");
            Assert.AreEqual("ApiUserProd", (string?)prodPool.Attribute("User"),
                "DeployApiPoolProd must reference ApiUserProd so Prod does not fail with duplicate IIS user.");

            var nonProdComponent = doc.Descendants(WixNs + "Component")
                .Single(e => (string?)e.Attribute("Id") == "RequestApiComponent");
            var prodComponent = doc.Descendants(WixNs + "Component")
                .Single(e => (string?)e.Attribute("Id") == "RequestApiComponentProd");

            var nonProdElementPaths = nonProdComponent.Descendants(JsonNs + "JsonFile")
                .Select(e => (string?)e.Attribute("ElementPath"))
                .OrderBy(p => p)
                .ToList();
            var prodElementPaths = prodComponent.Descendants(JsonNs + "JsonFile")
                .Select(e => (string?)e.Attribute("ElementPath"))
                .OrderBy(p => p)
                .ToList();

            CollectionAssert.AreEqual(nonProdElementPaths, prodElementPaths,
                "RequestApiComponentProd must have the exact same JsonFile ElementPath transforms as RequestApiComponent.");
        }

        [TestMethod]
        public void DOrcWebWxs_ProdAppPool_ReferencesDOrcWebServiceUserProd()
        {
            var path = JoinUnderRoot(GetRepoRoot(), "src", "Setup.Dorc", "Web", "Orchestrator", "DOrcWeb.wxs");
            var doc = XDocument.Load(path);

            var prodPool = doc.Descendants(IisNs + "WebAppPool")
                .Single(e => (string?)e.Attribute("Id") == "DOrcWebAppPoolProd");
            Assert.AreEqual("DOrcWebServiceUserProd", (string?)prodPool.Attribute("User"),
                "DOrcWebAppPoolProd must reference DOrcWebServiceUserProd.");
        }

        [TestMethod]
        [DataRow("Request.wxs")]
        [DataRow("PropertyValueCreation.wxs")]
        [DataRow("PostRestoreEndur.wxs")]
        [DataRow("DeployCopyEnvBuild.wxs")]
        public void CliToolsWxs_StampAllFourDorcApiOAuthProperties(string wxsFileName)
        {
            var path = JoinUnderRoot(GetRepoRoot(), "src", "Setup.Dorc", "Tools", wxsFileName);
            var doc = XDocument.Load(path);

            var elementPaths = doc.Descendants(JsonNs + "JsonFile")
                .Select(e => (string?)e.Attribute("ElementPath"))
                .ToHashSet();

            CollectionAssert.IsSubsetOf(
                new[] { "$.DorcApi.BaseUrl", "$.DorcApi.ClientId", "$.DorcApi.ClientSecret", "$.DorcApi.Scope" },
                elementPaths.ToList(),
                $"{wxsFileName} must stamp BaseUrl, ClientId, ClientSecret, and Scope.");
        }

        [TestMethod]
        public void SetupAcceptanceProductWxs_StampsDorcApiSettings()
        {
            var path = JoinUnderRoot(GetRepoRoot(), "src", "Setup.Acceptance", "Product.wxs");
            var doc = XDocument.Load(path);

            var elementPaths = doc.Descendants(JsonNs + "JsonFile")
                .Select(e => (string?)e.Attribute("ElementPath"))
                .ToHashSet();

            CollectionAssert.IsSubsetOf(
                new[] { "$.DorcApi.BaseUrl", "$.DorcApi.ClientId", "$.DorcApi.ClientSecret", "$.DorcApi.Scope" },
                elementPaths.ToList(),
                "Setup.Acceptance Product.wxs must stamp $.DorcApi.* settings into appsettings.test.json.");
        }
    }
}
