using Dorc.Core.AzureStorageAccount;
using Dorc.PersistentData.Model;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;

namespace Dorc.Core.Tests
{
    [TestClass]
    public class TerraformPlanArtifactIntegrityTests
    {
        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void Verify_RejectsChangedPlanOrSource(bool changeSource)
        {
            var directory = Path.Combine(Path.GetTempPath(), "dorc-integrity-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var storage = Substitute.For<IAzureStorageAccountWorker>();
                storage.LoadFileFromBlobs(Arg.Any<string>()).Returns("reviewed plan");
                storage.GetFileSha256FromBlobs(Arg.Any<string>()).Returns(call =>
                    TerraformPlanArtifactIntegrity.HashPlanContent("original " + call.Arg<string>()));
                storage.When(worker => worker.DownloadFileFromBlobs(Arg.Any<string>(), Arg.Any<string>()))
                    .Do(call => File.WriteAllText(call.ArgAt<string>(1), "original " + call.ArgAt<string>(0)));
                var digests = TerraformPlanArtifactIntegrity.Calculate(storage, 42);
                var approval = new TerraformPlanApproval
                {
                    DeploymentRequestId = 7, DeploymentResultId = 42,
                    PlanSha256 = digests.PlanSha256, SourceSha256 = digests.SourceSha256
                };
                var plan = Path.Combine(directory, "plan.tfplan");
                var source = plan + ".source.zip";
                storage.DownloadFileFromBlobs(42.CreateTerraformPlanBlobName(), plan);
                storage.DownloadFileFromBlobs(42.CreateTerraformPlanBlobName() + ".source.zip", source);
                TerraformPlanArtifactIntegrity.Verify(approval, 7, 42, plan, source);

                File.WriteAllText(changeSource ? source : plan, "changed after approval");

                Assert.Throws<InvalidOperationException>(() =>
                    TerraformPlanArtifactIntegrity.Verify(approval, 7, 42, plan, source));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [TestMethod]
        public void Verify_RejectsMissingOrDifferentExecutionApprovalBeforeReadingFiles()
        {
            Assert.Throws<InvalidOperationException>(() =>
                TerraformPlanArtifactIntegrity.Verify(null, 7, 42, "not-read", "not-read"));
            var approval = new TerraformPlanApproval { DeploymentRequestId = 8, DeploymentResultId = 42 };
            Assert.Throws<InvalidOperationException>(() =>
                TerraformPlanArtifactIntegrity.Verify(approval, 7, 42, "not-read", "not-read"));
        }
    }
}
