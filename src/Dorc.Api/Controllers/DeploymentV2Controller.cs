using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Dorc.Api.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class DeploymentV2Controller : ControllerBase
    {
        /// <summary>
        ///     Returns environment by name
        /// </summary>
        /// <returns>Json string with environment name and build number object or empty model if error occurred</returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(string))]
        [HttpGet]
        [HttpHead]
        public IResult Get()
        {
            return Results.Ok();
        }
    }
}
