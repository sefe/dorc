using Dorc.ApiModel;
using Dorc.PersistentData.Sources.Interfaces;
using log4net;

namespace Dorc.Monitor
{
    internal class TerraformConfirmedPlanProcessor
    {
        private readonly ILog logger;
        private readonly IRequestsPersistentSource requestsPersistentSource;
        private readonly ITerraformDispatcher terraformDispatcher;

        public TerraformConfirmedPlanProcessor(
            ILog logger,
            IRequestsPersistentSource requestsPersistentSource,
            ITerraformDispatcher terraformDispatcher)
        {
            this.logger = logger;
            this.requestsPersistentSource = requestsPersistentSource;
            this.terraformDispatcher = terraformDispatcher;
        }

        public async Task ProcessConfirmedPlansAsync(CancellationToken cancellationToken)
        {
            try
            {
                logger.Debug("Checking for confirmed Terraform plans to execute...");

                // Find all deployment results with Confirmed status
                var confirmedResults = GetConfirmedTerraformDeploymentResults();

                if (!confirmedResults.Any())
                {
                    logger.Debug("No confirmed Terraform plans found.");
                    return;
                }

                logger.Info($"Found {confirmedResults.Count()} confirmed Terraform plan(s) to execute.");

                foreach (var deploymentResult in confirmedResults)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        logger.Info($"Executing confirmed Terraform plan for deployment result ID: {deploymentResult.Id}");

                        // Execute the confirmed plan
                        var success = await terraformDispatcher.ExecuteConfirmedPlanAsync(
                            deploymentResult.Id, 
                            cancellationToken);

                        if (success)
                        {
                            logger.Info($"Successfully executed Terraform plan for deployment result ID: {deploymentResult.Id}");
                        }
                        else
                        {
                            logger.Error($"Failed to execute Terraform plan for deployment result ID: {deploymentResult.Id}");
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Error($"Exception while executing Terraform plan for deployment result ID {deploymentResult.Id}: {ex.Message}", ex);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Exception in TerraformConfirmedPlanProcessor: {ex.Message}", ex);
            }
        }

        private IEnumerable<DeploymentResultApiModel> GetConfirmedTerraformDeploymentResults()
        {
            // Get all deployment results with Confirmed status
            // Note: This is a simplified implementation - in a real system you might want to
            // query by specific criteria, limit the number of results, etc.
            
            try
            {
                var confirmedResults = new List<DeploymentResultApiModel>();
                
                // Get running and recently completed requests (they might have confirmed Terraform components)
                var runningRequests = requestsPersistentSource.GetRequestsWithStatus(DeploymentRequestStatus.Running, true)
                    .Concat(requestsPersistentSource.GetRequestsWithStatus(DeploymentRequestStatus.Running, false))
                    .Concat(requestsPersistentSource.GetRequestsWithStatus(DeploymentRequestStatus.Completed, true))
                    .Concat(requestsPersistentSource.GetRequestsWithStatus(DeploymentRequestStatus.Completed, false));
                
                foreach (var request in runningRequests)
                {
                    var deploymentResults = requestsPersistentSource.GetDeploymentResultsForRequest(request.Id);
                    
                    foreach (var result in deploymentResults)
                    {
                        if (result.Status == DeploymentResultStatus.Confirmed.ToString())
                        {
                            confirmedResults.Add(result);
                        }
                    }
                }
                
                return confirmedResults;
            }
            catch (Exception ex)
            {
                logger.Error($"Failed to get confirmed Terraform deployment results: {ex.Message}", ex);
                return Enumerable.Empty<DeploymentResultApiModel>();
            }
        }
    }
}