using System.Xml.Linq;

namespace Dorc.Core.Tests
{
    [TestClass]
    public class InstallerManifestParityTests
    {
        private static readonly XNamespace WixNs = "http://schemas.microsoft.com/wix/2006/wi";
        private static readonly XNamespace IisNs = "http://schemas.microsoft.com/wix/IIsExtension";
        private static readonly XNamespace JsonNs = "http://schemas.hegsie.com/wix/JsonExtension";

        private static string GetRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Setup.Dorc")))
            {
                dir = dir.Parent;
            }
            Assert.IsNotNull(dir, "Could not locate repository root containing src\\Setup.Dorc");
            return dir.FullName;
        }

        [TestMethod]
        public void RequestApiWxs_ProdAndNonProd_HaveSymmetricJsonTransformsAndProdUserAccount()
        {
            var path = Path.Combine(GetRepoRoot(), "src", "Setup.Dorc", "Web", "RequestApi", "RequestApi.wxs");
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
            var path = Path.Combine(GetRepoRoot(), "src", "Setup.Dorc", "Web", "Orchestrator", "DOrcWeb.wxs");
            var doc = XDocument.Load(path);

            var prodPool = doc.Descendants(IisNs + "WebAppPool")
                .Single(e => (string?)e.Attribute("Id") == "DOrcWebAppPoolProd");
            Assert.AreEqual("DOrcWebServiceUserProd", (string?)prodPool.Attribute("User"),
                "DOrcWebAppPoolProd must reference DOrcWebServiceUserProd.");
        }

        [DataTestMethod]
        [DataRow("Request.wxs")]
        [DataRow("PropertyValueCreation.wxs")]
        [DataRow("PostRestoreEndur.wxs")]
        [DataRow("DeployCopyEnvBuild.wxs")]
        public void CliToolsWxs_StampAllFourDorcApiOAuthProperties(string wxsFileName)
        {
            var path = Path.Combine(GetRepoRoot(), "src", "Setup.Dorc", "Tools", wxsFileName);
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
            var path = Path.Combine(GetRepoRoot(), "src", "Setup.Acceptance", "Product.wxs");
            var doc = XDocument.Load(path);

            var elementPaths = doc.Descendants(JsonNs + "JsonFile")
                .Select(e => (string?)e.Attribute("ElementPath"))
                .ToHashSet();

            CollectionAssert.IsSubsetOf(
                new[] { "$.DorcApi.BaseUrl", "$.DorcApi.ClientId", "$.DorcApi.ClientSecret", "$.DorcApi.Scope" },
                elementPaths.ToList(),
                "Setup.Acceptance Product.wxs must stamp $.DorcApi.* settings into appsettings.test.json.");
        }

        [TestMethod]
        public void ArchiveDeploymentRequestsSql_FiltersTerminalStatusesAndCleansDependentTables()
        {
            var path = Path.Combine(GetRepoRoot(), "src", "Dorc.Database", "deploy", "Stored Procedures", "ArchiveDeploymentRequests.sql");
            var sql = File.ReadAllText(path);

            StringAssert.Contains(sql, "d.Status IN ('Completed', 'Failed', 'Cancelled', 'Errored')");
            Assert.IsFalse(sql.Contains("@MaxYearInArchive"), "ArchiveDeploymentRequests must not skip older unarchived years via @MaxYearInArchive");
            StringAssert.Contains(sql, "DELETE FROM [deploy].[PowerShellExecutionReceipt]");
            StringAssert.Contains(sql, "DELETE FROM [deploy].[TerraformDeploymentOutcome]");
            StringAssert.Contains(sql, "DELETE FROM [deploy].[TerraformDeploymentReceipt]");
            StringAssert.Contains(sql, "DELETE FROM [deploy].[TerraformPlanApproval]");
            StringAssert.Contains(sql, "DELETE FROM [deploy].[CatalogRequestInputs]");
            StringAssert.Contains(sql, "DELETE FROM [deploy].[CatalogSubmission]");
            StringAssert.Contains(sql, "DELETE FROM [deploy].[DeploymentRequestDefinition]");
        }
    }
}
