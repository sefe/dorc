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

        public async Task SaveFileToBlobsAsync(string fileName)
        {
            if (!File.Exists(fileName))
            {
                throw new FileNotFoundException($"File {fileName} doesn't exist.");
            }

            var containerClient = _blobServiceClient.GetBlobContainerClient(_blobContainerName);
            var blobClient = containerClient.GetBlobClient(Path.GetFileName(fileName));

            await blobClient.UploadAsync(fileName, true);
        }

        public async Task<string> LoadFileFromBlobsAsync(string blobName)
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_blobContainerName);
            var blobClient = containerClient.GetBlobClient(Path.GetFileName(blobName));
            var blobExists = await blobClient.ExistsAsync();
            if (!blobExists)
            {
                throw new FileNotFoundException($"Blob {blobName} does not exist in container {this._blobContainerName}");
            }

            using (StreamReader sr = new StreamReader(await blobClient.OpenReadAsync()))
            {
                return await sr.ReadToEndAsync();
            }
        }
    }
}
