using Dorc.ApiModel;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Dorc.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class RefDataProjectBuildsController : ControllerBase
    {
        private readonly IEnvironmentsPersistentSource _environmentsPersistentSource;

        public RefDataProjectBuildsController(IEnvironmentsPersistentSource environmentsPersistentSource)
        {
            _environmentsPersistentSource = environmentsPersistentSource;
        }

        /// <summary>
        /// Get list of Environment Component Statuses
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(IEnumerable<EnvBuildsApiModel>))]
        [HttpGet]
        public IActionResult Get(string id)
        {
            var projectBuilds = _environmentsPersistentSource.GetEnvironmentComponentStatuses(id, DateTime.Now);
            return StatusCode(StatusCodes.Status200OK, projectBuilds);
        }
    }
}