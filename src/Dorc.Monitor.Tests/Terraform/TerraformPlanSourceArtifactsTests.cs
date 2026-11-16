using Dorc.Core.AzureStorageAccount;
using Dorc.Monitor.Terraform;
using NSubstitute;
using System.IO.Compression;
using System.Text.Json;

namespace Dorc.Monitor.Tests.Terraform
{
    [TestClass]
    public class TerraformPlanSourceArtifactsTests
    {
        private string _root = null!;
        private IAzureStorageAccountWorker _storage = null!;
        private Dictionary<string, byte[]> _blobs = null!;

        [TestInitialize]
        public void Setup()
        {
            _root = Path.Join(AppContext.BaseDirectory, "source-artifact-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _blobs = new Dictionary<string, byte[]>();
            _storage = Substitute.For<IAzureStorageAccountWorker>();
            _storage.When(worker => worker.SaveFileToBlobs(Arg.Any<string>())).Do(call =>
            {
                var path = call.Arg<string>();
                _blobs[Path.GetFileName(path)] = File.ReadAllBytes(path);
            });
            _storage.When(worker => worker.DownloadFileFromBlobs(Arg.Any<string>(), Arg.Any<string>())).Do(call =>
            {
                var blobName = call.ArgAt<string>(0);
                if (!_blobs.TryGetValue(blobName, out var bytes))
                    throw new FileNotFoundException($"Blob '{blobName}' is missing.");
                File.WriteAllBytes(call.ArgAt<string>(1), bytes);
            });
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        [TestMethod]
        public void SourceSnapshot_RoundTripsThroughArtifactStore_AfterPlanHostFilesAreDeleted()
        {
            var planHost = Path.Join(_root, "plan-host");
            var applyHost = Path.Join(_root, "apply-host");
            Directory.CreateDirectory(planHost);
            Directory.CreateDirectory(applyHost);
            var planName = 42.CreateTerraformPlanBlobName();
            var planPath = Path.Join(planHost, planName);
            File.WriteAllText(planPath, "approved binary plan");
            using (var zip = ZipFile.Open(planPath + ".source.zip", ZipArchiveMode.Create))
            {
                WriteEntry(zip, "main.tf", "# approved source");
                WriteEntry(zip, "terraform.tfvars", "password = \"[REDACTED]\"");
                WriteEntry(zip, "_dorc_provenance.json", "{\"GitRef\":\"main\",\"GitCommitSha\":\"approved-sha\"}");
            }
            var approvedArchive = File.ReadAllBytes(planPath + ".source.zip");

            _storage.SaveFileToBlobs(planPath);
            TerraformPlanSourceArtifacts.Upload(_storage, planPath);
            Directory.Delete(planHost, true);
            // Separate staging location models a fresh Monitor/Runner host:
            // nothing except the remote artifacts survives from plan.
            var applyPlanPath = Path.Join(applyHost, planName);
            _storage.DownloadFileFromBlobs(planName, applyPlanPath);
            TerraformPlanSourceArtifacts.Download(_storage, applyPlanPath);

            Assert.AreEqual("approved binary plan", File.ReadAllText(applyPlanPath));
            CollectionAssert.AreEqual(approvedArchive, File.ReadAllBytes(applyPlanPath + ".source.zip"));
            _storage.Received(1).SaveFileToBlobs(planPath + ".source.zip");
            _storage.Received(1).DownloadFileFromBlobs(planName + ".source.zip", applyPlanPath + ".source.zip");
            using var restoredArchive = ZipFile.OpenRead(applyPlanPath + ".source.zip");
            Assert.AreEqual("# approved source", ReadEntry(restoredArchive, "main.tf"));
            Assert.AreEqual("password = \"[REDACTED]\"", ReadEntry(restoredArchive, "terraform.tfvars"));
            using var provenance = JsonDocument.Parse(ReadEntry(restoredArchive, "_dorc_provenance.json"));
            Assert.AreEqual("approved-sha", provenance.RootElement.GetProperty("GitCommitSha").GetString());
        }

        [TestMethod]
        public void MissingSnapshotBlob_RequiresNewPlan_EvenWhenStaleLocalSnapshotExists()
        {
            var planPath = Path.Join(_root, 42.CreateTerraformPlanBlobName());
            File.WriteAllText(planPath + ".source.zip", "stale local snapshot");
            var error = Assert.ThrowsExactly<InvalidOperationException>(() =>
                TerraformPlanSourceArtifacts.Download(_storage, planPath));

            StringAssert.Contains(error.Message, "Create and approve a new plan");
            Assert.IsInstanceOfType<FileNotFoundException>(error.InnerException);
        }

        [TestMethod]
        public void SnapshotUploadFailure_PropagatesBeforePlanCanBecomeReadyForConfirmation()
        {
            var storage = Substitute.For<IAzureStorageAccountWorker>();
            storage.When(worker => worker.SaveFileToBlobs(Arg.Any<string>()))
                .Do(_ => throw new IOException("Artifact store unavailable."));

            Assert.ThrowsExactly<IOException>(() =>
                TerraformPlanSourceArtifacts.Upload(storage, Path.Join(_root, 42.CreateTerraformPlanBlobName())));
        }

        [TestMethod]
        public void SnapshotDownloadStorageFailure_IsNotTreatedAsMissingOrLocalFallback()
        {
            var storage = Substitute.For<IAzureStorageAccountWorker>();
            storage.When(worker => worker.DownloadFileFromBlobs(Arg.Any<string>(), Arg.Any<string>()))
                .Do(_ => throw new UnauthorizedAccessException("Artifact store access denied."));

            Assert.ThrowsExactly<UnauthorizedAccessException>(() =>
                TerraformPlanSourceArtifacts.Download(storage, Path.Join(_root, 42.CreateTerraformPlanBlobName())));
        }

        private static void WriteEntry(ZipArchive archive, string name, string content)
        {
            using var writer = new StreamWriter(archive.CreateEntry(name).Open());
            writer.Write(content);
        }

        private static string ReadEntry(ZipArchive archive, string name)
        {
            using var reader = new StreamReader(archive.GetEntry(name)!.Open());
            return reader.ReadToEnd();
        }
    }
}
