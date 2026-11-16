using Azure.Identity;
using Azure.Storage.Blobs;
using Dorc.Core.Configuration;
using System.Security.Cryptography;

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

        public string GetFileSha256FromBlobs(string blobName)
        {
            var blobClient = _blobServiceClient.GetBlobContainerClient(_blobContainerName)
                .GetBlobClient(Path.GetFileName(blobName));
            if (!blobClient.Exists())
                throw new FileNotFoundException($"Blob {blobName} does not exist in container {_blobContainerName}");
            using var stream = blobClient.OpenRead();
            return Convert.ToHexString(SHA256.HashData(stream));
        }

        public string LoadVerifiedTextFromBlobs(string blobName, string expectedSha256, int maximumBytes)
        {
            using var stream = _blobServiceClient.GetBlobContainerClient(_blobContainerName)
                .GetBlobClient(Path.GetFileName(blobName)).OpenRead();
            if (maximumBytes <= 0 || stream.Length > maximumBytes)
                throw new InvalidDataException("The retained inventory exceeds the recovery size limit.");
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var bytes = memory.ToArray();
            if (bytes.Length > maximumBytes ||
                !string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), expectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Retained inventory does not match the recorded artifact digest.");
            return System.Text.Encoding.UTF8.GetString(bytes);
        }

        public void CopyVerifiedBlob(string sourceBlobName, string destinationBlobName, string expectedSha256)
        {
            var container = _blobServiceClient.GetBlobContainerClient(_blobContainerName);
            // OpenRead pins the source ETag (allowModifications defaults to false).
            using var stream = container.GetBlobClient(Path.GetFileName(sourceBlobName)).OpenRead();
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(stream)), expectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Retained source does not match the approved artifact digest.");
            stream.Position = 0;
            container.GetBlobClient(Path.GetFileName(destinationBlobName)).Upload(stream, true);
        }
    }
}
