using Dorc.ApiModel;
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
    public class AnalyticsEnvironmentUsageController : ControllerBase
    {
        private readonly IAnalyticsPersistentSource _analyticsPersistentSource;
        private readonly ILog _log;

        public AnalyticsEnvironmentUsageController(IAnalyticsPersistentSource analyticsPersistentSource, ILog log)
        {
            _analyticsPersistentSource = analyticsPersistentSource;
            _log = log;
        }

        /// <summary>
        /// Get environment usage analytics
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(IEnumerable<AnalyticsEnvironmentUsageApiModel>))]
        [SwaggerResponse(StatusCodes.Status500InternalServerError, Type = typeof(string))]
        public IActionResult Get()
        {
            try
            {
                var result = _analyticsPersistentSource.GetEnvironmentUsage();
                return Ok(result);
            }
            catch (Exception e)
            {
                _log.Error("AnalyticsEnvironmentUsageController.Get", e);
                return StatusCode(StatusCodes.Status500InternalServerError, "An error occurred while retrieving environment usage analytics.");
            }
        }
    }
}
