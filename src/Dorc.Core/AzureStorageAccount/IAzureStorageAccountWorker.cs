namespace Dorc.Core.AzureStorageAccount
{
    public interface IAzureStorageAccountWorker
    {
        Task SaveFileToBlobsAsync(string fileName);
        Task<string> LoadFileFromBlobsAsync(string blobName);
        Task DownloadFileFromBlobsAsync(string blobName, string filePath);
    }
}
