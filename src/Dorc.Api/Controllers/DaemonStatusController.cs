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
        private readonly IDaemonStatusProbe _daemonStatusProbe;
        private readonly ISecurityPrivilegesChecker _securityPrivilegesChecker;
        private readonly IEnvironmentsPersistentSource _environmentsPersistentSource;
        private readonly IEnvironmentMapper _environmentMapper;

        public DaemonStatusController(IDaemonStatusProbe daemonStatusProbe, ISecurityPrivilegesChecker securityPrivilegesChecker,
            IEnvironmentsPersistentSource environmentsPersistentSource, IEnvironmentMapper environmentMapper)
        {
            _environmentMapper = environmentMapper;
            _environmentsPersistentSource = environmentsPersistentSource;
            _securityPrivilegesChecker = securityPrivilegesChecker;
            _daemonStatusProbe = daemonStatusProbe;
        }

        /// <summary>
        ///     Get app servers daemon statuses
        /// </summary>
        /// <param name="id">Environment ID</param>
        /// <returns></returns>
        [HttpGet]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(IEnumerable<DaemonStatusApiModel>))]
        public IActionResult Get(int id)
        {
            if (id == 0)
                return StatusCode(StatusCodes.Status400BadRequest, "Environment ID is not valid!");

            var result = _daemonStatusProbe.GetDaemonStatuses(id)
                .Select(DaemonStatusMapping.ToApi)
                .ToList();

            return StatusCode(StatusCodes.Status200OK, result);
        }

        /// <summary>
        /// Get app servers daemon statuses for specified environment
        /// </summary>
        /// <param name="envName"></param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(IEnumerable<DaemonStatusApiModel>))]
        [HttpGet]
        [Route("{envName}")]
        public IActionResult Get(string envName)
        {
            if (string.IsNullOrEmpty(envName))
                return StatusCode(StatusCodes.Status400BadRequest, "Environment name is not valid!");

            var result = _daemonStatusProbe.GetDaemonStatuses(envName, User)
                .Select(DaemonStatusMapping.ToApi)
                .ToList();

            return StatusCode(StatusCodes.Status200OK, result);
        }

        /// <summary>
        ///     Change daemon state. Returns new daemon status.
        /// </summary>
        /// <param name="value">json string containing DaemonStatusApiModel object.</param>
        /// <returns>New DaemonStatusApiModel object</returns>
        [HttpPut]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(DaemonStatusApiModel))]
        [SwaggerResponse(StatusCodes.Status503ServiceUnavailable, Type = typeof(string))]
        public IActionResult PutDaemonState([FromBody] DaemonStatusApiModel value)
        {
            try
            {
                var environmentDetailsApiModel = _environmentsPersistentSource.GetEnvironment(value.EnvName, User);
                if (environmentDetailsApiModel == null || !_securityPrivilegesChecker.CanModifyEnvironment(User, environmentDetailsApiModel.EnvironmentName))
                    return StatusCode(StatusCodes.Status403Forbidden,
                        new NonEnoughRightsException("User doesn't have \"Write\" rights for this action!"));

                var result = _daemonStatusProbe.ChangeDaemonState(DaemonStatusMapping.ToCore(value), User);
                return StatusCode(StatusCodes.Status200OK,
                    result == null ? null : DaemonStatusMapping.ToApi(result));
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
        /// Discover all daemons on environment servers and automatically create daemon-server mappings
        /// </summary>
        /// <param name="envName">Environment name</param>
        /// <returns>Discovery result with statistics</returns>
        [HttpGet("discover/{envName}")]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(DiscoverDaemonsResult))]
        [SwaggerResponse(StatusCodes.Status400BadRequest, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status403Forbidden, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status404NotFound, Type = typeof(string))]
        public async Task<IActionResult> DiscoverDaemons(string envName)
        {
            if (string.IsNullOrEmpty(envName))
                return BadRequest("Environment name is required");

            try
            {
                // Check environment exists and user has access
                var environment = _environmentsPersistentSource.GetEnvironment(envName, User);
                if (environment == null)
                {
                    return NotFound($"Environment '{envName}' not found");
                }

                // Check permissions
                if (!_securityPrivilegesChecker.CanModifyEnvironment(User, envName))
                {
                    return StatusCode(StatusCodes.Status403Forbidden,
                        "Daemon discovery can only be performed by users with Write permissions or higher!");
                }

                // Call the probe to discover and map daemons
                var result = await Task.Run(() =>
                    _daemonStatusProbe.DiscoverAndMapDaemons(envName, User));

                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError,
                    $"An error occurred during daemon discovery: {ex.Message}");
            }
        }
    }
}
