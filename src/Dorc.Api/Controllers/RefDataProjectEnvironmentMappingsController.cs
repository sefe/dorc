using Dorc.Api.Interfaces;
using Dorc.Api.Exceptions;
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
    public class RefDataProjectEnvironmentMappingsController : ControllerBase
    {
        private readonly IProjectsPersistentSource _projectsPersistentSource;
        private readonly ISecurityPrivilegesChecker _securityPrivilegesChecker;
        private readonly IApiServices _apiServices;

        public RefDataProjectEnvironmentMappingsController(ISecurityPrivilegesChecker securityPrivilegesChecker,
            IProjectsPersistentSource projectsPersistentSource, IApiServices apiServices)
        {
            _apiServices = apiServices;
            _projectsPersistentSource = projectsPersistentSource;
            _securityPrivilegesChecker = securityPrivilegesChecker;
        }

        /// <summary>
        ///     Returns Projects with list of Environments
        /// </summary>
        /// <param name="project"></param>
        /// <returns>Json string with array of EnvironmentContentApiModel objects</returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(TemplateApiModel<EnvironmentApiModel>))]
        [HttpGet]
        public IActionResult Get(string project, bool includeRead)
        {
            var envDetails = _projectsPersistentSource.GetProjectEnvironments(project, User, includeRead);
            return envDetails == null
                ? StatusCode(StatusCodes.Status404NotFound)
                : StatusCode(StatusCodes.Status200OK, envDetails);
        }

        /// <summary>
        /// Create new Environment mapping for Project
        /// </summary>
        /// <param name="project"></param>
        /// <param name="environment"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        [SwaggerResponse(StatusCodes.Status200OK)]
        [HttpPost]
        public IActionResult Post(string project, string environment)
        {
            if (string.IsNullOrEmpty(project))
                throw new ArgumentOutOfRangeException(nameof(project), "Project name for mapping missing");

            if (string.IsNullOrEmpty(environment))
                throw new ArgumentOutOfRangeException(nameof(environment), "Environment name(s) for mapping missing");

            if (!_securityPrivilegesChecker.CanModifyProject(User, project))
                return StatusCode(StatusCodes.Status403Forbidden,
                    new NonEnoughRightsException("User doesn't have \"Modify\" permission for this action!"));

            var success = true;
            foreach (var s in environment.Split(';'))
            {
                var result = _projectsPersistentSource.AddEnvironmentMappingToProject(project, s, User);
                if (result == false) success = false;
            }

            return StatusCode(success ? StatusCodes.Status200OK : StatusCodes.Status500InternalServerError);
        }

        /// <summary>
        /// Delete Environment mapping from Project
        /// </summary>
        /// <param name="project"></param>
        /// <param name="environment"></param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK)]
        [HttpDelete]
        public IActionResult Delete(string project, string environment)
        {
            if (!_securityPrivilegesChecker.CanModifyProject(User, project))
                return StatusCode(StatusCodes.Status403Forbidden,
                    new NonEnoughRightsException("User doesn't have \"Modify\" permission for this action!"));

            return StatusCode(
                _projectsPersistentSource.RemoveEnvironmentMappingFromProject(project, environment, User)
                    ? StatusCodes.Status200OK
                    : StatusCodes.Status500InternalServerError);
        }
    }
}