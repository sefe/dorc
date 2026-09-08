using Dorc.TerraformRunner.State;

namespace Dorc.TerraformRunner.Tests.State
{
    [TestClass]
    public class TerraformBackendRendererTests
    {
        private string _tempRoot = null!;

        [TestInitialize]
        public void Setup()
        {
            _tempRoot = Path.Join(Path.GetTempPath(), "tbrt-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, true); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { /* best-effort */ }
        }

        [TestMethod]
        public void RenderAzureBlob_RequiredFields_ProducesDeterministicOutput()
        {
            var first = TerraformBackendRenderer.RenderAzureBlob(
                new TerraformBackendRenderer.AzureBlobBackend(
                    "stdorc01", "tfstate", "TEVO/sql-app/dv11.tfstate", "rg-state"));
            var second = TerraformBackendRenderer.RenderAzureBlob(
                new TerraformBackendRenderer.AzureBlobBackend(
                    "stdorc01", "tfstate", "TEVO/sql-app/dv11.tfstate", "rg-state"));

            Assert.AreEqual(first, second);
            StringAssert.Contains(first, "backend \"azurerm\"");
            StringAssert.Contains(first, "storage_account_name = \"stdorc01\"");
            StringAssert.Contains(first, "container_name       = \"tfstate\"");
            StringAssert.Contains(first, "key                  = \"TEVO/sql-app/dv11.tfstate\"");
            StringAssert.Contains(first, "resource_group_name  = \"rg-state\"");
        }

        [TestMethod]
        public void RenderAzureBlob_OmitsResourceGroupWhenAbsent()
        {
            var rendered = TerraformBackendRenderer.RenderAzureBlob(
                new TerraformBackendRenderer.AzureBlobBackend("stdorc01", "tfstate", "x.tfstate", null));

            Assert.IsFalse(rendered.Contains("resource_group_name"));
        }

        [TestMethod]
        public void RenderAzureBlob_EscapesQuotesInValues()
        {
            var rendered = TerraformBackendRenderer.RenderAzureBlob(
                new TerraformBackendRenderer.AzureBlobBackend(
                    "stdorc01", "tfstate", "k\"with-quote.tfstate", null));

            // The literal `\"` (backslash + quote) must appear in the output;
            // the raw `"` must NOT appear inside the value position.
            StringAssert.Contains(rendered, "\\\"with-quote.tfstate");
        }

        [TestMethod]
        public void RenderAzureBlob_MissingStorageAccount_Throws()
        {
            Assert.ThrowsExactly<ArgumentException>(() =>
                TerraformBackendRenderer.RenderAzureBlob(
                    new TerraformBackendRenderer.AzureBlobBackend("", "tfstate", "k.tfstate", null)));
        }

        [TestMethod]
        public void WriteToWorkingDirectory_CreatesBackendFile()
        {
            TerraformBackendRenderer.WriteToWorkingDirectory(
                _tempRoot,
                new TerraformBackendRenderer.AzureBlobBackend("stdorc01", "tfstate", "k.tfstate", null));

            var path = Path.Join(_tempRoot, TerraformBackendRenderer.BackendFileName);
            Assert.IsTrue(File.Exists(path));
            StringAssert.Contains(File.ReadAllText(path), "backend \"azurerm\"");
        }
    }
}
