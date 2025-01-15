﻿using Dorc.Api.Interfaces;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Sources.Interfaces;
using log4net;
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
        private readonly ILog _log;
        private readonly IRequestsManager _requestsManager;
        private readonly IRequestsPersistentSource _requestsPersistentSource;
        private readonly IProjectsPersistentSource _projectsPersistentSource;

        public RequestController(IRequestService service, ISecurityPrivilegesChecker apiSecurityService, ILog log,
            IRequestsManager requestsManager, IRequestsPersistentSource requestsPersistentSource, IProjectsPersistentSource projectsPersistentSource)
        {
            _projectsPersistentSource = projectsPersistentSource;
            _requestsPersistentSource = requestsPersistentSource;
            _requestsManager = requestsManager;
            _service = service;
            _apiSecurityService = apiSecurityService;
            _log = log;
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
                _log.Error("api/Request/:id", e);
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
                _log.Error("api/Request/BuildDefinitions", e);
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
                _log.Error("api/Request/Builds", e);
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
                _log.Error("api/Request/Components", e);
                var result = StatusCode(StatusCodes.Status500InternalServerError, e);
                return result;
            }
        }

        /// <summary>
        /// Restarts an existing request
        /// </summary>
        /// <param name="requestId"></param>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(RequestStatusDto))]
        [SwaggerResponse(StatusCodes.Status400BadRequest, Type = typeof(RequestStatusDto))]
        [Route("restart")]
        [HttpPost]
        public IActionResult RestartPost(int requestId)
        {
            try
            {
                var deploymentRequest = _requestsPersistentSource.GetRequestForUser(requestId, User);

                var canModifyEnv = _apiSecurityService.CanModifyEnvironment(User, deploymentRequest.EnvironmentName);
                if (!canModifyEnv)
                {
                    _log.Info($"Forbidden request to restart {requestId} for {deploymentRequest.EnvironmentName} from {User.Identity.Name}");
                    return StatusCode(StatusCodes.Status403Forbidden,
                        $"Forbidden request to {deploymentRequest.EnvironmentName} from {User.Identity.Name}");
                }

                if (deploymentRequest.Status != DeploymentRequestStatus.Cancelling.ToString()
                    || deploymentRequest.Status != DeploymentRequestStatus.Cancelled.ToString())
                    _requestsPersistentSource.UpdateRequestStatus(
                        requestId,
                        DeploymentRequestStatus.Restarting,
                        User.Identity.Name);

                var updated = _requestsPersistentSource.GetRequestForUser(requestId, User);

                return StatusCode(StatusCodes.Status200OK,
                    new RequestStatusDto { Id = updated.Id, Status = updated.Status.ToString() });
            }
            catch (Exception e)
            {
                _log.Error("api/Request/restart", e);
                var result = StatusCode(StatusCodes.Status500InternalServerError, e);
                return result;
            }
        }


        /// <summary>
        /// Creates new request
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
                    _log.Info($"Forbidden request to cancel {requestId} for {deploymentRequest.EnvironmentName} from {User.Identity.Name}");
                    return StatusCode(StatusCodes.Status403Forbidden,
                        $"Forbidden request to {deploymentRequest.EnvironmentName} from {User.Identity.Name}");
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

                return StatusCode(StatusCodes.Status200OK,
                    new RequestStatusDto { Id = updated.Id, Status = updated.Status.ToString() });
            }
            catch (Exception e)
            {
                _log.Error("api/Request/cancel", e);
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
                    _log.Info($"Forbidden request to {requestDto.Environment} from {User.Identity.Name}");
                    return StatusCode(StatusCodes.Status403Forbidden,
                            $"Forbidden request to {requestDto.Environment} from {User.Identity.Name}");
                }

                try
                {
                    var result = _service.CreateRequest(requestDto, User);
                    if (result == null)
                        return BadRequest("Failed to create request");

                    if (result.Id <= 0)
                        return BadRequest(result.Status);

                    _log.Info($"Request {result.Id} created");
                    return Ok(result);
                }
                catch (Exception e)
                {
                    _log.Error(e.Message);
                    return BadRequest(e.Message);
                }
            }
            catch (Exception e)
            {
                _log.Error("api/Request/post", e);
                var result = StatusCode(StatusCodes.Status500InternalServerError, e);
                return result;
            }
        }
    }
}