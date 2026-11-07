using Dorc.PersistentData.Model;
using System.Security.Cryptography;
using System.Text;

namespace Dorc.Core.AzureStorageAccount
{
    public sealed record TerraformPlanArtifactDigests(
        string PlanSha256, string SourceSha256, string PlanContentSha256);

    public static class TerraformPlanArtifactIntegrity
    {
        public static string HashPlanContent(string content) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

        public static TerraformPlanArtifactDigests Calculate(IAzureStorageAccountWorker storage, int resultId)
        {
            return new TerraformPlanArtifactDigests(
                storage.GetFileSha256FromBlobs(resultId.CreateTerraformPlanBlobName()),
                storage.GetFileSha256FromBlobs(resultId.CreateTerraformPlanBlobName() + ".source.zip"),
                HashPlanContent(storage.LoadFileFromBlobs(resultId.CreateTerraformPlanContentBlobName())));
        }

        public static void Verify(TerraformPlanApproval? approval, int requestId, int resultId,
            string planPath, string sourcePath)
        {
            if (approval == null || approval.DeploymentRequestId != requestId ||
                approval.DeploymentResultId != resultId)
                throw new InvalidOperationException("No approval exists for this execution. Replan and obtain a fresh approval.");
            if (!string.Equals(approval.PlanSha256, HashFile(planPath), StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(approval.SourceSha256, HashFile(sourcePath), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Approved Terraform artifacts have changed. Replan and obtain a fresh approval.");
        }

        private static string HashFile(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
    }
}
