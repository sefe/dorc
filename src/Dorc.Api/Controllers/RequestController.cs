using Dorc.Api.Interfaces;
using Dorc.ApiModel;
using Dorc.Core.Events;
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
    public class RequestController : ControllerBase
    {
        private readonly IRequestService _service;
        private readonly ISecurityPrivilegesChecker _apiSecurityService;
        private readonly ILogger _log;
        private readonly IRequestsManager _requestsManager;
        private readonly IRequestsPersistentSource _requestsPersistentSource;
        private readonly IProjectsPersistentSource _projectsPersistentSource;
        private readonly IClaimsPrincipalReader _claimsPrincipalReader;
        private readonly IDeploymentEventsPublisher _deploymentEventsPublisher;

        public RequestController(IRequestService service, ISecurityPrivilegesChecker apiSecurityService, ILogger<RequestController> log,
            IRequestsManager requestsManager, IRequestsPersistentSource requestsPersistentSource,
            IProjectsPersistentSource projectsPersistentSource,
            IClaimsPrincipalReader claimsPrincipalReader,
            IDeploymentEventsPublisher deploymentEventsPublisher
            )
        {
            _projectsPersistentSource = projectsPersistentSource;
            _requestsPersistentSource = requestsPersistentSource;
            _requestsManager = requestsManager;
            _service = service;
            _apiSecurityService = apiSecurityService;
            _log = log;
            _claimsPrincipalReader = claimsPrincipalReader;
            _deploymentEventsPublisher = deploymentEventsPublisher;
        }

        /// <summary>
        /// Gets request status
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(RequestStatusDto))]
        [HttpGet]
        public IActionResult Get(int id)
        {
            try
            {
                var requestStatus = _requestsPersistentSource.GetRequestStatus(id);
                if (requestStatus == null)
                {
                    return NotFound();
                }

                return Ok(requestStatus);
            }
            catch (Exception e)
            {
                _log.LogError(e, "api/Request/:id");
                var result = StatusCode(StatusCodes.Status500InternalServerError, e);
                return result;
            }
        }

        /// <summary>
        /// Gets request status
        /// </summary>
        /// <param name="projectId"></param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(List<DeployArtefactDto>))]
        [Route("BuildDefinitions")]
        [HttpGet]
        public IActionResult GetBuildDefinitions(int projectId)
        {
            try
            {
                var project = _projectsPersistentSource.GetProject(projectId);

                var deplorableArtifacts = _requestsManager.GetBuildDefinitions(project);

                if (deplorableArtifacts == null || !deplorableArtifacts.Any())
                {
                    return StatusCode(StatusCodes.Status404NotFound,
                        $"Unable to locate any build definitions for project '{project.ProjectName}' with regex '{project.ArtefactsBuildRegex}'");
                }

                var output = deplorableArtifacts
                    .Select(artifact => new DeployArtefactDto { Id = artifact.Id, Name = artifact.Name }).ToList();

                return Ok(output);
            }
            catch (Exception e)
            {
                _log.LogError(e, "api/Request/BuildDefinitions");
                var unrollException = UnrollException(e);
                var result = StatusCode(StatusCodes.Status500InternalServerError, unrollException);
                return result;
            }
        }

        /// <summary>
        /// Gets request status
        /// </summary>
        /// <param name="projectId"></param>
        /// <param name="environment"></param>
        /// <param name="buildDefinitionName"></param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(List<DeployArtefactDto>))]
        [Route("Builds")]
        [HttpGet]
        public IActionResult GetBuilds(int projectId, string? environment, string? buildDefinitionName)
        {
            try
            {
                var artifacts =
                    _requestsManager.GetBuildsAsync(projectId, environment, buildDefinitionName).Result;

                if (artifacts == null)
                {
                    return NotFound();
                }

                var output = artifacts
                    .Select(artifact => new DeployArtefactDto { Id = artifact.Id, Name = artifact.Name }).ToList();

                return Ok(output);
            }
            catch (Exception e)
            {
                _log.LogError(e, "api/Request/Builds");
                var unrollException = UnrollException(e);
                var result = StatusCode(StatusCodes.Status500InternalServerError, unrollException);
                return result;
            }
        }

        private static Exception UnrollException(Exception e)
        {
            return e is AggregateException && e.InnerException != null
                ? UnrollException(e.InnerException)
                : e;
        }

        /// <summary>
        /// Gets request status
        /// </summary>
        /// <param name="projectId"></param>
        /// <param name="parentId"></param>
        /// <returns></returns>`
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(List<DeployComponentDto>))]
        [Route("Components")]
        [HttpGet]
        public IActionResult GetComponents(int projectId, int? parentId = null)
        {
            try
            {
                var comps = parentId == null
                    ? _requestsManager.GetComponents(projectId)
                    : _requestsManager.GetComponents(projectId, parentId);

                var output = comps
                    .Select(component => new DeployComponentDto
                    {
                        Id = component.Id,
                        Name = component.Name,
                        NumOfChildren = component.NumOfChildren,
                        IsEnabled = component.IsEnabled,
                        Description = component.Description,
                        ParentId = component.ParentId
                    }).ToList();

                return Ok(output);
            }
            catch (Exception e)
            {
                _log.LogError(e, "api/Request/Components");
                var result = StatusCode(StatusCodes.Status500InternalServerError, e);
                return result;
            }
        }

        /// <summary>
        /// Restarts an existing request by cloning it
        /// </summary>
        /// <param name="requestId"></param>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(RequestStatusDto))]
        [SwaggerResponse(StatusCodes.Status400BadRequest, Type = typeof(RequestStatusDto))]
        [Route("redeploy")]
        [HttpPost]
        public IActionResult RedeployPost(int requestId)
        {
            try
            {
                var deploymentRequest = _requestsPersistentSource.GetRequestForUser(requestId, User);

                var canModifyEnv = _apiSecurityService.CanModifyEnvironment(User, deploymentRequest.EnvironmentName);
                string username = _claimsPrincipalReader.GetUserFullDomainName(User);
                if (!canModifyEnv)
                {
                    _log.LogInformation($"Forbidden request to redeploy {requestId} for {deploymentRequest.EnvironmentName} from {username}");
                    return StatusCode(StatusCodes.Status403Forbidden,
                        $"Forbidden request to {deploymentRequest.EnvironmentName} from {username}");
                }

                // Clone the request instead of modifying the existing one
                var newRequestId = _requestsPersistentSource.CloneRequest(requestId, username);

                _log.LogInformation($"Request {requestId} cloned as new request {newRequestId} by {username}");
                var updated = _requestsPersistentSource.GetRequestForUser(requestId, User);

                // Broadcast redeploy -> Redeploying
                _ = _deploymentEventsPublisher.PublishRequestStatusChangedAsync(
                    new DeploymentRequestEventData(updated));

                return StatusCode(StatusCodes.Status200OK,
                    new RequestStatusDto { Id = newRequestId, Status = "Pending" });
            }
            catch (Exception e)
            {
                _log.LogError(e, "api/Request/redeploy");
                var result = StatusCode(StatusCodes.Status500InternalServerError, e);
                return result;
            }
        }


        /// <summary>
        /// Cancel request
        /// </summary>
        /// <param name="requestId"></param>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(RequestStatusDto))]
        [SwaggerResponse(StatusCodes.Status400BadRequest, Type = typeof(RequestStatusDto))]
        [Route("cancel")]
        [HttpPut]
        public IActionResult CancelPut(int requestId)
        {
            try
            {
                var deploymentRequest = _requestsPersistentSource.GetRequestForUser(requestId, User);

                var canModifyEnv = _apiSecurityService.CanModifyEnvironment(User, deploymentRequest.EnvironmentName);
                if (!canModifyEnv)
                {
                    string username = _claimsPrincipalReader.GetUserFullDomainName(User);
                    _log.LogInformation($"Forbidden request to cancel {requestId} for {deploymentRequest.EnvironmentName} from {username}");
                    return StatusCode(StatusCodes.Status403Forbidden,
                        $"Forbidden request to {deploymentRequest.EnvironmentName} from {username}");
                }

                if (deploymentRequest.Status == DeploymentRequestStatus.Running.ToString()
                    || deploymentRequest.Status == DeploymentRequestStatus.Requesting.ToString())
                {
                    _requestsPersistentSource.UpdateRequestStatus(requestId, DeploymentRequestStatus.Cancelling);
                }

                if (deploymentRequest.Status == DeploymentRequestStatus.Pending.ToString()
                    || deploymentRequest.Status == DeploymentRequestStatus.Restarting.ToString())
                {
                    _requestsPersistentSource.UpdateRequestStatus(requestId, DeploymentRequestStatus.Cancelled);
                }

                var updated = _requestsPersistentSource.GetRequestForUser(requestId, User);

                // Broadcast cancel -> Cancelling/Cancelled
                _ = _deploymentEventsPublisher.PublishRequestStatusChangedAsync(
                    new DeploymentRequestEventData(updated));

                return StatusCode(StatusCodes.Status200OK,
                    new RequestStatusDto { Id = updated.Id, Status = updated.Status.ToString() });
            }
            catch (Exception e)
            {
                _log.LogError(e, "api/Request/cancel");
                var result = StatusCode(StatusCodes.Status500InternalServerError, e);
                return result;
            }
        }

        /// <summary>
        /// Creates new request
        /// </summary>
        /// <param name="requestDto"></param>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(RequestStatusDto))]
        [SwaggerResponse(StatusCodes.Status400BadRequest, Type = typeof(RequestStatusDto))]
        [SwaggerResponse(StatusCodes.Status403Forbidden, Type = typeof(string))]
        [HttpPost]
        public IActionResult Post([FromBody] RequestDto requestDto)
        {
            try
            {
                var canModifyEnv = _apiSecurityService.CanModifyEnvironment(User, requestDto.Environment);
                if (!canModifyEnv)
                {
                    string username = _claimsPrincipalReader.GetUserFullDomainName(User);
                    _log.LogInformation($"Forbidden request to {requestDto.Environment} from {username}");
                    return StatusCode(StatusCodes.Status403Forbidden,
                            $"Forbidden request to {requestDto.Environment} from {username}");
                }

                try
                {
                    var result = _service.CreateRequest(requestDto, User);
                    if (result == null)
                        return BadRequest("Failed to create request");

                    if (result.Id <= 0)
                        return BadRequest(result.Status);

                    _log.LogInformation($"Request {result.Id} created");

                    return Ok(result);
                }
                catch (Exception e)
                {
                    _log.LogError(e.Message);
                    return BadRequest(e.Message);
                }
            }
            catch (Exception e)
            {
                _log.LogError(e, "api/Request/post");
                var result = StatusCode(StatusCodes.Status500InternalServerError, e);
                return result;
            }
        }
    }
}