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
        private readonly IServiceStatus _serviceStatus;
        private readonly ISecurityPrivilegesChecker _securityPrivilegesChecker;
        private readonly IEnvironmentsPersistentSource _environmentsPersistentSource;
        private readonly IEnvironmentMapper _environmentMapper;

        public DaemonStatusController(IServiceStatus serviceStatus, ISecurityPrivilegesChecker securityPrivilegesChecker,
            IEnvironmentsPersistentSource environmentsPersistentSource, IEnvironmentMapper environmentMapper)
        {
            _environmentMapper = environmentMapper;
            _environmentsPersistentSource = environmentsPersistentSource;
            _securityPrivilegesChecker = securityPrivilegesChecker;
            _serviceStatus = serviceStatus;
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

            var result = _serviceStatus.GetDaemonStatuses(id)
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

            var result = _serviceStatus.GetDaemonStatuses(envName, User)
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

                var result = _serviceStatus.ChangeDaemonState(DaemonStatusMapping.ToCore(value), User);
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
    }
}
