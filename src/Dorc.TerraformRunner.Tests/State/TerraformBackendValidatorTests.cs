using Dorc.TerraformRunner.State;

namespace Dorc.TerraformRunner.Tests.State
{
    [TestClass]
    public class TerraformBackendValidatorTests
    {
        private string _tempRoot = null!;

        [TestInitialize]
        public void Setup()
        {
            _tempRoot = Path.Join(Path.GetTempPath(), "tbvt-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, true); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { /* best-effort */ }
        }

        [TestMethod]
        public void Scan_CleanWorkingDir_ReturnsNoFindings()
        {
            File.WriteAllText(Path.Join(_tempRoot, "main.tf"),
                "resource \"azurerm_storage_account\" \"x\" { name = \"a\" }");

            var findings = TerraformBackendValidator.Scan(_tempRoot);

            Assert.AreEqual(0, findings.Count);
        }

        [TestMethod]
        public void Scan_BackendBlockPresent_FindingReportedWithLineNumber()
        {
            File.WriteAllText(Path.Join(_tempRoot, "providers.tf"),
                "terraform {\n" +
                "  backend \"azurerm\" {\n" +
                "    storage_account_name = \"foo\"\n" +
                "  }\n" +
                "}");

            var findings = TerraformBackendValidator.Scan(_tempRoot);

            Assert.AreEqual(1, findings.Count);
            Assert.AreEqual(1, findings[0].LineNumber);
            StringAssert.EndsWith(findings[0].FilePath, "providers.tf");
        }

        [TestMethod]
        public void Scan_RequiredVersionInTerraformBlock_NotFlagged()
        {
            // A terraform { required_version = ... } block without backend
            // must NOT trigger the validator.
            File.WriteAllText(Path.Join(_tempRoot, "versions.tf"),
                "terraform {\n  required_version = \">= 1.5.0\"\n}");

            var findings = TerraformBackendValidator.Scan(_tempRoot);

            Assert.AreEqual(0, findings.Count);
        }

        [TestMethod]
        public void Scan_BackendAfterRequiredProviders_StillFlagged()
        {
            // Regression: the original regex used [^}]* which stopped at the
            // first inner `}` (e.g. closing required_providers) and silently
            // missed the subsequent backend block. The brace-balanced scanner
            // must catch this case.
            File.WriteAllText(Path.Join(_tempRoot, "versions.tf"),
                "terraform {\n" +
                "  required_providers {\n" +
                "    azurerm = { source = \"hashicorp/azurerm\" version = \"~> 3.100\" }\n" +
                "  }\n" +
                "  backend \"azurerm\" {\n" +
                "    storage_account_name = \"x\"\n" +
                "  }\n" +
                "}\n");

            var findings = TerraformBackendValidator.Scan(_tempRoot);

            Assert.AreEqual(1, findings.Count);
            Assert.AreEqual(5, findings[0].LineNumber);
        }

        [TestMethod]
        public void Scan_BackendInSecondTerraformBlock_Flagged()
        {
            // If a file declares two terraform { } blocks (legal HCL), a
            // backend in the second must still be detected.
            File.WriteAllText(Path.Join(_tempRoot, "versions.tf"),
                "terraform {\n  required_version = \">= 1.5.0\"\n}\n\n" +
                "terraform {\n  backend \"azurerm\" {}\n}\n");

            var findings = TerraformBackendValidator.Scan(_tempRoot);

            Assert.AreEqual(1, findings.Count);
        }

        [TestMethod]
        public void Scan_PlatformBackendFile_Skipped()
        {
            // The platform-rendered _dorc_backend.tf must not be re-flagged
            // on a re-scan of the working dir.
            File.WriteAllText(
                Path.Join(_tempRoot, TerraformBackendRenderer.BackendFileName),
                "terraform { backend \"azurerm\" { storage_account_name = \"x\" } }");

            var findings = TerraformBackendValidator.Scan(_tempRoot);

            Assert.AreEqual(0, findings.Count);
        }

        [TestMethod]
        public void RejectIfUserBackendBlocksPresent_Clean_DoesNotThrow()
        {
            File.WriteAllText(Path.Join(_tempRoot, "main.tf"),
                "variable \"x\" { type = string }");

            TerraformBackendValidator.RejectIfUserBackendBlocksPresent(_tempRoot);
        }

        [TestMethod]
        public void RejectIfUserBackendBlocksPresent_BlockPresent_Throws()
        {
            File.WriteAllText(Path.Join(_tempRoot, "main.tf"),
                "terraform { backend \"local\" { path = \"./local.tfstate\" } }");

            var ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
                TerraformBackendValidator.RejectIfUserBackendBlocksPresent(_tempRoot));
            StringAssert.Contains(ex.Message, "user-checked-in");
            StringAssert.Contains(ex.Message, "main.tf");
        }
    }
}
