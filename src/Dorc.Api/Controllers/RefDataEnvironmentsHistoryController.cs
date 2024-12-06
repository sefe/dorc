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
    public class RefDataEnvironmentsHistoryController : ControllerBase
    {
        private readonly IEnvironmentHistoryPersistentSource _environmentHistoryPersistentSource;

        public RefDataEnvironmentsHistoryController(IEnvironmentHistoryPersistentSource environmentHistoryPersistentSource)
        {
            _environmentHistoryPersistentSource = environmentHistoryPersistentSource;
        }

        /// <summary>
        ///     Returns Environment history
        /// </summary>
        /// <param name="id">Environment ID</param>
        /// <returns>Json string with array of EnvironmentHistoryApiModel</returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(IEnumerable<EnvironmentHistoryApiModel>))]
        [HttpGet]
        public IActionResult Get(int id)
        {
            var history = _environmentHistoryPersistentSource.GetEnvironmentDetailHistory(id);
            return StatusCode(StatusCodes.Status200OK, history);
        }

        /// <summary>
        /// Edit Environment history comment
        /// </summary>
        /// <param name="history"></param>
        [HttpPut]
        public void Put([FromBody] EnvironmentHistoryApiModel history)
        {
            _environmentHistoryPersistentSource.UpdateEnvironmentDetailHistoryComment(history.Id, history.Comment);
        }

    }
}