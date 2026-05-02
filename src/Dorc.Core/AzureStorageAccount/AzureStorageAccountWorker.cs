using Azure.Identity;
using Azure.Storage.Blobs;
using Dorc.Core.Configuration;

namespace Dorc.Core.AzureStorageAccount
{
    public class AzureStorageAccountWorker : IAzureStorageAccountWorker
    {
        private readonly BlobServiceClient _blobServiceClient;
        private readonly string _blobContainerName;

        public AzureStorageAccountWorker(IConfigurationSettings configurationSettings)
        {
            var clientSecretCredential = new ClientSecretCredential(
                configurationSettings.GetAzureStorageAccountTenantId(),
                configurationSettings.GetAzureStorageAccountClientId(),
                configurationSettings.GetAzureStorageAccountClientSecret());
            _blobServiceClient = new BlobServiceClient(
                    new Uri(configurationSettings.GetAzureStorageAccounUri()),
                    clientSecretCredential);

            _blobContainerName = configurationSettings.GetAzureStorageAccountTerraformBlobsContainerName();
        }

        public void SaveFileToBlobs(string fileName)
        {
            if (!File.Exists(fileName))
            {
                throw new FileNotFoundException($"File {fileName} doesn't exist.");
            }

            var containerClient = _blobServiceClient.GetBlobContainerClient(_blobContainerName);
            var blobClient = containerClient.GetBlobClient(Path.GetFileName(fileName));

            blobClient.Upload(fileName, true);
        }

        public string LoadFileFromBlobs(string blobName)
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_blobContainerName);
            var blobClient = containerClient.GetBlobClient(Path.GetFileName(blobName));
            var blobExists = blobClient.Exists();
            if (!blobExists)
            {
                throw new FileNotFoundException($"Blob {blobName} does not exist in container {this._blobContainerName}");
            }

            using (StreamReader sr = new StreamReader(blobClient.OpenRead()))
            {
                return sr.ReadToEnd();
            }
        }

        public void DownloadFileFromBlobs(string blobName, string filePath)
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_blobContainerName);
            var blobClient = containerClient.GetBlobClient(Path.GetFileName(blobName));
            var blobExists = blobClient.Exists();
            if (!blobExists)
            {
                throw new FileNotFoundException($"Blob {blobName} does not exist in container {this._blobContainerName}");
            }
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            blobClient.DownloadTo(filePath);
        }
    }
}
