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
    public sealed class RefDataSearchPropertyValuesController : ControllerBase
    {
        private readonly IPropertyValuesPersistentSource _propertyValuesPersistentSource;
        private readonly IRolePrivilegesChecker _rolePrivilegesChecker;
        private readonly ISecurityPrivilegesChecker _securityPrivilegesChecker;

        public RefDataSearchPropertyValuesController(IPropertyValuesPersistentSource propertyValuesPersistentSource,
            IRolePrivilegesChecker rolePrivilegesChecker, ISecurityPrivilegesChecker securityPrivilegesChecker)
        {
            _rolePrivilegesChecker = rolePrivilegesChecker;
            _propertyValuesPersistentSource = propertyValuesPersistentSource;
            _securityPrivilegesChecker = securityPrivilegesChecker;
        }

        /// <summary>
        /// Get list of Property Values by Page
        /// </summary>
        /// <param name="operators"></param>
        /// <param name="page"></param>
        /// <param name="limit"></param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(GetScopedPropertyValuesResponseDto))]
        [SwaggerResponse(StatusCodes.Status400BadRequest, Type = typeof(string))]
        [HttpPut]
        public IActionResult Put([FromBody] PagedDataOperators operators, int page = 1, int limit = 50)
        {
            var getScopedPropertyValuesResponseDto = _propertyValuesPersistentSource.GetPropertyValuesForSearchValueByPage(limit,
                page, operators, User);

            if (getScopedPropertyValuesResponseDto?.Items == null || getScopedPropertyValuesResponseDto.Items.Count == 0)
                return Ok(getScopedPropertyValuesResponseDto);

            var canReadCache = new Dictionary<string, bool>(System.StringComparer.OrdinalIgnoreCase);

            foreach (var item in getScopedPropertyValuesResponseDto.Items)
            {
                var scopeName = item.PropertyValueScope ?? string.Empty;

                bool canReadSecrets = false;
                if (!string.IsNullOrWhiteSpace(scopeName))
                {
                    if (!canReadCache.TryGetValue(scopeName, out canReadSecrets))
                    {
                        canReadSecrets = _securityPrivilegesChecker.CanReadSecrets(User, scopeName);
                        canReadCache[scopeName] = canReadSecrets;
                    }
                }

                if (item.Secure == true && !canReadSecrets)
                {
                    item.PropertyValue = null;
                }
            }

            if (_rolePrivilegesChecker.IsAdmin(User))
            {
                foreach (var item in getScopedPropertyValuesResponseDto.Items)
                    item.UserEditable = true;
            }

            return Ok(getScopedPropertyValuesResponseDto);
        }
    }
}
