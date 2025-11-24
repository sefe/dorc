using Dorc.Api.Interfaces;
using Dorc.Api.Services;
using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core;
using Dorc.Core.VariableResolution;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Dorc.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    [Route("api/[controller]")]
    public class PropertyValuesController : ControllerBase
    {
        private readonly IPropertyValues _propertyValuesService;
        private readonly IPropertyValuesPersistentSource _propertyValuesPersistentSource;
        private readonly IVariableResolver _variableResolver;
        private readonly IEnvironmentsPersistentSource _environmentsPersistentSource;
        private readonly IVariableScopeOptionsResolver _variableScopeOptionsResolver;

        public PropertyValuesController(IPropertyValues propertyValuesService,
            IPropertyValuesPersistentSource propertyValuesPersistentSource,
            [FromKeyedServices("VariableResolver")] IVariableResolver variableResolver,
            IEnvironmentsPersistentSource environmentsPersistentSource, IVariableScopeOptionsResolver variableScopeOptionsResolver)
        {
            _variableScopeOptionsResolver = variableScopeOptionsResolver;
            _environmentsPersistentSource = environmentsPersistentSource;
            _variableResolver = variableResolver;
            _propertyValuesPersistentSource = propertyValuesPersistentSource;
            _propertyValuesService = propertyValuesService;
        }

        /// <summary>
        /// Get property value scope options
        /// </summary>
        /// <param name="propertyValueScope"></param>
        /// <returns></returns>
        [HttpGet]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(List<PropertyValueScopeOptionApiModel>))]
        [Route("ScopeOptions")]
        public IActionResult GetScopeOptions(string? propertyValueScope)
        {
            _propertyValuesPersistentSource.AddFilter(PropertyValueFilterTypes.EnvironmentPropertyFilterType, propertyValueScope);
            _variableResolver.SetPropertyValue(PropertyValueScopeOptionsFixed.EnvironmentName, propertyValueScope);
            var environment = _environmentsPersistentSource.GetEnvironment(propertyValueScope, User);
            if (environment is not null)
            {
                _variableScopeOptionsResolver.SetPropertyValues(_variableResolver, environment);
            }

            var props = _variableResolver.LocalProperties();

            if (props == null)
            {
                return NotFound();
            }

            List<PropertyValueScopeOptionApiModel> output = props.Where(FilterOutNullAndEmpty)
                .Select(model => new PropertyValueScopeOptionApiModel
                {
                    ValueFilterScope = propertyValueScope,
                    ValueOption = "$" + model.Key + "$",
                    SampleResolvedValue = model.Value.Value
                }).ToList();

            return Ok(output);
        }

        private bool FilterOutNullAndEmpty(KeyValuePair<string, VariableValue> arg)
        {
            if (arg.Value == null)
                return false;

            if (!(arg.Value.Value is string str))
                return true;

            return string.Empty != str;
        }

        /// <summary>
        /// Get property values
        /// </summary>
        /// <param name="propertyName"></param>
        /// <param name="environmentName"></param>
        /// <returns></returns>
        [Produces(typeof(IQueryable<PropertyValueDto>))]
        [HttpGet]
        public IActionResult GetPropertyValues(string propertyName = null, string environmentName = null)
        {
            try
            {
                var propertyValues = _propertyValuesService.GetPropertyValues(propertyName, environmentName,
                    User).ToArray();
                if (!propertyValues.Any())
                {
                    return NotFound();
                }

                return Ok(propertyValues);
            }
            catch (NonEnoughRightsException e)
            {
                return StatusCode(StatusCodes.Status403Forbidden, e);
            }
            catch (Exception e)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, e);
            }
        }

        /// <summary>
        /// Edit property values
        /// </summary>
        /// <param name="propertyValuesToUpdate"></param>
        /// <returns></returns>
        [Produces(typeof(IEnumerable<Response>))]
        [HttpPut]
        public IActionResult PutPropertyValue(IEnumerable<PropertyValueDto> propertyValuesToUpdate)
        {
            return Ok(_propertyValuesService.PutPropertyValues(propertyValuesToUpdate, User));
        }

        /// <summary>
        /// Create property values
        /// </summary>
        /// <param name="propertyValuesToCreate"></param>
        /// <returns></returns>
        [Produces(typeof(IEnumerable<Response>))]
        [HttpPost]
        public IActionResult PostPropertyValue(IEnumerable<PropertyValueDto> propertyValuesToCreate)
        {
            return Ok(_propertyValuesService.PostPropertyValues(propertyValuesToCreate, User));
        }

        /// <summary>
        /// Delete property values
        /// </summary>
        /// <param name="propertyValuesToDelete"></param>
        /// <returns></returns>
        [Produces(typeof(IEnumerable<Response>))]
        [HttpDelete]
        public IActionResult DeletePropertyValue(IEnumerable<PropertyValueDto> propertyValuesToDelete)
        {
            return Ok(_propertyValuesService.DeletePropertyValues(propertyValuesToDelete, User));
        }
    }
}