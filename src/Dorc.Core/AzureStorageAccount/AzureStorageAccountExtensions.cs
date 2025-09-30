namespace Dorc.Core.AzureStorageAccount
{
    public static class AzureStorageAccountExtensions
    {
        public static string CreateTerraformBlobName(this string deploymentResultId)
        {
            return $"plan-{deploymentResultId}.txt";
        }
        public static string CreateTerraformBlobName(this int deploymentResultId)
        {
            return $"plan-{deploymentResultId}.txt";
        }
    }
}
