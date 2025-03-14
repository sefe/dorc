using Dorc.Api.Services;
using Dorc.PersistentData.Utils;
using log4net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dorc.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class ApiRootController : ControllerBase
    {
        private readonly ILog _logger;

        public ApiRootController(ILog logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Get the API endpoints for property management
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Produces(typeof(ApiEndpoints))]
        [Route("/")]
        public IActionResult Get()
        {
            using (var profiler = new TimeProfiler(this._logger, "GetMetadata"))
            {
                return Ok(new ApiEndpoints(Request));
            }
        }
    }
}
