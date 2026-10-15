using System.IO.Compression;

namespace Dorc.TerraformRunner.Tests
{
    [TestClass]
    public class TerraformSourceArchiveTests
    {
        private string _workDir = string.Empty;
        private string _archivePath = string.Empty;

        [TestInitialize]
        public void Setup()
        {
            _workDir = Path.Join(Path.GetTempPath(), $"tf-archive-test-{Guid.NewGuid()}");
            Directory.CreateDirectory(_workDir);
            _archivePath = Path.Join(Path.GetTempPath(), $"tf-archive-test-{Guid.NewGuid()}.zip");
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_workDir)) Directory.Delete(_workDir, true);
            if (File.Exists(_archivePath)) File.Delete(_archivePath);
        }

        private void WriteFile(string relativePath, string content)
        {
            var full = Path.Join(_workDir, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }

        private Dictionary<string, string> ReadArchive()
        {
            using var zip = ZipFile.OpenRead(_archivePath);
            return zip.Entries.ToDictionary(
                e => e.FullName,
                e => { using var r = new StreamReader(e.Open()); return r.ReadToEnd(); });
        }

        [TestMethod]
        public void Create_IncludesConfigurationAndProvenance_ExcludesGitTerraformAndState()
        {
            WriteFile("main.tf", "resource \"x\" \"y\" {}");
            WriteFile("modules/sub/variables.tf", "variable \"v\" {}");
            WriteFile(".terraform.lock.hcl", "provider lock");
            WriteFile(".git/config", "[core]");
            WriteFile(".terraform/providers/provider.bin", "binary");
            WriteFile("terraform.tfstate", "{}");
            WriteFile("terraform.tfstate.backup", "{}");

            var count = TerraformSourceArchive.Create(_workDir, _archivePath, "{\"a\":1}", s => s);

            var entries = ReadArchive();
            Assert.IsTrue(entries.ContainsKey(TerraformSourceArchive.ProvenanceEntryName));
            Assert.AreEqual("{\"a\":1}", entries[TerraformSourceArchive.ProvenanceEntryName]);
            Assert.IsTrue(entries.ContainsKey("main.tf"));
            Assert.IsTrue(entries.ContainsKey("modules/sub/variables.tf"));
            Assert.IsTrue(entries.ContainsKey(".terraform.lock.hcl"));
            Assert.IsFalse(entries.Keys.Any(k => k.StartsWith(".git/")));
            Assert.IsFalse(entries.Keys.Any(k => k.StartsWith(".terraform/")));
            Assert.IsFalse(entries.Keys.Any(k => k.Contains("tfstate")));
            Assert.AreEqual(entries.Count, count);
        }

        [TestMethod]
        public void Create_RedactsTfvarsContent_LeavesOtherFilesVerbatim()
        {
            WriteFile("terraform.tfvars", "password = \"s3cret\"\nname = \"app\"");
            WriteFile("extra.tfvars.json", "{\"password\":\"s3cret\"}");
            WriteFile("main.tf", "# s3cret is not redacted here by the archive itself");

            TerraformSourceArchive.Create(
                _workDir, _archivePath, "{}",
                s => s.Replace("s3cret", "[REDACTED]"));

            var entries = ReadArchive();
            StringAssert.Contains(entries["terraform.tfvars"], "[REDACTED]");
            Assert.IsFalse(entries["terraform.tfvars"].Contains("s3cret"));
            StringAssert.Contains(entries["extra.tfvars.json"], "[REDACTED]");
            // Non-tfvars files are copied verbatim; redaction targets the
            // rendered variables file where resolved properties live.
            StringAssert.Contains(entries["main.tf"], "s3cret");
        }
    }
}
