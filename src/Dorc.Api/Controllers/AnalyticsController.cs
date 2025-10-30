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
    public class AnalyticsController : ControllerBase
    {
        private readonly IAnalyticsPersistentSource _analyticsPersistentSource;
        private readonly ILog _log;

        public AnalyticsController(IAnalyticsPersistentSource analyticsPersistentSource, ILog log)
        {
            _analyticsPersistentSource = analyticsPersistentSource;
            _log = log;
        }

        /// <summary>
        /// Get the number of deployments per project per month
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("AnalyticsDeploymentsMonth")]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(IEnumerable<AnalyticsDeploymentsPerProjectApiModel>))]
        public IEnumerable<AnalyticsDeploymentsPerProjectApiModel> GetDeploymentsMonth()
        {
            try
            {
                return _analyticsPersistentSource.GetCountDeploymentsPerProjectMonth();
            }
            catch (Exception e)
            {
                _log.Error("AnalyticsController.GetDeploymentsMonth", e);
                throw;
            }
        }

        /// <summary>
        /// Gets the count of deployments per project and date
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("AnalyticsDeploymentsDate")]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(IEnumerable<AnalyticsDeploymentsPerProjectApiModel>))]
        public IEnumerable<AnalyticsDeploymentsPerProjectApiModel> GetDeploymentsDate()
        {
            try
            {
                return _analyticsPersistentSource.GetCountDeploymentsPerProjectDate();
            }
            catch (Exception e)
            {
                _log.Error("AnalyticsController.GetDeploymentsDate", e);
                throw;
            }
        }

        /// <summary>
        /// Get environment usage analytics
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("AnalyticsEnvironmentUsage")]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(IEnumerable<AnalyticsEnvironmentUsageApiModel>))]
        [SwaggerResponse(StatusCodes.Status500InternalServerError, Type = typeof(string))]
        public IActionResult GetEnvironmentUsage()
        {
            try
            {
                var result = _analyticsPersistentSource.GetEnvironmentUsage();
                return Ok(result);
            }
            catch (Exception e)
            {
                _log.Error("AnalyticsController.GetEnvironmentUsage", e);
                return StatusCode(StatusCodes.Status500InternalServerError, "An error occurred while retrieving environment usage analytics.");
            }
        }

        /// <summary>
        /// Get user activity analytics
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("AnalyticsUserActivity")]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(IEnumerable<AnalyticsUserActivityApiModel>))]
        [SwaggerResponse(StatusCodes.Status500InternalServerError, Type = typeof(string))]
        public IActionResult GetUserActivity()
        {
            try
            {
                var result = _analyticsPersistentSource.GetUserActivity();
                return Ok(result);
            }
            catch (Exception e)
            {
                _log.Error("AnalyticsController.GetUserActivity", e);
                return StatusCode(StatusCodes.Status500InternalServerError, "An error occurred while retrieving user activity analytics.");
            }
        }

        /// <summary>
        /// Get time pattern analytics
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("AnalyticsTimePattern")]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(IEnumerable<AnalyticsTimePatternApiModel>))]
        public IActionResult GetTimePattern()
        {
            try
            {
                var result = _analyticsPersistentSource.GetTimePatterns();
                return Ok(result);
            }
            catch (Exception e)
            {
                _log.Error("AnalyticsController.GetTimePattern", e);
                return StatusCode(StatusCodes.Status500InternalServerError, "An error occurred while retrieving time pattern analytics.");
            }
        }

        /// <summary>
        /// Get component usage analytics
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("AnalyticsComponentUsage")]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(IEnumerable<AnalyticsComponentUsageApiModel>))]
        public IActionResult GetComponentUsage()
        {
            try
            {
                var result = _analyticsPersistentSource.GetComponentUsage();
                return Ok(result);
            }
            catch (Exception e)
            {
                _log.Error("AnalyticsController.GetComponentUsage", e);
                return StatusCode(StatusCodes.Status500InternalServerError, "An error occurred while retrieving component usage analytics.");
            }
        }

        /// <summary>
        /// Get deployment duration analytics
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("AnalyticsDuration")]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(AnalyticsDurationApiModel))]
        public IActionResult GetDuration()
        {
            try
            {
                var result = _analyticsPersistentSource.GetDeploymentDuration();
                return Ok(result);
            }
            catch (Exception e)
            {
                _log.Error("AnalyticsController.GetDuration", e);
                return StatusCode(StatusCodes.Status500InternalServerError, "An error occurred while retrieving deployment duration analytics.");
            }
        }
    }
}
