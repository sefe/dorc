using Dorc.ApiModel;
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
        private readonly ILogger _log;

        public RequestStatusesController(IRequestsPersistentSource requestsPersistentSource, IRequestsStatusPersistentSource requestsStatusPersistentSource, IRolePrivilegesChecker rolePrivilegesChecker, ILogger<RequestStatusesController> log)
        {
            _rolePrivilegesChecker = rolePrivilegesChecker;
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
        [HttpGet("Log")]
        public IActionResult GetLog(int requestId)
        {
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

        [HttpPatch]
        public IActionResult Patch(int requestId, int deploymentResultId, [FromBody] string log)
        {
            _log.LogDebug($"Calling Append to log method {log}");
            _requestsStatusPersistentSource.AppendLogToJob(deploymentResultId, log);
            _log.LogDebug($"Calling Append to log method {log}...Done");
            return Ok();
        }

        [HttpPost("RawLog")]
        public IActionResult Post(int requestId, string uncLogPath)
        {
            _requestsStatusPersistentSource.SetUncLogPathforRequest(requestId, uncLogPath);

            return Ok();
        }
    }
}
