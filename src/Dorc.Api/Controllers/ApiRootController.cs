using Dorc.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dorc.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class ApiRootController : ControllerBase
    {
        /// <summary>
        /// Get the API endpoints for property management
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Produces(typeof(ApiEndpoints))]
        [Route("/")]
        public IActionResult Get()
        {
            return Ok(new ApiEndpoints(Request));
        }
    }
}
