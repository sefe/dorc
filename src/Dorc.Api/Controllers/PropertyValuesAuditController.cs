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
    public sealed class PropertyValuesAuditController : ControllerBase
    {
        private readonly IPropertyValuesAuditPersistentSource _propertyValuesAuditPersistentSource;

        public PropertyValuesAuditController(IPropertyValuesAuditPersistentSource propertyValuesAuditPersistentSource)
        {
            _propertyValuesAuditPersistentSource = propertyValuesAuditPersistentSource;
        }

        /// <summary>
        /// Get property values audit list, used for infinite scrolling in the UI
        /// </summary>
        /// <param name="operators"></param>
        /// <param name="page"></param>
        /// <param name="limit"></param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(GetPropertyValuesAuditListResponseDto))]
        [HttpPut]
        public IActionResult Put([FromBody] PagedDataOperators operators, int page = 1, int limit = 50, bool useAndLogic = true)
        {
            var requestStatusesListResponseDto = _propertyValuesAuditPersistentSource.GetPropertyValueAuditsByPage(limit,
                page, operators, useAndLogic);

            return StatusCode(StatusCodes.Status200OK, requestStatusesListResponseDto);
        }
    }
}
