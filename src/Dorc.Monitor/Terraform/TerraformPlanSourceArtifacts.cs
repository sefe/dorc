using Dorc.Core.AzureStorageAccount;

namespace Dorc.Monitor.Terraform
{
    internal static class TerraformPlanSourceArtifacts
    {
        public static void Upload(IAzureStorageAccountWorker storage, string planFilePath)
        {
            storage.SaveFileToBlobs(planFilePath + ".source.zip");
        }

        public static void Download(IAzureStorageAccountWorker storage, string planFilePath)
        {
            var archivePath = planFilePath + ".source.zip";
            try
            {
                storage.DownloadFileFromBlobs(Path.GetFileName(archivePath), archivePath);
            }
            catch (FileNotFoundException ex)
            {
                throw new InvalidOperationException(
                    "The approved Terraform plan has no persisted source snapshot. Create and approve a new plan before applying.", ex);
            }
        }
    }
}
