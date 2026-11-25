using Dorc.Api.Interfaces;
using Dorc.Api.Properties;
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
        private readonly IPropertyDefinitionEditor _propertyDefinitionEditor;
        private readonly ISecurityPrivilegesChecker _apiSecurityService;
        private readonly IRolePrivilegesChecker _rolePrivilegesChecker;
        private readonly IPropertyValuesPersistentSource _propertyValuesPersistentSource;
        private readonly IEnvironmentsPersistentSource _environmentsPersistentSource;

        public PropertiesController(IPropertyDefinitionEditor propertyDefinitionEditor, ISecurityPrivilegesChecker apiSecurityService,
            IRolePrivilegesChecker rolePrivilegesChecker, IPropertyValuesPersistentSource propertyValuesPersistentSource,
            IEnvironmentsPersistentSource environmentsPersistentSource)
        {
            _propertyDefinitionEditor = propertyDefinitionEditor;
            _apiSecurityService = apiSecurityService;
            _rolePrivilegesChecker = rolePrivilegesChecker;
            _propertyValuesPersistentSource = propertyValuesPersistentSource;
            _environmentsPersistentSource = environmentsPersistentSource;
        }

        private bool CanCreateProperty()
        {
            return _apiSecurityService.CanModifyProperty(User) ||
                   (_environmentsPersistentSource.GetEnvironments(User)?.Any(e => e.UserEditable) ?? false);
        }

        /// <summary>
        /// Get all properties
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        public IEnumerable<PropertyApiModel> GetProperties()
        {
            return _propertyDefinitionEditor.GetProperties();
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
            var property = _propertyDefinitionEditor.GetProperty(id);
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
                return Ok(_propertyDefinitionEditor.PutProperties(propertiesToUpdate, User));
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
            if (CanCreateProperty())
            {
                return Ok(_propertyDefinitionEditor.PostProperties(properties, User));
            }

            return StatusCode(StatusCodes.Status403Forbidden, "Current user do not have permissions to modify properties");
        }

        /// <summary>
        /// Import properties and values from CSV-formatted rows
        /// </summary>
        /// <param name="propertiesToImport"></param>
        /// <returns></returns>
        [HttpPost("import")]
        [Produces(typeof(IEnumerable<Response>))]
        public IActionResult ImportProperties(IEnumerable<PropertyImportCsvDto> propertiesToImport)
        {
            if (CanCreateProperty())
            {
                return Ok(_propertyDefinitionEditor.ImportProperties(propertiesToImport, User));
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
                return Ok(_propertyDefinitionEditor.DeleteProperties(properties, User));
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

            return Ok(_propertyDefinitionEditor.DeleteProperties(properties, User));
        }
    }
}