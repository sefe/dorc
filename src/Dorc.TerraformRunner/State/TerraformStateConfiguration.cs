using Dorc.ApiModel;

namespace Dorc.TerraformRunner.State
{
    internal static class TerraformStateConfiguration
    {
        public static TerraformBackendRenderer.AzureBlobBackend? Resolve(ScriptGroup scriptGroup)
        {
            var values = new[]
            {
                scriptGroup.TerraformStateStorageAccount,
                scriptGroup.TerraformStateContainerName,
                scriptGroup.TerraformStateKey,
                scriptGroup.TerraformStateResourceGroup,
                scriptGroup.TerraformStateSubscriptionId
            };
            if (values.All(string.IsNullOrWhiteSpace)
                && scriptGroup.TerraformSourceType != TerraformSourceType.Catalog)
                return null;

            if (string.IsNullOrWhiteSpace(scriptGroup.TerraformStateStorageAccount)
                || string.IsNullOrWhiteSpace(scriptGroup.TerraformStateContainerName)
                || string.IsNullOrWhiteSpace(scriptGroup.TerraformStateKey)
                || !Guid.TryParse(scriptGroup.TerraformStateSubscriptionId, out _))
                throw new InvalidOperationException(
                    "Durable Terraform state is required: configure StorageAccount, ContainerName and " +
                    "SubscriptionId in the project's Terraform state backends and submit a new request. A non-empty state key is required. " +
                    "Catalog deployments cannot use local state; partially configured platform state cannot fall back to a legacy backend.");

            return new TerraformBackendRenderer.AzureBlobBackend(
                scriptGroup.TerraformStateStorageAccount,
                scriptGroup.TerraformStateContainerName,
                scriptGroup.TerraformStateKey,
                scriptGroup.TerraformStateResourceGroup,
                scriptGroup.TerraformStateSubscriptionId);
        }
    }
}
