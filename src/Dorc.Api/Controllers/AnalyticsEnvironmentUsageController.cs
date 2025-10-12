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
        /// Get deployment counts grouped by environment
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(IEnumerable<AnalyticsEnvironmentUsageApiModel>))]
        public IEnumerable<AnalyticsEnvironmentUsageApiModel> Get()
        {
            try
            {
                return _analyticsPersistentSource.GetEnvironmentUsage();
            }
            catch (Exception e)
            {
                _log.Error("AnalyticsEnvironmentUsageController.Get", e);
                throw;
            }
        }
    }
}
