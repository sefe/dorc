using Dorc.Api.Interfaces;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dorc.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    [Route("api/[controller]")]
    public class PropertiesController : ControllerBase
    {
        private readonly IPropertiesService _propertiesService;
        private readonly ISecurityPrivilegesChecker _apiSecurityService;
        private readonly IRolePrivilegesChecker _rolePrivilegesChecker;
        private readonly IPropertyValuesPersistentSource _propertyValuesPersistentSource;

        public PropertiesController(IPropertiesService propertiesService, ISecurityPrivilegesChecker apiSecurityService,
            IRolePrivilegesChecker rolePrivilegesChecker, IPropertyValuesPersistentSource propertyValuesPersistentSource)
        {
            _propertiesService = propertiesService;
            _apiSecurityService = apiSecurityService;
            _rolePrivilegesChecker = rolePrivilegesChecker;
            _propertyValuesPersistentSource = propertyValuesPersistentSource;
        }

        /// <summary>
        /// Get all properties
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        public IEnumerable<PropertyApiModel> GetProperties()
        {
            return _propertiesService.GetProperties();
        }

        /// <summary>
        /// Get a property by id
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [HttpGet("id={id}")]
        [Produces(typeof(PropertyApiModel))]
        public IActionResult GetProperty(string id)
        {
            var property = _propertiesService.GetProperty(id);
            if (property == null)
            {
                return NotFound();
            }

            return Ok(property);
        }

        /// <summary>
        /// Edit a property
        /// </summary>
        /// <param name="propertiesToUpdate"></param>
        /// <returns></returns>
        [HttpPut]
        [Produces(typeof(IEnumerable<Response>))]
        public IActionResult PutProperty(IDictionary<string, PropertyApiModel> propertiesToUpdate)
        {
            if (_apiSecurityService.CanModifyProperty(User))
            {
                return Ok(_propertiesService.PutProperties(propertiesToUpdate, User));
            }

            return StatusCode(StatusCodes.Status403Forbidden, "Current user do not have permissions to modify properties");
        }

        /// <summary>
        /// Create a new property
        /// </summary>
        /// <param name="properties"></param>
        /// <returns></returns>
        [HttpPost]
        [Produces(typeof(IEnumerable<Response>))]
        public IActionResult PostProperties(IEnumerable<PropertyApiModel> properties)
        {
            if (_apiSecurityService.CanModifyProperty(User))
            {
                return Ok(_propertiesService.PostProperties(properties, User));
            }

            return StatusCode(StatusCodes.Status403Forbidden, "Current user do not have permissions to modify properties");
        }

        /// <summary>
        /// Delete a property
        /// </summary>
        /// <param name="properties"></param>
        /// <returns></returns>
        [HttpDelete]
        [Produces(typeof(IEnumerable<Response>))]
        public IActionResult DeleteProperties(IEnumerable<string> properties)
        {
            if (_rolePrivilegesChecker.IsAdmin(User))
            {
                return Ok(_propertiesService.DeleteProperties(properties, User));
            }

            if (!_apiSecurityService.CanModifyProperty(User))
            {
                return StatusCode(StatusCodes.Status403Forbidden, "Current user do not have permissions to modify properties");
            }

            var propertiesWithValues = properties
                .Where(p => _propertyValuesPersistentSource.GetPropertyValuesByName(p)?.Any() == true)
                .ToList();
            if (propertiesWithValues.Any())
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    $"Only administrators can delete variables that have values: {string.Join(", ", propertiesWithValues)}");
            }

            return Ok(_propertiesService.DeleteProperties(properties, User));
        }
    }
}