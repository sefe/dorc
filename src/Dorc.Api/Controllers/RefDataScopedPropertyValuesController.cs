using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Dorc.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public sealed class RefDataScopedPropertyValuesController : ControllerBase
    {
        private readonly IPropertyValuesPersistentSource _propertyValuesPersistentSource;
        private readonly IEnvironmentsPersistentSource _environmentsPersistentSource;
        private readonly IRolePrivilegesChecker _rolePrivilegesChecker;
        private readonly ISecurityPrivilegesChecker _securityPrivilegesChecker;

        public RefDataScopedPropertyValuesController(IPropertyValuesPersistentSource propertyValuesPersistentSource,
            IEnvironmentsPersistentSource environmentsPersistentSource,
            IRolePrivilegesChecker rolePrivilegesChecker,
            ISecurityPrivilegesChecker securityPrivilegesChecker)
        {
            _rolePrivilegesChecker = rolePrivilegesChecker;
            _environmentsPersistentSource = environmentsPersistentSource;
            _propertyValuesPersistentSource = propertyValuesPersistentSource;
            _securityPrivilegesChecker = securityPrivilegesChecker;
        }

        /// <summary>
        /// Get list of Scoped Property Values used for infinite scrolling in the UI
        /// </summary>
        /// <param name="operators"></param>
        /// <param name="scope"></param>
        /// <param name="page"></param>
        /// <param name="limit"></param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(GetScopedPropertyValuesResponseDto))]
        [SwaggerResponse(StatusCodes.Status400BadRequest, Type = typeof(string))]
        [HttpPut]
        public IActionResult Put([FromBody] PagedDataOperators operators, string scope, int page = 1, int limit = 50)
        {
            if (string.IsNullOrEmpty(scope))
                return StatusCode(StatusCodes.Status400BadRequest, "'scope' must include an environment name");

            if (!_environmentsPersistentSource.EnvironmentExists(scope))
                return StatusCode(StatusCodes.Status400BadRequest, "'scope' must include a valid environment name, '" + scope + "' not located");

            var env = _environmentsPersistentSource.GetEnvironment(scope, User);

            var getScopedPropertyValuesResponseDto = _propertyValuesPersistentSource.GetPropertyValuesForScopeByPage(limit,
                page, operators, env, User);


            var canReadSecrets = _securityPrivilegesChecker.CanReadSecrets(User, scope);
            foreach (var prop in getScopedPropertyValuesResponseDto.Items)
            {
                if (prop.Secure == true && !canReadSecrets)
                {
                    prop.PropertyValue = null;
                }
            }

            var isAdmin = _rolePrivilegesChecker.IsAdmin(User);
            if (isAdmin && getScopedPropertyValuesResponseDto.Items != null)
            {
                foreach (var prop in getScopedPropertyValuesResponseDto.Items)
                    prop.UserEditable = true;
            }

            return StatusCode(StatusCodes.Status200OK, getScopedPropertyValuesResponseDto);
        }
    }
}