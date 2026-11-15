namespace Dorc.Core.Terraform
{
    public interface ITerraformCloudResourceRegistrar
    {
        TerraformRegistrationOutcome RegisterAppliedResources(
            string appliedResourcesFilePath,
            string environmentName,
            string username,
            int? deploymentResultId = null);

        TerraformRegistrationOutcome RegisterSnapshot(
            string json,
            string environmentName,
            string username,
            int? deploymentResultId = null);

        void DeregisterEnvironmentResources(
            int environmentId,
            int? deploymentResultId = null);
    }
}
