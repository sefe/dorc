using Dorc.ApiModel;
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

        public ResultStatusesController(IRequestsPersistentSource requestsPersistentSource)
        {
            _requestsPersistentSource = requestsPersistentSource;
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

            return Ok(deploymentResultModels);
        }
    }
}
