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
        /// Gets result status
        /// </summary>
        /// <returns></returns>`
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

            _deploymentLogService.EnrichDeploymentResultsWithLogs(deploymentResultModels);

            return Ok(deploymentResultModels);
        }
    }
}
