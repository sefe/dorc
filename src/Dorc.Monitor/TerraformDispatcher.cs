using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.PersistentData.Sources.Interfaces;
using log4net;
using System.Text;

namespace Dorc.Monitor
{
    public class TerraformDispatcher : ITerraformDispatcher
    {
        private readonly ILog logger;
        private readonly IRequestsPersistentSource requestsPersistentSource;

        public TerraformDispatcher(
            ILog logger,
            IRequestsPersistentSource requestsPersistentSource)
        {
            this.logger = logger;
            this.requestsPersistentSource = requestsPersistentSource;
        }

        public async Task<bool> DispatchAsync(
            ComponentApiModel component,
            DeploymentResultApiModel deploymentResult,
            IDictionary<string, VariableValue> properties,
            int requestId,
            bool isProduction,
            string environmentName,
            StringBuilder resultLogBuilder,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            logger.Info($"Creating Terraform plan for component '{component.ComponentName}' with id '{component.ComponentId}'.");

            try
            {
                // Update status to Running
                requestsPersistentSource.UpdateResultStatus(
                    deploymentResult,
                    DeploymentResultStatus.Running);

                // Create terraform plan (placeholder implementation)
                var planContent = await CreateTerraformPlanAsync(component, properties, environmentName, cancellationToken);
                
                // Save plan to blob storage (placeholder implementation)
                var blobUrl = await SavePlanToBlobStorageAsync(planContent, deploymentResult.Id, cancellationToken);

                resultLogBuilder.AppendLine($"Terraform plan created successfully for component '{component.ComponentName}'");
                resultLogBuilder.AppendLine($"Plan stored at: {blobUrl}");

                // Update status to WaitingConfirmation
                requestsPersistentSource.UpdateResultStatus(
                    deploymentResult,
                    DeploymentResultStatus.WaitingConfirmation);

                logger.Info($"Terraform plan created for component '{component.ComponentName}'. Waiting for confirmation.");
                return true;
            }
            catch (Exception ex)
            {
                logger.Error($"Failed to create Terraform plan for component '{component.ComponentName}': {ex.Message}", ex);
                resultLogBuilder.AppendLine($"Failed to create Terraform plan: {ex.Message}");
                return false;
            }
        }

        private async Task<string> CreateTerraformPlanAsync(
            ComponentApiModel component,
            IDictionary<string, VariableValue> properties,
            string environmentName,
            CancellationToken cancellationToken)
        {
            // Placeholder implementation - this would execute terraform plan command
            await Task.Delay(1000, cancellationToken); // Simulate terraform plan execution
            
            var planContent = $@"
Terraform used the selected providers to generate the following execution plan.
Resource actions are indicated with the following symbols:
  + create

Terraform will perform the following actions:

  # Component: {component.ComponentName}
  # Environment: {environmentName}
  # Script Path: {component.ScriptPath}
  
Plan: 1 to add, 0 to change, 0 to destroy.
            ";
            
            return planContent;
        }

        private async Task<string> SavePlanToBlobStorageAsync(
            string planContent,
            int deploymentResultId,
            CancellationToken cancellationToken)
        {
            // Placeholder implementation - this would save to Azure Blob Storage
            await Task.Delay(500, cancellationToken); // Simulate blob storage upload
            
            var blobUrl = $"https://storageaccount.blob.core.windows.net/terraform-plans/plan-{deploymentResultId}-{DateTime.UtcNow:yyyy-MM-dd-HH-mm-ss}.tfplan";
            
            return blobUrl;
        }
    }
}