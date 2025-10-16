using Dorc.ApiModel;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Dorc.Api.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class AnalyticsController : ControllerBase
    {
        private readonly IAnalyticsPersistentSource _analyticsPersistentSource;
        private readonly ILogger<AnalyticsController> _logger;

        public AnalyticsController(
            IAnalyticsPersistentSource analyticsPersistentSource,
            ILogger<AnalyticsController> logger)
        {
            _analyticsPersistentSource = analyticsPersistentSource;
            _logger = logger;
        }

        [HttpGet("EnvironmentUsage")]
        public ActionResult<IEnumerable<AnalyticsEnvironmentUsageApiModel>> GetEnvironmentUsage()
        {
            try
            {
                var data = _analyticsPersistentSource.GetEnvironmentUsage();
                return Ok(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving environment usage analytics");
                return StatusCode(500, "An error occurred while retrieving environment usage data");
            }
        }

        [HttpGet("UserActivity")]
        public ActionResult<IEnumerable<AnalyticsUserActivityApiModel>> GetUserActivity()
        {
            try
            {
                var data = _analyticsPersistentSource.GetUserActivity();
                return Ok(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving user activity analytics");
                return StatusCode(500, "An error occurred while retrieving user activity data");
            }
        }

        [HttpGet("TimePattern")]
        public ActionResult<IEnumerable<AnalyticsTimePatternApiModel>> GetTimePattern()
        {
            try
            {
                var data = _analyticsPersistentSource.GetTimePatterns();
                return Ok(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving time pattern analytics");
                return StatusCode(500, "An error occurred while retrieving time pattern data");
            }
        }

        [HttpGet("ComponentUsage")]
        public ActionResult<IEnumerable<AnalyticsComponentUsageApiModel>> GetComponentUsage()
        {
            try
            {
                var data = _analyticsPersistentSource.GetComponentUsage();
                return Ok(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving component usage analytics");
                return StatusCode(500, "An error occurred while retrieving component usage data");
            }
        }

        [HttpGet("Duration")]
        public ActionResult<AnalyticsDurationApiModel> GetDuration()
        {
            try
            {
                var data = _analyticsPersistentSource.GetDeploymentDuration();
                return Ok(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving deployment duration analytics");
                return StatusCode(500, "An error occurred while retrieving deployment duration data");
            }
        }
    }
}
