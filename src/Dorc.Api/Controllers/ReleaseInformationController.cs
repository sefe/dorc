using Dorc.Api.Interfaces;
using Dorc.ApiModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Dorc.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class ReleaseInformationController : ControllerBase
    {
        private readonly IApiServices _apiServices;

        public ReleaseInformationController(IApiServices apiServices)
        {
            _apiServices = apiServices;
        }

        /// <summary>
        /// Get Release Information for deployment Ids
        /// </summary>
        /// <param name="deploymentRequestIds"></param>
        /// <returns></returns>
        [HttpPost]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(IEnumerable<ReleaseInformationApiModel>))]
        public IActionResult Post(DeploymentRequestIds deploymentRequestIds)
        {
            return StatusCode(StatusCodes.Status200OK,
                _apiServices.GetReleaseInformation(deploymentRequestIds.RequestIds));
        }
    }
}