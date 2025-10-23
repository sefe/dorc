using Dorc.ApiModel;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Dorc.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class AnalyticsDeploymentsMonthController : ControllerBase
    {
        private readonly IAnalyticsPersistentSource _analyticsPersistentSource;
        private readonly ILogger _log;

        public AnalyticsDeploymentsMonthController(IAnalyticsPersistentSource analyticsPersistentSource, ILogger log)
        {
            _analyticsPersistentSource = analyticsPersistentSource;
            _log = log;
        }

        /// <summary>
        /// Get the number of deployments per project per month
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(IEnumerable<AnalyticsDeploymentsPerProjectApiModel>))]
        public IEnumerable<AnalyticsDeploymentsPerProjectApiModel> Get()
        {
            try
            {
                return _analyticsPersistentSource.GetCountDeploymentsPerProjectMonth();
            }
            catch (Exception e)
            {
                _log.LogError(e, "AnalyticsDeploymentsPerProjectApiModel.Get");
                throw;
            }
        }
    }
}