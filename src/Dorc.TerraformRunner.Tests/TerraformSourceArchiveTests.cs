using System.IO.Compression;
using Dorc.TerraformRunner.CodeSources;

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
            _workDir = Path.Join(Directory.GetCurrentDirectory(), $"tf-archive-test-{Guid.NewGuid()}");
            Directory.CreateDirectory(_workDir);
            _archivePath = _workDir + ".zip";
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
            WriteFile(".terraform/modules/child/main.tf", "# approved child module");
            WriteFile("terraform.tfstate", "{}");
            WriteFile("terraform.tfstate.backup", "{}");

            var count = TerraformSourceArchive.Create(_workDir, _archivePath, "{\"a\":1}");

            var entries = ReadArchive();
            Assert.IsTrue(entries.ContainsKey(TerraformSourceArchive.ProvenanceEntryName));
            Assert.AreEqual("{\"a\":1}", entries[TerraformSourceArchive.ProvenanceEntryName]);
            Assert.IsTrue(entries.ContainsKey("main.tf"));
            Assert.IsTrue(entries.ContainsKey("modules/sub/variables.tf"));
            Assert.IsTrue(entries.ContainsKey(".terraform.lock.hcl"));
            Assert.IsFalse(entries.Keys.Any(k => k.StartsWith(".git/")));
            Assert.IsFalse(entries.Keys.Any(k => k.StartsWith(".terraform/providers/")));
            Assert.AreEqual("# approved child module", entries[".terraform/modules/child/main.tf"]);
            Assert.IsFalse(entries.Keys.Any(k => k.Contains("tfstate")));
            Assert.AreEqual(entries.Count, count);
        }

        [TestMethod]
        public void Create_OmitsAllVariableFilesIncludingBackupAndCaseVariants()
        {
            WriteFile("terraform.tfvars", "password = \"s3cret\"\nname = \"app\"");
            WriteFile("extra.tfvars.json", "{\"password\":\"s3cret\"}");
            WriteFile("nested/secret.TFVARS.JSON.backup", "{\"UnrelatedDatabasePassword\":\"unflagged\"}");
            WriteFile("extra.auto.tfvars.bak", "secret = \"unflagged\"");
            WriteFile("main.tf", "# s3cret is not redacted here by the archive itself");

            TerraformSourceArchive.Create(
                _workDir, _archivePath, "{}");

            var entries = ReadArchive();
            Assert.IsFalse(entries.Keys.Any(k => k.Contains(".tfvars", StringComparison.OrdinalIgnoreCase)));
            StringAssert.Contains(entries["main.tf"], "s3cret");
        }

        [TestMethod]
        public void Create_RejectsSourceProvidedProvenanceInsteadOfProducingDuplicateEntries()
        {
            WriteFile(TerraformSourceArchive.ProvenanceEntryName, "{\"GitCommitSha\":\"spoofed\"}");
            Assert.ThrowsExactly<InvalidDataException>(() =>
                TerraformSourceArchive.Create(_workDir, _archivePath, "{}"));
        }

        [TestMethod]
        [DataRow(1, 100L, 100L, UnsafeArchiveReason.EntryCountExceeded)]
        [DataRow(10, 4L, 100L, UnsafeArchiveReason.EntrySizeExceeded)]
        [DataRow(10, 100L, 6L, UnsafeArchiveReason.TotalSizeExceeded)]
        public void Create_RejectsUnrestorableArchiveBeforeApproval(int count, long perEntry, long total,
            UnsafeArchiveReason reason)
        {
            WriteFile("oversized.txt", "12345");
            var error = Assert.ThrowsExactly<UnsafeArchiveException>(() =>
                TerraformSourceArchive.Create(_workDir, _archivePath, "{}", new ZipArchiveExtractionOptions
                {
                    MaxEntryCount = count, MaxBytesPerEntry = perEntry, MaxBytesTotal = total
                }));
            Assert.AreEqual(reason, error.Reason);
            StringAssert.Contains(error.Message, "oversized.txt");
        }

        [TestMethod]
        public void Create_AtLimits_RestoresWithSameLimits()
        {
            WriteFile("main.tf", "12345");
            var limits = new ZipArchiveExtractionOptions
            {
                MaxEntryCount = 2, MaxBytesPerEntry = 5, MaxBytesTotal = 7
            };
            TerraformSourceArchive.Create(_workDir, _archivePath, "{}", limits);
            var restored = Path.Join(_workDir, "restored");
            new ZipArchiveExtractor(limits).Extract(_archivePath, restored);
            Assert.AreEqual("12345", File.ReadAllText(Path.Join(restored, "main.tf")));
        }
    }
}
