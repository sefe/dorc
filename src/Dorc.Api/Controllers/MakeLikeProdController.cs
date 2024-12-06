using System.Runtime.Versioning;
using System.Security.Claims;
using Dorc.Api.Model;
using Dorc.ApiModel;
using Dorc.Core;
using Dorc.Core.Interfaces;
using Dorc.Core.VariableResolution;
using Dorc.PersistentData.Sources.Interfaces;
using log4net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Dorc.Api.Controllers
{
    [Authorize]
    [SupportedOSPlatform("windows")]
    [ApiController]
    [Route("[controller]")]
    public class MakeLikeProdController : ControllerBase
    {
        private readonly ILog _logger;
        private readonly IDeployLibrary _deployLibrary;
        private readonly IEnvironmentsPersistentSource _environmentsPersistentSource;
        private readonly ISecurityPrivilegesChecker _securityPrivilegesChecker;
        private readonly IEnvBackups _envBackups;
        private readonly IActiveDirectorySearcher _activeDirectorySearcher;
        private readonly IBundledRequestsPersistentSource _bundledRequestsPersistentSource;
        private readonly IVariableResolver _variableResolver;
        private readonly IBundledRequestVariableLoader _bundledRequestVariableLoader;
        private readonly IProjectsPersistentSource _projectsPersistentSource;

        public MakeLikeProdController(ILog logger,
            IDeployLibrary deployLibrary, IEnvironmentsPersistentSource environmentsPersistentSource,
            ISecurityPrivilegesChecker securityPrivilegesChecker, IEnvBackups envBackups,
            IActiveDirectorySearcher activeDirectorySearcher,
            IBundledRequestsPersistentSource bundledRequestsPersistentSource,
            [FromKeyedServices("BundledRequestVariableResolver")] IVariableResolver variableResolver,
            IBundledRequestVariableLoader bundledRequestVariableLoader, IProjectsPersistentSource projectsPersistentSource)
        {
            _projectsPersistentSource = projectsPersistentSource;
            _bundledRequestVariableLoader = bundledRequestVariableLoader;
            _variableResolver = variableResolver;
            _bundledRequestsPersistentSource = bundledRequestsPersistentSource;
            _activeDirectorySearcher = activeDirectorySearcher;
            _envBackups = envBackups;
            _securityPrivilegesChecker = securityPrivilegesChecker;
            _environmentsPersistentSource = environmentsPersistentSource;
            _deployLibrary = deployLibrary;
            _logger = logger;
        }

        /// <summary>
        /// Get the data backups available for the source environment
        /// </summary>
        /// <param name="projectId"></param>
        /// <returns></returns>
        [HttpGet]
        [Produces(typeof(IEnumerable<string>))]
        [Route("DataBackups")]
        public IActionResult GetDataBackups(int projectId)
        {
            try
            {
                var projectApiModel = _projectsPersistentSource.GetProject(projectId);

                if (projectApiModel == null)
                    return StatusCode(StatusCodes.Status404NotFound, "Project not found");

                if (projectApiModel.SourceDatabase == null)
                    return StatusCode(StatusCodes.Status404NotFound, "Project has no source database set");

                var dataBackups = new List<string> { "Live Snap" };

                dataBackups.AddRange(
                    from object availSnap in _envBackups.GetSnapsOfStatus(
                        projectApiModel.SourceDatabase.ServerName, "Available")
                    select "Staging Snap: " + availSnap);

                return Ok(dataBackups);
            }
            catch (Exception e)
            {
                _logger.Error(e);
                return StatusCode(StatusCodes.Status500InternalServerError, e.Message);
            }
        }

        /// <summary>
        /// Get the email address of the user
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Produces(typeof(string))]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(string))]
        [Route("NotifyEmailAddress")]
        public IResult GetNotifyEmailAddress()
        {
            try
            {
                var email = GetUserEmail(User);

                return Results.Ok(email);
            }
            catch (Exception e)
            {
                _logger.Error(e);
                return Results.Problem(e.Message, statusCode: StatusCodes.Status500InternalServerError);
            }
        }


        /// <summary>
        /// Get the list of request bundles
        /// </summary>
        /// <param name="projectNames"></param>
        /// <returns></returns>
        [HttpGet]
        [Produces(typeof(IEnumerable<BundledRequestsApiModel>))]
        [Route("BundledRequests")]
        public IActionResult GetBundledRequests([FromQuery] List<string> projectNames)
        {
            try
            {
                List<BundledRequestsApiModel> output = new();
                foreach (var p in projectNames)
                {
                    output.AddRange(_bundledRequestsPersistentSource.GetBundles(p));
                }

                return Ok(output);
            }
            catch (Exception ex)
            {
                string error = $"Error while locating Bundled Requests for project {projectNames}";
                _logger.Error(error, ex);
                return BadRequest(error + " - " + ex);
            }
        }


        /// <summary>
        /// Make Like Production a target environment
        /// </summary>
        /// <param name="mlpRequest"></param>
        /// <returns></returns>
        [HttpPut]
        public IActionResult Put([FromBody] MakeLikeProdRequest mlpRequest)
        {
            try
            {
                if (!_securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(User, mlpRequest.TargetEnv))
                    return StatusCode(StatusCodes.Status403Forbidden, "You are not the owner of this environment or a DOrc Admin");

                if (_environmentsPersistentSource.GetEnvironment(mlpRequest.TargetEnv).EnvironmentIsProd)
                    return StatusCode(StatusCodes.Status403Forbidden, "You cannot make a production environment like another production environment");

                var requestsForBundle = _bundledRequestsPersistentSource.GetRequestsForBundle(mlpRequest.BundleName).OrderBy(br => br.Sequence);

                _variableResolver.SetPropertyValue("CreatedByUserEmail", this.GetUserEmail(User));
                _variableResolver.SetPropertyValue("DataBackup", mlpRequest.DataBackup);
                _variableResolver.SetPropertyValue("TargetEnvironmentName", mlpRequest.TargetEnv);

                foreach (var mlpRequestBundleProperty in mlpRequest.BundleProperties)
                {
                    _variableResolver.SetPropertyValue(mlpRequestBundleProperty.PropertyName, mlpRequestBundleProperty.PropertyValue);
                }

                var initialRequestIdNotSet = true;

                foreach (var req in requestsForBundle)
                {
                    var reqIds = new List<int>();

                    switch (req.Type)
                    {
                        case BundledRequestType.JobRequest:
                            var job = System.Text.Json.JsonSerializer.Deserialize<RequestDto>(req.Request);

                            _bundledRequestVariableLoader.SetVariables(job.RequestProperties.ToList());
                            _variableResolver.LoadProperties();
                            List<RequestProperty> variables = new();
                            foreach (var variable in job.RequestProperties)
                            {
                                variables.Add(new RequestProperty
                                {
                                    PropertyName = variable.PropertyName,
                                    PropertyValue = _variableResolver.GetPropertyValue(variable.PropertyName).Value
                                        .ToString()
                                });
                            }

                            reqIds.Add(_deployLibrary.SubmitRequest(job.Project, mlpRequest.TargetEnv, job.BuildUrl,
                                job.BuildText, job.Components.ToList(), variables, User));
                            break;
                        case BundledRequestType.CopyEnvBuild:
                            var copyEnvBuildRequest = System.Text.Json.JsonSerializer.Deserialize<CopyEnvBuildRequest>(req.Request);
                            if (copyEnvBuildRequest != null)
                            {
                                if (copyEnvBuildRequest.Components.Any())
                                {
                                    reqIds.AddRange(_deployLibrary.CopyEnvBuildWithComponentIds(copyEnvBuildRequest.SourceEnvironmentName,
                                        mlpRequest.TargetEnv, copyEnvBuildRequest.ProjectName,
                                        copyEnvBuildRequest.Components.ToArray(), User));
                                }
                                else
                                {
                                    reqIds.AddRange(_deployLibrary.CopyEnvBuildAllComponents(copyEnvBuildRequest.SourceEnvironmentName,
                                        mlpRequest.TargetEnv, copyEnvBuildRequest.ProjectName,
                                        User));
                                }
                            }
                            break;
                    }

                    if (initialRequestIdNotSet)
                    {
                        _variableResolver.SetPropertyValue("StartingRequestId", reqIds.First().ToString());
                        initialRequestIdNotSet = false;
                    }
                }

                return Ok("The requests have been passed to DOrc");
            }
            catch (Exception e)
            {
                _logger.Error(e);
                return StatusCode(StatusCodes.Status500InternalServerError, e);
            }
        }

        private string GetUserEmail(ClaimsPrincipal user)
        {
            string? userName = user.Identity?.Name.Split("\\")[1];

            var directoryEntry = _activeDirectorySearcher.GetUserIdActiveDirectory(userName);

            var email = directoryEntry.Email;
            return email;
        }
    }
}