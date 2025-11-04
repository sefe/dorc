using Dorc.ApiModel;
using Dorc.OpenSearchData.Sources.Interfaces;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Dorc.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public sealed class ResultStatusesController : ControllerBase
    {
        private readonly IRequestsPersistentSource _requestsPersistentSource;
        private readonly IDeploymentLogService _deploymentLogService;

        public ResultStatusesController(IRequestsPersistentSource requestsPersistentSource, IDeploymentLogService deploymentLogService)
        {
            _requestsPersistentSource = requestsPersistentSource;
            _deploymentLogService = deploymentLogService;
        }

        /// <summary>
        /// Gets result statuses with limited logs (preview mode)
        /// </summary>
        /// <param name="requestId">Request ID</param>
        /// <returns>List of deployment results with first 3 log lines each</returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(IEnumerable<DeploymentResultApiModel>))]
        [HttpGet]
        public IActionResult GetComponents(int requestId)
        {
            var request = _requestsPersistentSource.GetRequestForUser(requestId, User);
            if (request == null)
            {
                return Ok(new List<DeploymentResultApiModel>());
            }

            var deploymentResultModels = _requestsPersistentSource.GetDeploymentResultsForRequest(requestId);

            _deploymentLogService.EnrichDeploymentResultsWithLimitedLogs(deploymentResultModels, 3);

            return Ok(deploymentResultModels);
        }

        /// <summary>
        /// Gets full logs for a single deployment result
        /// </summary>
        /// <param name="requestId">Request ID</param>
        /// <param name="resultId">Deployment Result ID</param>
        /// <returns>Full log string for the specified deployment result</returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status404NotFound)]
        [HttpGet("Log")]
        public IActionResult GetLog(int requestId, int resultId)
        {
            var request = _requestsPersistentSource.GetRequestForUser(requestId, User);
            if (request == null)
            {
                return NotFound($"Request with ID {requestId} not found or access denied.");
            }

            var deploymentResult = _requestsPersistentSource.GetDeploymentResults(resultId);
            if (deploymentResult == null || deploymentResult.RequestId != requestId)
            {
                return NotFound($"Deployment result with ID {resultId} not found for request {requestId}.");
            }

            var log = _deploymentLogService.GetLogsForSingleResult(requestId, resultId);

            return Ok(log);
        }
    }
}
