﻿using Dorc.ApiModel;
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

        public RefDataSearchPropertyValuesController(IPropertyValuesPersistentSource propertyValuesPersistentSource,
            IRolePrivilegesChecker rolePrivilegesChecker)
        {
            _rolePrivilegesChecker = rolePrivilegesChecker;
            _propertyValuesPersistentSource = propertyValuesPersistentSource;
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

            var isAdmin = _rolePrivilegesChecker.IsAdmin(User);
            if (!isAdmin)
                return StatusCode(StatusCodes.Status200OK, getScopedPropertyValuesResponseDto);

            foreach (var prop in getScopedPropertyValuesResponseDto.Items)
            {
                prop.UserEditable = true;
            }

            return StatusCode(StatusCodes.Status200OK, getScopedPropertyValuesResponseDto);
        }
    }
}
