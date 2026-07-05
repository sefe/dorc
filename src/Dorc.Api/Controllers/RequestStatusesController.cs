using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Dorc.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public sealed class RequestStatusesController : ControllerBase
    {
        private readonly IRequestsStatusPersistentSource _requestsStatusPersistentSource;
        private readonly IRequestsPersistentSource _requestsPersistentSource;
        private readonly IRolePrivilegesChecker _rolePrivilegesChecker;
        private readonly ISecurityPrivilegesChecker _securityPrivilegesChecker;
        private readonly ILogger _log;

        public RequestStatusesController(IRequestsPersistentSource requestsPersistentSource, IRequestsStatusPersistentSource requestsStatusPersistentSource, IRolePrivilegesChecker rolePrivilegesChecker, ISecurityPrivilegesChecker securityPrivilegesChecker, ILogger<RequestStatusesController> log)
        {
            _rolePrivilegesChecker = rolePrivilegesChecker;
            _securityPrivilegesChecker = securityPrivilegesChecker;
            _requestsPersistentSource = requestsPersistentSource;
            _requestsStatusPersistentSource = requestsStatusPersistentSource;
            _log = log;
        }

        /// <summary>
        /// Get the list of request statuses by Page
        /// </summary>
        /// <param name="operators"></param>
        /// <param name="page"></param>
        /// <param name="limit"></param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(GetRequestStatusesListResponseDto))]
        [HttpPut]
        public IActionResult Put([FromBody] PagedDataOperators operators, int page = 1, int limit = 50)
        {
            try
            {
                var requestStatusesListResponseDto = _requestsStatusPersistentSource.GetRequestStatusesByPage(limit,
                    page, operators, User);

                var isAdmin = _rolePrivilegesChecker.IsAdmin(User);
                if (!isAdmin)
                    return StatusCode(StatusCodes.Status200OK, requestStatusesListResponseDto);

                foreach (var prop in requestStatusesListResponseDto.Items)
                {
                    prop.UserEditable = true;
                }

                return StatusCode(StatusCodes.Status200OK, requestStatusesListResponseDto);
            }
            catch (ArgumentException exception)
            {
                return StatusCode(StatusCodes.Status400BadRequest, exception.Message);
            }
        }

        /// <summary>
        ///     Get the logs for the specified request ID
        /// </summary>
        /// <param name="requestId">Request ID</param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status404NotFound)]
        [HttpGet("Log")]
        public IActionResult GetLog(int requestId)
        {
            // Only return a log for a request the caller can resolve, mirroring the
            // neighbouring Get action rather than returning any request's log by id.
            var request = _requestsPersistentSource.GetRequestForUser(requestId, User);
            if (request == null)
            {
                return StatusCode(StatusCodes.Status404NotFound, $"The request with id {requestId} is not found.");
            }

            var result = _requestsPersistentSource.GetRequestLog(requestId);

            return StatusCode(StatusCodes.Status200OK, result);
        }

        /// <summary>
        ///     Get the logs for the specified request ID
        /// </summary>
        /// <param name="requestId">Request ID</param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(DeploymentRequestApiModel))]
        [SwaggerResponse(StatusCodes.Status404NotFound, Type = typeof(DeploymentRequestApiModel))]
        [SwaggerResponse(StatusCodes.Status500InternalServerError, Type = typeof(DeploymentRequestApiModel))]
        [HttpGet]
        public IActionResult Get(int requestId)
        {
            DeploymentRequestApiModel? result = null;

            try
            {
                result = _requestsPersistentSource.GetRequestForUser(requestId, User);
            }
            catch (Exception exception)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, exception.ToString());
            }

            if (result == null)
            {
                return StatusCode(StatusCodes.Status404NotFound, $"The request with id {requestId} is not found.");
            }

            var isAdmin = _rolePrivilegesChecker.IsAdmin(User);
            if (isAdmin)
            {
                result.UserEditable = true;
            }

            return StatusCode(StatusCodes.Status200OK, result);
        }

        [SwaggerResponse(StatusCodes.Status403Forbidden)]
        [SwaggerResponse(StatusCodes.Status404NotFound)]
        [HttpPatch]
        public IActionResult Patch(int requestId, int deploymentResultId, [FromBody] string log)
        {
            // Authorize on the request that owns the deployment result being
            // mutated, NOT on the caller-supplied requestId (which the mutation
            // ignores): otherwise a caller could pair a requestId they own with a
            // deploymentResultId they do not.
            var deploymentResult = _requestsPersistentSource.GetDeploymentResults(deploymentResultId);
            if (deploymentResult == null)
            {
                return StatusCode(StatusCodes.Status404NotFound, $"Deployment result with id {deploymentResultId} is not found.");
            }

            if (!CanModifyOwningEnvironment(deploymentResult.RequestId))
            {
                return StatusCode(StatusCodes.Status403Forbidden, "You are not authorized to modify this deployment's log.");
            }

            _log.LogDebug($"Calling Append to log method {log}");
            _requestsStatusPersistentSource.AppendLogToJob(deploymentResultId, log);
            _log.LogDebug($"Calling Append to log method {log}...Done");
            return Ok();
        }

        [SwaggerResponse(StatusCodes.Status400BadRequest)]
        [SwaggerResponse(StatusCodes.Status403Forbidden)]
        [SwaggerResponse(StatusCodes.Status404NotFound)]
        [HttpPost("RawLog")]
        public IActionResult Post(int requestId, string uncLogPath)
        {
            if (!IsValidUncPath(uncLogPath))
            {
                return StatusCode(StatusCodes.Status400BadRequest, "uncLogPath must be a well-formed UNC path (\\\\server\\share\\...).");
            }

            if (!CanModifyOwningEnvironment(requestId))
            {
                return StatusCode(StatusCodes.Status403Forbidden, "You are not authorized to modify this request's log path.");
            }

            _requestsStatusPersistentSource.SetUncLogPathforRequest(requestId, uncLogPath);

            return Ok();
        }

        /// <summary>
        /// Returns true when the caller may modify the environment that owns the
        /// given request. Fails closed when the request/environment cannot be
        /// resolved.
        /// </summary>
        private bool CanModifyOwningEnvironment(int requestId)
        {
            var request = _requestsPersistentSource.GetRequestForUser(requestId, User);
            if (request == null || string.IsNullOrEmpty(request.EnvironmentName))
            {
                return false;
            }

            return _securityPrivilegesChecker.CanModifyEnvironment(User, request.EnvironmentName);
        }

        /// <summary>
        /// Minimal UNC-path validation: must start with two backslashes and name a
        /// server and share, and must not contain characters invalid in a path.
        /// Prevents repointing a request's log at an arbitrary local path or URL.
        /// </summary>
        public static bool IsValidUncPath(string uncLogPath)
        {
            if (string.IsNullOrWhiteSpace(uncLogPath))
                return false;
            if (!uncLogPath.StartsWith(@"\\"))
                return false;
            // Reject any character that is not valid in a Windows path.
            if (uncLogPath.IndexOfAny(System.IO.Path.GetInvalidPathChars()) >= 0)
                return false;
            // Require \\server\share (at least a server and a share segment).
            var remainder = uncLogPath.Substring(2);
            var segments = remainder.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            return segments.Length >= 2;
        }
    }
}
