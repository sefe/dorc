namespace Dorc.Core.AzureStorageAccount
{
    public static class AzureStorageAccountExtensions
    {
        public static string CreateTerraformPlanBlobName(this int deploymentResultId)
        {
            return $"plan-{deploymentResultId}.tfplan";
        }
        public static string CreateTerraformPlanContantBlobName(this int deploymentResultId)
        {
            return $"plan-{deploymentResultId}.txt";
        }
    }
}
