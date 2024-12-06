using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Dorc.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class RefDataPropertiesController : ControllerBase
    {
        private readonly IPropertiesPersistentSource _propertiesPersistentSource;

        public RefDataPropertiesController(IPropertiesPersistentSource propertiesPersistentSource)
        {
            _propertiesPersistentSource = propertiesPersistentSource;
        }

        /// <summary>
        /// Get list of Properties
        /// </summary>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(IEnumerable<Property>))]
        [HttpGet]
        public IActionResult Get()
        {
            var data = _propertiesPersistentSource.GetAllProperties();
            return StatusCode(StatusCodes.Status200OK, data);
        }
    }
}