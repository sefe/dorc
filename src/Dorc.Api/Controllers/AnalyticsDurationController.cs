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
    public class AnalyticsDurationController : ControllerBase
    {
        private readonly IAnalyticsPersistentSource _analyticsPersistentSource;
        private readonly ILog _log;

        public AnalyticsDurationController(IAnalyticsPersistentSource analyticsPersistentSource, ILog log)
        {
            _analyticsPersistentSource = analyticsPersistentSource;
            _log = log;
        }

        /// <summary>
        /// Get deployment duration analytics
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(AnalyticsDurationApiModel))]
        public AnalyticsDurationApiModel Get()
        {
            try
            {
                return _analyticsPersistentSource.GetDeploymentDuration();
            }
            catch (Exception e)
            {
                _log.Error("AnalyticsDurationController.Get", e);
                throw;
            }
        }
    }
}
