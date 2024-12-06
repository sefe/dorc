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
    public sealed class RefDataDaemonsController : ControllerBase
    {
        private readonly IDaemonsPersistentSource _daemonsPersistentSource;

        public RefDataDaemonsController(IDaemonsPersistentSource daemonsPersistentSource) =>
            _daemonsPersistentSource = daemonsPersistentSource;

        /// <summary>
        /// Get all daemons definitions
        /// </summary>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(List<DaemonApiModel>))]
        [HttpGet]
        public IActionResult Get()
        {
            var output = _daemonsPersistentSource
                .GetDaemons()
                .Select(service =>
                    new DaemonApiModel
                    {
                        Id = service.Id,
                        AccountName = service.AccountName,
                        DisplayName = service.DisplayName,
                        Name = service.Name,
                        ServiceType = service.ServiceType
                    }
                )
                .ToList();

            return Ok(output);
        }

        /// <summary>
        /// Create new daemon definition
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        [HttpPost]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(DaemonApiModel))]
        public IActionResult Post([FromBody] DaemonApiModel model) =>
            Ok(_daemonsPersistentSource.Add(model));

        /// <summary>
        /// Edit daemon definition
        /// </summary>
        /// <param name="id"></param>
        /// <param name="model"></param>
        /// <returns></returns>
        [HttpPut]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(DaemonApiModel))]
        public IActionResult Put(int id, [FromBody] DaemonApiModel model) =>
            Ok(_daemonsPersistentSource.Update(model));

        /// <summary>
        /// Delete daemon definition
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK)]
        [SwaggerResponse(StatusCodes.Status404NotFound)]
        [HttpDelete]
        public IResult Delete(int id)
        {
            var result = _daemonsPersistentSource.Delete(id)
                ? Results.Ok()
                : Results.NotFound($"Unable to find {id}");
            return result;
        }
    }
}
