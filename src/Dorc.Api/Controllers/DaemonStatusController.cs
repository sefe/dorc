using Dorc.Api.Interfaces;
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
    public class DaemonStatusController : ControllerBase
    {
        private readonly IApiServices _apiServices;
        private readonly ISecurityPrivilegesChecker _securityPrivilegesChecker;
        private readonly IEnvironmentsPersistentSource _environmentsPersistentSource;
        private readonly IEnvironmentMapper _environmentMapper;
        private readonly IServiceStatus _serviceStatus;

        public DaemonStatusController(
            IApiServices apiServices,
            ISecurityPrivilegesChecker securityPrivilegesChecker,
            IEnvironmentsPersistentSource environmentsPersistentSource,
            IEnvironmentMapper environmentMapper,
            IServiceStatus serviceStatus)
        {
            _environmentMapper = environmentMapper;
            _environmentsPersistentSource = environmentsPersistentSource;
            _securityPrivilegesChecker = securityPrivilegesChecker;
            _apiServices = apiServices;
            _serviceStatus = serviceStatus;
        }

        /// <summary>
        ///     Get app servers service statuses
        /// </summary>
        /// <param name="id">Environment ID</param>
        /// <returns></returns>
        [HttpGet]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(IEnumerable<ServiceStatusApiModel>))]
        public IActionResult Get(int id)
        {
            if (id == 0)
                return StatusCode(StatusCodes.Status400BadRequest, "Environment ID is not valid!");

            var result = _apiServices.GetEnvDaemonsStatuses(id);

            return StatusCode(StatusCodes.Status200OK, result);
        }

        /// <summary>
        /// Get app servers service statuses for specified environment
        /// </summary>
        /// <param name="envName"></param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(IEnumerable<ServiceStatusApiModel>))]
        [HttpGet]
        [Route("{envName}")]
        public IActionResult Get(string envName)
        {
            if (string.IsNullOrEmpty(envName))
                return StatusCode(StatusCodes.Status400BadRequest, "Environment name is not valid!");

            var result = _apiServices.GetEnvDaemonsStatuses(envName, User);

            return StatusCode(StatusCodes.Status200OK, result);
        }

        /// <summary>
        ///     Change service state. Returns new service status.
        /// </summary>
        /// <param name="value">json string containing ServiceStatusApiModel object.</param>
        /// <returns>New ServiceStatusApiModel object</returns>
        [HttpPut]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(ServiceStatusApiModel))]
        [SwaggerResponse(StatusCodes.Status503ServiceUnavailable, Type = typeof(string))]
        public IActionResult PutServiceState([FromBody] ServiceStatusApiModel value)
        {
            try
            {
                var environmentDetailsApiModel = _environmentsPersistentSource.GetEnvironment(value.EnvName, User);
                if (environmentDetailsApiModel == null || !_securityPrivilegesChecker.CanModifyEnvironment(User, environmentDetailsApiModel.EnvironmentName))
                    return StatusCode(StatusCodes.Status403Forbidden,
                        new NonEnoughRightsException("User doesn't have \"Write\" rights for this action!"));
                var result = _apiServices.ChangeServiceState(value, User);
                return StatusCode(StatusCodes.Status200OK, result);
            }
            catch (NonEnoughRightsException)
            {
                throw;
            }
            catch (Exception e)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    e.InnerException != null ? e.InnerException.Message : e.Message);
            }
        }

        /// <summary>
        ///     Discover and persist daemon mappings for a specific environment.
        ///     Probes all servers in the environment and maps confirmed running services.
        /// </summary>
        /// <param name="envName">Environment name</param>
        [HttpPost]
        [Route("discover/{envName}")]
        [SwaggerResponse(StatusCodes.Status200OK, Description = "Discovery completed for the specified environment.")]
        [SwaggerResponse(StatusCodes.Status400BadRequest)]
        [SwaggerResponse(StatusCodes.Status500InternalServerError)]
        public IActionResult DiscoverForEnvironment(string envName)
        {
            if (string.IsNullOrWhiteSpace(envName))
                return BadRequest("Environment name must not be empty.");

            try
            {
                _serviceStatus.GetServicesAndStatus(envName, User);
                return Ok($"Daemon discovery completed for environment '{envName}'.");
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError,
                    ex.InnerException?.Message ?? ex.Message);
            }
        }

        /// <summary>
        ///     Discover and persist daemon mappings for ALL environments.
        ///     Probes every known environment's servers and maps confirmed running services.
        ///     Intended for post-release automation (e.g. Postman).
        /// </summary>
        [HttpPost]
        [Route("discover")]
        [SwaggerResponse(StatusCodes.Status200OK, Description = "Discovery results per environment.")]
        public IActionResult DiscoverForAllEnvironments()
        {
            var envNames = _environmentsPersistentSource.GetEnvironmentNames(User).ToList();
            var results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var envName in envNames)
            {
                try
                {
                    _serviceStatus.GetServicesAndStatus(envName, User);
                    results[envName] = "OK";
                }
                catch (Exception ex)
                {
                    results[envName] = $"Failed: {ex.InnerException?.Message ?? ex.Message}";
                }
            }

            return Ok(results);
        }
    }
}
