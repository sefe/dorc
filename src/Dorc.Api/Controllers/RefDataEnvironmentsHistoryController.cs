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
    public class RefDataEnvironmentsHistoryController : ControllerBase
    {
        private readonly IEnvironmentHistoryPersistentSource _environmentHistoryPersistentSource;
        private readonly ISecurityPrivilegesChecker? _securityPrivilegesChecker;

        public RefDataEnvironmentsHistoryController(
            IEnvironmentHistoryPersistentSource environmentHistoryPersistentSource,
            ISecurityPrivilegesChecker? securityPrivilegesChecker = null)
        {
            _environmentHistoryPersistentSource = environmentHistoryPersistentSource;
            _securityPrivilegesChecker = securityPrivilegesChecker;
        }

        /// <summary>
        ///     Returns Environment history
        /// </summary>
        /// <param name="id">Environment ID</param>
        /// <returns>Json string with array of EnvironmentHistoryApiModel</returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(IEnumerable<EnvironmentHistoryApiModel>))]
        [HttpGet]
        public IActionResult Get(int id)
        {
            var history = _environmentHistoryPersistentSource.GetEnvironmentDetailHistory(id);
            return StatusCode(StatusCodes.Status200OK, history);
        }

        /// <summary>
        /// Edit Environment history comment
        /// </summary>
        /// <param name="history"></param>
        [HttpPut]
        public IActionResult Put([FromBody] EnvironmentHistoryApiModel history)
        {
            if (history == null)
            {
                return BadRequest("Environment history payload is required.");
            }

            if (_securityPrivilegesChecker != null)
            {
                var persistedEnvName = _environmentHistoryPersistentSource.GetEnvironmentNameForHistoryId(history.Id);
                if (string.IsNullOrWhiteSpace(persistedEnvName) ||
                    !_securityPrivilegesChecker.CanModifyEnvironment(User, persistedEnvName))
                {
                    return StatusCode(StatusCodes.Status403Forbidden,
                        $"You do not have permission to modify environment '{persistedEnvName ?? history.EnvName}'.");
                }
            }

            _environmentHistoryPersistentSource.UpdateEnvironmentDetailHistoryComment(history.Id, history.Comment);
            return Ok();
        }

        /// <summary>
        ///     Add a new environment history entry
        /// </summary>
        /// <param name="request">The history update request</param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(ApiBoolResult))]
        [SwaggerResponse(StatusCodes.Status400BadRequest, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status403Forbidden, Type = typeof(string))]
        [HttpPost]
        public IActionResult Post([FromBody] UpdateEnvironmentHistoryRequest request)
        {
            try
            {
                if (request == null || string.IsNullOrWhiteSpace(request.EnvName))
                {
                    return StatusCode(StatusCodes.Status400BadRequest, "Environment name is required.");
                }

                if (_securityPrivilegesChecker != null &&
                    !_securityPrivilegesChecker.CanModifyEnvironment(User, request.EnvName))
                {
                    return StatusCode(StatusCodes.Status403Forbidden,
                        $"You do not have permission to modify environment '{request.EnvName}'.");
                }

                var result = _environmentHistoryPersistentSource.UpdateHistory(
                    request.EnvName,
                    request.BackupFile,
                    request.Comment,
                    request.UpdatedBy,
                    request.UpdateType);
                return Ok(new ApiBoolResult { Result = result });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status400BadRequest, ex.Message);
            }
        }
    }
}