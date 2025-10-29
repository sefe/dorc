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
    public class AnalyticsComponentUsageController : ControllerBase
    {
        private readonly IAnalyticsPersistentSource _analyticsPersistentSource;
        private readonly ILog _log;

        public AnalyticsComponentUsageController(IAnalyticsPersistentSource analyticsPersistentSource, ILog log)
        {
            _analyticsPersistentSource = analyticsPersistentSource;
            _log = log;
        }

        /// <summary>
        /// Get component usage analytics
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(IEnumerable<AnalyticsComponentUsageApiModel>))]
        public IEnumerable<AnalyticsComponentUsageApiModel> Get()
        {
            try
            {
                return _analyticsPersistentSource.GetComponentUsage();
            }
            catch (Exception e)
            {
                _log.Error("AnalyticsComponentUsageController.Get", e);
                throw;
            }
        }
    }
}
