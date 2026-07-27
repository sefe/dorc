using Dorc.TerraformRunner.State;

namespace Dorc.TerraformRunner.Tests.State
{
    [TestClass]
    public class TerraformProviderRendererTests
    {
        private string _tempRoot = null!;

        [TestInitialize]
        public void Setup()
        {
            _tempRoot = Path.Join(Path.GetTempPath(), "tprt-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, true); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { /* best-effort */ }
        }

        private string ProviderFilePath => Path.Join(_tempRoot, TerraformProviderRenderer.ProviderFileName);

        [TestMethod]
        public void WriteAzureRmIfRequired_ModuleRequiresAzureRmWithoutProviderBlock_RendersFeaturesBlock()
        {
            // The stock-module layout: versions.tf declares the requirement,
            // no file configures the provider.
            File.WriteAllText(Path.Join(_tempRoot, "versions.tf"),
                "terraform {\n" +
                "  required_providers {\n" +
                "    azurerm = {\n" +
                "      source  = \"hashicorp/azurerm\"\n" +
                "      version = \"~> 3.100\"\n" +
                "    }\n" +
                "  }\n" +
                "}\n");
            File.WriteAllText(Path.Join(_tempRoot, "main.tf"),
                "resource \"azurerm_storage_account\" \"x\" { name = \"a\" }");

            TerraformProviderRenderer.WriteAzureRmIfRequired(_tempRoot);

            Assert.IsTrue(File.Exists(ProviderFilePath));
            var content = File.ReadAllText(ProviderFilePath);
            StringAssert.Contains(content, "provider \"azurerm\"");
            StringAssert.Contains(content, "features {}");
        }

        [TestMethod]
        public void WriteAzureRmIfRequired_ModuleSuppliesOwnProviderBlock_NoFileWritten()
        {
            File.WriteAllText(Path.Join(_tempRoot, "providers.tf"),
                "provider \"azurerm\" {\n  features {}\n  subscription_id = \"abc\"\n}");
            File.WriteAllText(Path.Join(_tempRoot, "versions.tf"),
                "terraform { required_providers { azurerm = { source = \"hashicorp/azurerm\" } } }");

            TerraformProviderRenderer.WriteAzureRmIfRequired(_tempRoot);

            Assert.IsFalse(File.Exists(ProviderFilePath));
        }

        [TestMethod]
        public void WriteAzureRmIfRequired_ModuleHasOnlyAliasedProviderBlock_RendersDefault()
        {
            // An aliased provider block (permitted by the module contract for
            // cross-scope resources) is NOT a default provider configuration,
            // so the default must still be rendered or terraform plan fails
            // for resources using the default provider.
            File.WriteAllText(Path.Join(_tempRoot, "providers.tf"),
                "provider \"azurerm\" {\n  alias    = \"hub\"\n  features {}\n}\n");
            File.WriteAllText(Path.Join(_tempRoot, "versions.tf"),
                "terraform { required_providers { azurerm = { source = \"hashicorp/azurerm\" } } }");

            TerraformProviderRenderer.WriteAzureRmIfRequired(_tempRoot);

            Assert.IsTrue(File.Exists(ProviderFilePath),
                "an aliased-only provider block must not suppress the default provider render");
            StringAssert.Contains(File.ReadAllText(ProviderFilePath), "features {}");
        }

        [TestMethod]
        public void WriteAzureRmIfRequired_ModuleHasDefaultAndAliasedBlocks_NoFileWritten()
        {
            // A default block plus an aliased sibling: the default is already
            // configured, so nothing should be rendered.
            File.WriteAllText(Path.Join(_tempRoot, "providers.tf"),
                "provider \"azurerm\" {\n  features {}\n}\n\nprovider \"azurerm\" {\n  alias    = \"hub\"\n  features {}\n}\n");
            File.WriteAllText(Path.Join(_tempRoot, "versions.tf"),
                "terraform { required_providers { azurerm = { source = \"hashicorp/azurerm\" } } }");

            TerraformProviderRenderer.WriteAzureRmIfRequired(_tempRoot);

            Assert.IsFalse(File.Exists(ProviderFilePath));
        }

        [TestMethod]
        public void WriteAzureRmIfRequired_ModuleDoesNotUseAzureRm_NoFileWritten()
        {
            File.WriteAllText(Path.Join(_tempRoot, "versions.tf"),
                "terraform { required_providers { random = { source = \"hashicorp/random\" } } }");

            TerraformProviderRenderer.WriteAzureRmIfRequired(_tempRoot);

            Assert.IsFalse(File.Exists(ProviderFilePath));
        }

        [TestMethod]
        public void WriteAzureRmIfRequired_RequirementInNestedDirectoryOnly_NoFileWritten()
        {
            // Provider configuration is a root-module concern; a requirement
            // declared only in a nested example must not trigger rendering.
            var nested = Path.Join(_tempRoot, "examples", "basic");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Join(nested, "main.tf"),
                "terraform { required_providers { azurerm = { source = \"hashicorp/azurerm\" } } }");
            File.WriteAllText(Path.Join(_tempRoot, "main.tf"),
                "variable \"x\" { type = string }");

            TerraformProviderRenderer.WriteAzureRmIfRequired(_tempRoot);

            Assert.IsFalse(File.Exists(ProviderFilePath));
        }

        [TestMethod]
        public void WriteAzureRmIfRequired_PathWithParentSegments_Throws()
        {
            Assert.ThrowsExactly<ArgumentException>(() =>
                TerraformProviderRenderer.WriteAzureRmIfRequired(Path.Join(_tempRoot, "..", "escape")));
        }
    }
}
