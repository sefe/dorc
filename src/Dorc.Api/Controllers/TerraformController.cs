using Dorc.ApiModel;
using Dorc.Core.AzureStorageAccount;
using Dorc.Core.Configuration;
using Dorc.Core.Interfaces;
using Dorc.Core.Security;
using Dorc.PersistentData;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using System.Security.Cryptography;
using System.Text;

namespace Dorc.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class TerraformController : ControllerBase
    {
        private const string NoEnvironmentAuthorityMessage =
            "You do not have permission to act on Terraform plans for this environment.";

        private const string SeparateApproverRequiredMessage =
            "This deployment was requested by you. A different approver is required for this environment.";

        private const string CannotEstablishAuthorityMessage =
            "Unable to establish which environment this Terraform plan belongs to.";

        private readonly ILogger _log;
        private readonly IRequestsPersistentSource _requestsPersistentSource;
        private readonly ISecurityPrivilegesChecker _apiSecurityService;
        private readonly IClaimsPrincipalReader _claimsPrincipalReader;
        private readonly IAzureStorageAccountWorker _azureStorageAccountWorker;
        private readonly IConfigurationSettings _configurationSettings;

        public TerraformController(
            ILogger<TerraformController> log,
            IRequestsPersistentSource requestsPersistentSource,
            ISecurityPrivilegesChecker apiSecurityService,
            IClaimsPrincipalReader claimsPrincipalReader,
            IAzureStorageAccountWorker azureStorageAccountWorker,
            IConfigurationSettings configurationSettings)
        {
            _log = log;
            _requestsPersistentSource = requestsPersistentSource;
            _apiSecurityService = apiSecurityService;
            _claimsPrincipalReader = claimsPrincipalReader;
            _azureStorageAccountWorker = azureStorageAccountWorker;
            _configurationSettings = configurationSettings;
        }

        /// <summary>
        /// Gets Terraform plan for a deployment result
        /// </summary>
        /// <param name="deploymentResultId">The deployment result ID</param>
        /// <returns>The Terraform plan details</returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(TerraformPlanApiModel))]
        [SwaggerResponse(StatusCodes.Status403Forbidden)]
        [SwaggerResponse(StatusCodes.Status404NotFound)]
        [HttpGet("plan/{deploymentResultId}")]
        public IActionResult GetTerraformPlan(int deploymentResultId)
        {
            try
            {
                _log.LogInformation($"Getting Terraform plan for deployment result ID: {deploymentResultId}");

                var authorization = AuthorizeForEnvironment(deploymentResultId);
                if (!authorization.Succeeded)
                {
                    return authorization.Failure!;
                }

                var deploymentResult = authorization.Result!;

                // Plan content is only loaded once authority is established. Ordering
                // matters: the plan embeds resolved variable values, so a refused caller
                // must not cause it to be read at all.
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
                _log.LogError(ex, $"Failed to get Terraform plan for deployment result ID {deploymentResultId}: {ex.Message}");
                return StatusCode(StatusCodes.Status500InternalServerError, "Failed to retrieve Terraform plan");
            }
        }

        /// <summary>
        /// Confirms a Terraform plan for execution
        /// </summary>
        /// <param name="deploymentResultId">The deployment result ID</param>
        /// <returns>Success response</returns>
        [SwaggerResponse(StatusCodes.Status200OK)]
        [SwaggerResponse(StatusCodes.Status403Forbidden)]
        [SwaggerResponse(StatusCodes.Status404NotFound)]
        [SwaggerResponse(StatusCodes.Status409Conflict)]
        [HttpPost("plan/{deploymentResultId}/confirm")]
        public IActionResult ConfirmTerraformPlan(int deploymentResultId)
        {
            try
            {
                _log.LogInformation($"Confirming Terraform plan for deployment result ID: {deploymentResultId}");

                var authorization = AuthorizeForEnvironment(deploymentResultId);
                if (!authorization.Succeeded)
                {
                    return authorization.Failure!;
                }

                var deploymentResult = authorization.Result!;
                var deploymentRequest = authorization.Request!;

                // Confirming is the only action gated by segregation of duties. Declining is
                // not - see DeclineTerraformPlan.
                var separateApproverFailure = RequireSeparateApprover(deploymentRequest);
                if (separateApproverFailure != null)
                {
                    return separateApproverFailure;
                }

                if (deploymentResult.Status != DeploymentResultStatus.WaitingConfirmation.ToString())
                {
                    return BadRequest($"Deployment result {deploymentResultId} is not in WaitingConfirmation status. Current status: {deploymentResult.Status}");
                }

                // Guarded transition. The status was read before the authorization checks, so
                // it may have moved since - a confirm racing the Monitor would otherwise push
                // a running result back to Confirmed and cause a second apply dispatch.
                var transitioned = _requestsPersistentSource.UpdateResultStatus(
                    deploymentResult,
                    DeploymentResultStatus.Confirmed,
                    DeploymentResultStatus.WaitingConfirmation);

                if (!transitioned)
                {
                    _log.LogWarning($"Terraform plan confirmation for deployment result ID {deploymentResultId} lost a race; the result is no longer awaiting confirmation.");
                    return Conflict($"Deployment result {deploymentResultId} is no longer awaiting confirmation. It may have been confirmed, declined, or started already.");
                }

                _requestsPersistentSource.UpdateRequestStatus(
                    deploymentResult.RequestId,
                    DeploymentRequestStatus.Confirmed);

                var userName = _claimsPrincipalReader.GetUserName(User);
                _log.LogInformation($"Terraform plan confirmed for deployment result ID: {deploymentResultId} by user: {userName}");

                // Note: The actual execution will be handled by the Monitor service when it picks up the Confirmed status

                return Ok(new
                {
                    message = "Terraform plan confirmed successfully",
                    deploymentResultId = deploymentResultId,
                    confirmedBy = userName,
                    confirmedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Failed to confirm Terraform plan for deployment result ID {deploymentResultId}: {ex.Message}", ex);
                return StatusCode(StatusCodes.Status500InternalServerError, "Failed to confirm Terraform plan");
            }
        }

        /// <summary>
        /// Declines a Terraform plan
        /// </summary>
        /// <param name="deploymentResultId">The deployment result ID</param>
        /// <returns>Success response</returns>
        [SwaggerResponse(StatusCodes.Status200OK)]
        [SwaggerResponse(StatusCodes.Status403Forbidden)]
        [SwaggerResponse(StatusCodes.Status404NotFound)]
        [HttpPost("plan/{deploymentResultId}/decline")]
        public IActionResult DeclineTerraformPlan(int deploymentResultId)
        {
            try
            {
                _log.LogInformation($"Declining Terraform plan for deployment result ID: {deploymentResultId}");

                // Environment authority only. Segregation of duties deliberately does NOT
                // apply here: declining stops a deployment rather than starting one, so
                // there is no self-approval to prevent, and applying it would stop a
                // requester cancelling their own pending plan.
                var authorization = AuthorizeForEnvironment(deploymentResultId);
                if (!authorization.Succeeded)
                {
                    return authorization.Failure!;
                }

                var deploymentResult = authorization.Result!;

                if (deploymentResult.Status != DeploymentResultStatus.WaitingConfirmation.ToString())
                {
                    return BadRequest($"Deployment result {deploymentResultId} is not in WaitingConfirmation status. Current status: {deploymentResult.Status}");
                }

                var transitioned = _requestsPersistentSource.UpdateResultStatus(
                    deploymentResult,
                    DeploymentResultStatus.Cancelled,
                    DeploymentResultStatus.WaitingConfirmation);

                if (!transitioned)
                {
                    _log.LogWarning($"Terraform plan decline for deployment result ID {deploymentResultId} lost a race; the result is no longer awaiting confirmation.");
                    return Conflict($"Deployment result {deploymentResultId} is no longer awaiting confirmation. It may have been confirmed, declined, or started already.");
                }

                _requestsPersistentSource.UpdateRequestStatus(
                    deploymentResult.RequestId,
                    DeploymentRequestStatus.Cancelled);

                var userName = _claimsPrincipalReader.GetUserName(User);
                _log.LogInformation($"Terraform plan declined for deployment result ID: {deploymentResultId} by user: {userName}");

                return Ok(new
                {
                    message = "Terraform plan declined successfully",
                    deploymentResultId = deploymentResultId,
                    declinedBy = userName,
                    declinedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Failed to decline Terraform plan for deployment result ID {deploymentResultId}: {ex.Message}", ex);
                return StatusCode(StatusCodes.Status500InternalServerError, "Failed to decline Terraform plan");
            }
        }

        /// <summary>
        /// Resolves the deployment result and its request, and checks that the caller holds
        /// modify authority over the request's environment.
        ///
        /// A deployment result does not carry an environment, so the environment must be
        /// reached through the result's request. Every failure in that chain refuses:
        /// CanModifyEnvironment falls back to an administrator check when the environment
        /// cannot be found, so an absent or blank environment name would otherwise permit
        /// every administrator rather than refuse.
        /// </summary>
        private AuthorizationOutcome AuthorizeForEnvironment(int deploymentResultId)
        {
            DeploymentResultApiModel? deploymentResult;
            DeploymentRequestApiModel? deploymentRequest;

            try
            {
                deploymentResult = _requestsPersistentSource.GetDeploymentResults(deploymentResultId);

                if (deploymentResult == null)
                {
                    return AuthorizationOutcome.Refused(
                        NotFound($"Deployment result with ID {deploymentResultId} not found"));
                }

                deploymentRequest = _requestsPersistentSource.GetRequest(deploymentResult.RequestId);
            }
            catch (Exception ex)
            {
                // Converted to a refusal rather than allowed to reach the action's blanket
                // handler, which would return 500. An authorization decision that cannot be
                // made is a refusal, not a fault to be reported as success-adjacent.
                _log.LogError(ex, $"Failed to resolve the environment for deployment result ID {deploymentResultId}; refusing.");
                return AuthorizationOutcome.Refused(
                    StatusCode(StatusCodes.Status403Forbidden, CannotEstablishAuthorityMessage));
            }

            if (deploymentRequest == null)
            {
                _log.LogWarning($"Deployment result {deploymentResultId} references request {deploymentResult.RequestId}, which could not be resolved; refusing.");
                return AuthorizationOutcome.Refused(
                    StatusCode(StatusCodes.Status403Forbidden, CannotEstablishAuthorityMessage));
            }

            if (string.IsNullOrWhiteSpace(deploymentRequest.EnvironmentName))
            {
                _log.LogWarning($"Request {deploymentRequest.Id} has no environment name; refusing rather than deferring to the privileges checker, which would fall back to an administrator check.");
                return AuthorizationOutcome.Refused(
                    StatusCode(StatusCodes.Status403Forbidden, CannotEstablishAuthorityMessage));
            }

            if (!_apiSecurityService.CanModifyEnvironment(User, deploymentRequest.EnvironmentName))
            {
                var userIdentifier = CreateLogIdentifier(
                    _claimsPrincipalReader.GetUserSafeIdentifier(User));
                _log.LogInformation(
                    "Forbidden Terraform plan access to deployment result {DeploymentResultId} from user {UserIdentifier}",
                    deploymentResultId,
                    userIdentifier);
                return AuthorizationOutcome.Refused(
                    StatusCode(StatusCodes.Status403Forbidden, NoEnvironmentAuthorityMessage));
            }

            return AuthorizationOutcome.Allowed(deploymentResult, deploymentRequest);
        }

        /// <summary>
        /// Refuses when the caller submitted the request they are confirming, where the
        /// environment's tier requires a separate approver. Returns null when the action
        /// may proceed.
        /// </summary>
        private IActionResult? RequireSeparateApprover(DeploymentRequestApiModel deploymentRequest)
        {
            // Absent or unparseable configuration means "required for production-tier".
            var configured = _configurationSettings.GetTerraformSeparateApproverRequired();
            var required = configured ?? deploymentRequest.IsProd;

            if (!required)
            {
                return null;
            }

            var submitter = GetOriginalSubmitter(deploymentRequest);
            var approverFullDomainName = _claimsPrincipalReader.GetUserFullDomainName(User);
            var approverLogin = _claimsPrincipalReader.GetUserLogin(User);

            // Every comparison input must be established. Permitting because one identifier
            // is unavailable could miss the representation that matches the stored submitter.
            if (!UserIdentityCanonicaliser.CanEstablish(submitter) ||
                !UserIdentityCanonicaliser.CanEstablish(approverFullDomainName) ||
                !UserIdentityCanonicaliser.CanEstablish(approverLogin))
            {
                _log.LogWarning($"Cannot establish submitter or approver identity for request {deploymentRequest.Id}; refusing confirmation.");
                return StatusCode(StatusCodes.Status403Forbidden, SeparateApproverRequiredMessage);
            }

            // OAuth's full-domain identity is normally an email address, whose local part
            // need not equal the user's SAM account name. Compare both representations so
            // DOMAIN\jsmith cannot approve as john.smith@corp when their login is jsmith.
            if (UserIdentityCanonicaliser.SamePrincipal(submitter, approverFullDomainName) ||
                UserIdentityCanonicaliser.SamePrincipal(submitter, approverLogin))
            {
                var approverIdentifier = CreateLogIdentifier(
                    _claimsPrincipalReader.GetUserSafeIdentifier(User));
                _log.LogInformation(
                    "Refused self-approval of request {RequestId} by {ApproverIdentifier}.",
                    deploymentRequest.Id,
                    approverIdentifier);
                return StatusCode(StatusCodes.Status403Forbidden, SeparateApproverRequiredMessage);
            }

            return null;
        }

        private static string CreateLogIdentifier(string? safeIdentifier)
        {
            if (string.IsNullOrWhiteSpace(safeIdentifier))
            {
                return "unavailable";
            }

            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(safeIdentifier));
            return Convert.ToHexString(hash.AsSpan(0, 8));
        }

        /// <summary>
        /// The identity that originally submitted the request.
        ///
        /// The request's own user name is not that identity: the restart endpoint overwrites
        /// it with the restarter's name. Restart archives the current attempt before doing
        /// so, and the archive copies the then-current user name, so the earliest attempt
        /// holds the original submitter. A request that has never been restarted has no
        /// attempts, and its user name has not been overwritten.
        /// </summary>
        private string? GetOriginalSubmitter(DeploymentRequestApiModel deploymentRequest)
        {
            var attempts = _requestsPersistentSource.GetAttemptsForRequest(deploymentRequest.Id);
            var earliestAttempt = attempts?.OrderBy(attempt => attempt.AttemptNumber).FirstOrDefault();

            return earliestAttempt != null
                ? earliestAttempt.UserName
                : deploymentRequest.UserName;
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
                _log.LogError(ex, $"Failed to load plan content for deployment result ID {deploymentResultId}: {ex.Message}", ex);
                throw;
            }
        }

        private sealed class AuthorizationOutcome
        {
            private AuthorizationOutcome() { }

            public IActionResult? Failure { get; private init; }
            public DeploymentResultApiModel? Result { get; private init; }
            public DeploymentRequestApiModel? Request { get; private init; }

            public bool Succeeded => Failure == null;

            public static AuthorizationOutcome Refused(IActionResult failure) =>
                new() { Failure = failure };

            public static AuthorizationOutcome Allowed(
                DeploymentResultApiModel result,
                DeploymentRequestApiModel request) =>
                new() { Result = result, Request = request };
        }
    }
}
