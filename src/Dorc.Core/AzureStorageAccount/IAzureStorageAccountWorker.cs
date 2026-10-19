namespace Dorc.Core.AzureStorageAccount
{
    public interface IAzureStorageAccountWorker
    {
        void SaveFileToBlobs(string fileName);
        string LoadFileFromBlobs(string blobName);
        void DownloadFileFromBlobs(string blobName, string filePath);
        string GetFileSha256FromBlobs(string blobName);
    }
}
