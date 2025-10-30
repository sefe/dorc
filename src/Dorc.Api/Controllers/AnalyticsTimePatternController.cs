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
    public class AnalyticsTimePatternController : ControllerBase
    {
        private readonly IAnalyticsPersistentSource _analyticsPersistentSource;
        private readonly ILog _log;

        public AnalyticsTimePatternController(IAnalyticsPersistentSource analyticsPersistentSource, ILog log)
        {
            _analyticsPersistentSource = analyticsPersistentSource;
            _log = log;
        }

        /// <summary>
        /// Get time pattern analytics
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(IEnumerable<AnalyticsTimePatternApiModel>))]
        public IActionResult Get()
        {
            try
            {
                var result = _analyticsPersistentSource.GetTimePatterns();
                return Ok(result);
            }
            catch (Exception e)
            {
                _log.Error("AnalyticsTimePatternController.Get", e);
                return StatusCode(StatusCodes.Status500InternalServerError, "An error occurred while retrieving time pattern analytics.");
            }
        }
    }
}
