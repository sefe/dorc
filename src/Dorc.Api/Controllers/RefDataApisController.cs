using System.Net;
using Dorc.Api.Services;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Dorc.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class RefDataApisController : ControllerBase
    {
        private readonly IApisPersistentSource _apisPersistentSource;
        private readonly IEnvironmentsPersistentSource _environmentsPersistentSource;
        private readonly ISecurityPrivilegesChecker _securityPrivilegesChecker;
        private readonly IApiEndpointResolver _apiEndpointResolver;

        public RefDataApisController(
            IApisPersistentSource apisPersistentSource,
            IEnvironmentsPersistentSource environmentsPersistentSource,
            ISecurityPrivilegesChecker securityPrivilegesChecker,
            IApiEndpointResolver apiEndpointResolver)
        {
            _apisPersistentSource = apisPersistentSource;
            _environmentsPersistentSource = environmentsPersistentSource;
            _securityPrivilegesChecker = securityPrivilegesChecker;
            _apiEndpointResolver = apiEndpointResolver;
        }

        /// <summary>
        ///     Return APIs scoped to an environment, with endpoint resolution applied.
        /// </summary>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(List<ApiApiModel>))]
        [HttpGet]
        [Route("ForEnvironment/{environmentId}")]
        public IActionResult GetForEnvironment(int environmentId)
        {
            if (environmentId <= 0)
                return BadRequest("environmentId must be greater than zero.");

            var env = _environmentsPersistentSource.GetEnvironment(environmentId, User);
            if (env == null)
                return NotFound($"Environment {environmentId} not found.");

            var apis = _apisPersistentSource.GetApisForEnvId(environmentId).ToList();
            _apiEndpointResolver.ResolveEndpoints(apis, env.EnvironmentName);
            foreach (var api in apis)
                api.UserEditable = env.UserEditable;
            return Ok(apis);
        }

        /// <summary>
        ///     Return a single API. Returns NotFound if the caller cannot
        ///     access the API's environment, so the existence of an API row
        ///     is not leaked across environment-permission boundaries.
        /// </summary>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(ApiApiModel))]
        [HttpGet]
        [Route("{id}")]
        public IActionResult Get(int id)
        {
            if (id <= 0)
                return BadRequest("id must be greater than zero.");

            var api = _apisPersistentSource.GetApi(id);
            if (api == null)
                return NotFound();

            var env = _environmentsPersistentSource.GetEnvironment(api.EnvironmentId, User);
            if (env == null)
                return NotFound();

            _apiEndpointResolver.ResolveEndpoint(api, env.EnvironmentName);
            api.UserEditable = env.UserEditable;

            return Ok(api);
        }

        /// <summary>
        ///     Create a new API for an environment.
        /// </summary>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(ApiApiModel))]
        [HttpPost]
        public IActionResult Post([FromQuery] int environmentId, [FromBody] ApiApiModel model)
        {
            try
            {
                if (environmentId <= 0)
                    return BadRequest("environmentId must be greater than zero.");

                var env = _environmentsPersistentSource.GetEnvironment(environmentId, User);
                if (env == null)
                    return NotFound($"Environment {environmentId} not found.");

                if (!_securityPrivilegesChecker.CanModifyEnvironment(User, env.EnvironmentName))
                    return StatusCode((int)HttpStatusCode.Forbidden,
                        $"User does not have write permission on {env.EnvironmentName}.");

                var created = _apisPersistentSource.AddApi(environmentId, model, User);
                _apiEndpointResolver.ResolveEndpoint(created, env.EnvironmentName);
                created.UserEditable = env.UserEditable;
                return Ok(created);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        /// <summary>
        ///     Update an existing API.
        /// </summary>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(ApiApiModel))]
        [HttpPut]
        public IActionResult Put([FromBody] ApiApiModel model)
        {
            try
            {
                if (model.Id <= 0)
                    return BadRequest("Api Id must be greater than zero.");

                var existing = _apisPersistentSource.GetApi(model.Id);
                if (existing == null)
                    return NotFound();

                var env = _environmentsPersistentSource.GetEnvironment(existing.EnvironmentId, User);
                if (env == null)
                    return NotFound($"Environment {existing.EnvironmentId} not found.");

                if (!_securityPrivilegesChecker.CanModifyEnvironment(User, env.EnvironmentName))
                    return StatusCode((int)HttpStatusCode.Forbidden,
                        $"User does not have write permission on {env.EnvironmentName}.");

                var updated = _apisPersistentSource.UpdateApi(model, User);
                if (updated == null)
                    return NotFound();

                _apiEndpointResolver.ResolveEndpoint(updated, env.EnvironmentName);
                updated.UserEditable = env.UserEditable;
                return Ok(updated);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        /// <summary>
        ///     Delete an API.
        /// </summary>
        [Produces("application/json")]
        [ProducesResponseType(typeof(ApiBoolResult), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(string), StatusCodes.Status403Forbidden)]
        [HttpDelete]
        public IActionResult Delete(int id)
        {
            if (id <= 0)
                return BadRequest("id must be greater than zero.");

            var existing = _apisPersistentSource.GetApi(id);
            if (existing == null)
                return NotFound();

            var env = _environmentsPersistentSource.GetEnvironment(existing.EnvironmentId, User);
            if (env == null)
                return NotFound($"Environment {existing.EnvironmentId} not found.");

            if (!_securityPrivilegesChecker.CanModifyEnvironment(User, env.EnvironmentName))
                return StatusCode((int)HttpStatusCode.Forbidden,
                    $"User does not have write permission on {env.EnvironmentName}.");

            var ok = _apisPersistentSource.DeleteApi(id, User);
            return ok
                ? Ok(new ApiBoolResult { Result = true })
                : BadRequest("Delete failed.");
        }
    }
}
