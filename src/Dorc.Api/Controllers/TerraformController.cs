using Dorc.Api.Interfaces;
using Dorc.ApiModel;
using Dorc.Core.AzureStorageAccount;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using log4net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Dorc.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class TerraformController : ControllerBase
    {
        private readonly ILog _log;
        private readonly IRequestsPersistentSource _requestsPersistentSource;
        private readonly ISecurityPrivilegesChecker _apiSecurityService;
        private readonly IClaimsPrincipalReader _claimsPrincipalReader;
        private readonly IAzureStorageAccountWorker _azureStorageAccountWorker;

        public TerraformController(
            ILog log,
            IRequestsPersistentSource requestsPersistentSource,
            ISecurityPrivilegesChecker apiSecurityService,
            IClaimsPrincipalReader claimsPrincipalReader,
            IAzureStorageAccountWorker azureStorageAccountWorker)
        {
            _log = log;
            _requestsPersistentSource = requestsPersistentSource;
            _apiSecurityService = apiSecurityService;
            _claimsPrincipalReader = claimsPrincipalReader;
            _azureStorageAccountWorker = azureStorageAccountWorker;
        }

        /// <summary>
        /// Gets Terraform plan for a deployment result
        /// </summary>
        /// <param name="deploymentResultId">The deployment result ID</param>
        /// <returns>The Terraform plan details</returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(TerraformPlanApiModel))]
        [SwaggerResponse(StatusCodes.Status404NotFound)]
        [HttpGet("plan/{deploymentResultId}")]
        public IActionResult GetTerraformPlan(int deploymentResultId)
        {
            try
            {
                _log.Info($"Getting Terraform plan for deployment result ID: {deploymentResultId}");

                // Get the deployment result
                var deploymentResult = _requestsPersistentSource.GetDeploymentResults(deploymentResultId);
                if (deploymentResult == null)
                {
                    return NotFound($"Deployment result with ID {deploymentResultId} not found");
                }

                // Check if user has permission to view this deployment
                if (!HasViewPermission(deploymentResult))
                {
                    return Forbid("You do not have permission to view this Terraform plan");
                }

                // Load plan content from storage
                var planContent = LoadPlanContentFromStorage(deploymentResultId);
                
                var plan = new TerraformPlanApiModel
                {
                    DeploymentResultId = deploymentResultId,
                    PlanContent = planContent,
                    CreatedAt = deploymentResult.StartedTime?.DateTime ?? DateTime.UtcNow,
                    Status = deploymentResult.Status ?? "Unknown"
                };

                return Ok(plan);
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to get Terraform plan for deployment result ID {deploymentResultId}: {ex.Message}", ex);
                return StatusCode(StatusCodes.Status500InternalServerError, "Failed to retrieve Terraform plan");
            }
        }

        /// <summary>
        /// Confirms a Terraform plan for execution
        /// </summary>
        /// <param name="deploymentResultId">The deployment result ID</param>
        /// <returns>Success response</returns>
        [SwaggerResponse(StatusCodes.Status200OK)]
        [SwaggerResponse(StatusCodes.Status404NotFound)]
        [HttpPost("plan/{deploymentResultId}/confirm")]
        public IActionResult ConfirmTerraformPlan(int deploymentResultId)
        {
            try
            {
                _log.Info($"Confirming Terraform plan for deployment result ID: {deploymentResultId}");

                // Get the deployment result
                var deploymentResult = _requestsPersistentSource.GetDeploymentResults(deploymentResultId);
                if (deploymentResult == null)
                {
                    return NotFound($"Deployment result with ID {deploymentResultId} not found");
                }

                // Security check - ensure user has permission to confirm
                if (!HasConfirmPermission(deploymentResult))
                {
                    return Forbid("You do not have permission to confirm this Terraform plan");
                }

                // Validate that the deployment is in the correct status
                if (deploymentResult.Status != DeploymentResultStatus.WaitingConfirmation.ToString())
                {
                    return BadRequest($"Deployment result {deploymentResultId} is not in WaitingConfirmation status. Current status: {deploymentResult.Status}");
                }

                // Update deployment result status to Confirmed
                _requestsPersistentSource.UpdateResultStatus(
                    deploymentResult,
                    DeploymentResultStatus.Confirmed);

                // Update deployment request status to Confirmed
                _requestsPersistentSource.UpdateRequestStatus(
                    deploymentResult.RequestId,
                    DeploymentRequestStatus.Confirmed);

                // Log the confirmation action for audit purposes
                var userName = _claimsPrincipalReader.GetUserName(User);
                _log.Info($"Terraform plan confirmed for deployment result ID: {deploymentResultId} by user: {userName}");
                
                // Note: The actual execution will be handled by the Monitor service when it picks up the Confirmed status
                
                return Ok(new { 
                    message = "Terraform plan confirmed successfully",
                    deploymentResultId = deploymentResultId,
                    confirmedBy = userName,
                    confirmedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to confirm Terraform plan for deployment result ID {deploymentResultId}: {ex.Message}", ex);
                return StatusCode(StatusCodes.Status500InternalServerError, "Failed to confirm Terraform plan");
            }
        }

        /// <summary>
        /// Declines a Terraform plan
        /// </summary>
        /// <param name="deploymentResultId">The deployment result ID</param>
        /// <returns>Success response</returns>
        [SwaggerResponse(StatusCodes.Status200OK)]
        [SwaggerResponse(StatusCodes.Status404NotFound)]
        [HttpPost("plan/{deploymentResultId}/decline")]
        public IActionResult DeclineTerraformPlan(int deploymentResultId)
        {
            try
            {
                _log.Info($"Declining Terraform plan for deployment result ID: {deploymentResultId}");

                // Get the deployment result
                var deploymentResult = _requestsPersistentSource.GetDeploymentResults(deploymentResultId);
                if (deploymentResult == null)
                {
                    return NotFound($"Deployment result with ID {deploymentResultId} not found");
                }

                // Security check - ensure user has permission to decline
                if (!HasDeclinePermission(deploymentResult))
                {
                    return Forbid("You do not have permission to decline this Terraform plan");
                }

                // Validate that the deployment is in the correct status
                if (deploymentResult.Status != DeploymentResultStatus.WaitingConfirmation.ToString())
                {
                    return BadRequest($"Deployment result {deploymentResultId} is not in WaitingConfirmation status. Current status: {deploymentResult.Status}");
                }

                // Update deployment result status to Cancelled
                _requestsPersistentSource.UpdateResultStatus(
                    deploymentResult,
                    DeploymentResultStatus.Cancelled);

                // Update deployment result status to Cancelled
                _requestsPersistentSource.UpdateRequestStatus(
                    deploymentResult.RequestId,
                    DeploymentRequestStatus.Cancelled);

                // Log the decline action for audit purposes
                var userName = _claimsPrincipalReader.GetUserName(User);
                _log.Info($"Terraform plan declined for deployment result ID: {deploymentResultId} by user: {userName}");

                return Ok(new { 
                    message = "Terraform plan declined successfully",
                    deploymentResultId = deploymentResultId,
                    declinedBy = userName,
                    declinedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to decline Terraform plan for deployment result ID {deploymentResultId}: {ex.Message}", ex);
                return StatusCode(StatusCodes.Status500InternalServerError, "Failed to decline Terraform plan");
            }
        }

        private bool HasViewPermission(DeploymentResultApiModel deploymentResult)
        {
            // TODO: Implement proper permission checking based on environment, project, etc.
            // For now, return true - all authenticated users can view plans
            return true;
        }

        private bool HasConfirmPermission(DeploymentResultApiModel deploymentResult)
        {
            // TODO: Implement proper permission checking
            // This should check if user has deploy permissions for the environment/project
            var userName = _claimsPrincipalReader.GetUserName(User);
            
            // For now, check if user has admin privileges for the environment
            // In a real implementation, this would check environment-specific permissions
            return _apiSecurityService.IsEnvironmentOwnerOrAdmin(User, "default");
        }

        private bool HasDeclinePermission(DeploymentResultApiModel deploymentResult)
        {
            // Same permission logic as confirm for now
            return HasConfirmPermission(deploymentResult);
        }

        private string LoadPlanContentFromStorage(int deploymentResultId)
        {
            try
            {
                var terraformPlanBlobName = deploymentResultId.CreateTerraformPlanContentBlobName();
                var blobContent = _azureStorageAccountWorker.LoadFileFromBlobs(terraformPlanBlobName);

                return blobContent;
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to load plan content for deployment result ID {deploymentResultId}: {ex.Message}", ex);
                return "Failed to load plan content.";
            }
        }
    }
}