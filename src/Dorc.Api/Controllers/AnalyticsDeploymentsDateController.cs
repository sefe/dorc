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
    public class AnalyticsDeploymentsDateController : ControllerBase
    {
        private readonly IAnalyticsPersistentSource _analyticsPersistentSource;
        private readonly ILogger _log;

        public AnalyticsDeploymentsDateController(IAnalyticsPersistentSource analyticsPersistentSource, ILogger log)
        {
            _analyticsPersistentSource = analyticsPersistentSource;
            _log = log;
        }

        /// <summary>
        /// Gets the count of deployments per project and date
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(IEnumerable<AnalyticsDeploymentsPerProjectApiModel>))]
        public IEnumerable<AnalyticsDeploymentsPerProjectApiModel> Get()
        {
            try
            {
                return _analyticsPersistentSource.GetCountDeploymentsPerProjectDate();
            }
            catch (Exception e)
            {
                _log.LogError(e, "AnalyticsDeploymentsDateController.Get");
                throw;
            }
        }
    }
}