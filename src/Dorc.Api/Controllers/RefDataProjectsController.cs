using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Dorc.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class RefDataProjectsController : ControllerBase
    {
        private readonly IProjectsPersistentSource _projectsPersistentSource;
        private readonly IAccessControlPersistentSource _accessControlPersistentSource;
        private readonly IActiveDirectorySearcher _activeDirectorySearcher;
        private readonly IRolePrivilegesChecker _rolePrivilegesChecker;
        private readonly ISecurityPrivilegesChecker _securityPrivilegesChecker;
        private readonly IClaimsPrincipalReader _claimsPrincipalReader;

        public RefDataProjectsController(
            IProjectsPersistentSource projectsPersistentSource,
            IAccessControlPersistentSource accessControlPersistentSource,
            IActiveDirectorySearcher activeDirectorySearcher, IRolePrivilegesChecker rolePrivilegesChecker,
            ISecurityPrivilegesChecker securityPrivilegesChecker,
            IClaimsPrincipalReader claimsPrincipalReader
            )
        {
            _securityPrivilegesChecker = securityPrivilegesChecker;
            _rolePrivilegesChecker = rolePrivilegesChecker;
            _activeDirectorySearcher = activeDirectorySearcher;
            _accessControlPersistentSource = accessControlPersistentSource;
            _projectsPersistentSource = projectsPersistentSource;
            _claimsPrincipalReader = claimsPrincipalReader;
        }

        /// <summary>
        /// Get list of Projects
        /// </summary>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(IEnumerable<ProjectApiModel>))]
        [HttpGet]
        public IActionResult Get()
        {
            var projects = _projectsPersistentSource.GetProjects(User);
            return StatusCode(StatusCodes.Status200OK, projects);
        }

        /// <summary>
        /// Get Project by Id
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(ProjectApiModel))]
        [HttpGet("ById/{value}")]
        public IActionResult Get(int value)
        {
            var project = _projectsPersistentSource.GetProject(value);
            return StatusCode(StatusCodes.Status200OK, project);
        }

        /// <summary>
        /// Get Project by Project Name
        /// </summary>
        /// <param name="projectName"></param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(ProjectApiModel))]
        [HttpGet("{projectName}")]
        public IActionResult Get(string projectName)
        {
            var project = _projectsPersistentSource.GetProject(projectName);
            return StatusCode(StatusCodes.Status200OK, project);
        }

        /// <summary>
        /// Create new Project
        /// </summary>
        /// <param name="project"></param>
        /// <returns></returns>
        [HttpPost]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(ProjectApiModel))]
        public IActionResult Post(ProjectApiModel project)
        {
            if (!_rolePrivilegesChecker.IsPowerUser(User) && !_rolePrivilegesChecker.IsAdmin(User))
                return StatusCode(StatusCodes.Status403Forbidden,
                    "Projects can only be created by PowerUsers or Admins!");

            if (project.ProjectName == null)
                return StatusCode(StatusCodes.Status400BadRequest, "Project Name can not be null!");

            if (_projectsPersistentSource.GetProject(project.ProjectName) != null)
                return StatusCode(StatusCodes.Status400BadRequest, "Project Already Exists in DOrc!");

            if (_projectsPersistentSource.ProjectArtifactsUriHttpValid(project) ||
                _projectsPersistentSource.ProjectArtifactsUriFileValid(project))
            {
                _projectsPersistentSource.InsertProject(project);

                var securityObject = _projectsPersistentSource.GetSecurityObject(project.ProjectName);

                string username = _claimsPrincipalReader.GetUserLogin(User);
                var adSearch = _activeDirectorySearcher.GetUserData(username);

                _accessControlPersistentSource.AddAccessControl(new AccessControlApiModel
                { Sid = adSearch.Sid, Pid = adSearch.Pid, Name = adSearch.DisplayName, Allow = (int)(AccessLevel.Write | AccessLevel.ReadSecrets), Deny = 0 }, securityObject.ObjectId, User);

                return StatusCode(StatusCodes.Status200OK, _projectsPersistentSource.GetProject(project.ProjectName));
            }
            else
            {
                return StatusCode(StatusCodes.Status400BadRequest, "Project doesn't contain a valid URI!");
            }
        }

        /// <summary>
        /// Edit Project
        /// </summary>
        /// <param name="project"></param>
        /// <returns></returns>
        [HttpPut]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(ProjectApiModel))]
        public IActionResult Put(ProjectApiModel project)
        {
            var projectApiModel = _projectsPersistentSource.GetProject(project.ProjectId);
            if (projectApiModel == null)
                return StatusCode(StatusCodes.Status403Forbidden, "Project needs to already exist in DOrc!");

            if (!_securityPrivilegesChecker.CanModifyProject(User, projectApiModel.ProjectName) &&
                !_rolePrivilegesChecker.IsAdmin(User))
                return StatusCode(StatusCodes.Status403Forbidden,
                    "Projects can only be updated by privileged users or Admins!");

            if (project.ProjectName == null)
                return StatusCode(StatusCodes.Status403Forbidden, "Project Name can not be null!");

            if (_projectsPersistentSource.ProjectArtifactsUriHttpValid(project) ||
                _projectsPersistentSource.ProjectArtifactsUriFileValid(project))
            {
                _projectsPersistentSource.UpdateProject(project);

                return StatusCode(StatusCodes.Status200OK, _projectsPersistentSource.GetProject(project.ProjectName));
            }
            else
            {
                return StatusCode(StatusCodes.Status400BadRequest, "Project doesn't contain a valid URI!");
            }
        }

        /// <summary>
        /// Delete Project
        /// </summary>
        /// <param name="projectId"></param>
        /// <returns></returns>
        [Produces("application/json")]
        [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(string), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
        [HttpDelete("{projectId}")]
        public IActionResult Delete(int projectId)
        {
            string? projectName = null;
            try
            {
                if (!_rolePrivilegesChecker.IsAdmin(User) && !_rolePrivilegesChecker.IsPowerUser(User))
                    return StatusCode(StatusCodes.Status403Forbidden,
                        "Projects can only be deleted by privileged users or Admins!");

                var projectApiModel = _projectsPersistentSource.GetProject(projectId);
                if (projectApiModel == null)
                    return NotFound("Project not found!");

                projectName = projectApiModel.ProjectName;

                if (!(_rolePrivilegesChecker.IsPowerUser(User) && _securityPrivilegesChecker.CanModifyProject(User, projectApiModel.ProjectName))
                    && !_rolePrivilegesChecker.IsAdmin(User))
                    return StatusCode(StatusCodes.Status403Forbidden,
                        "Projects can only be deleted by privileged users or Admins!");

                _projectsPersistentSource.DeleteProject(projectId);
                return Ok($"Project {projectName} deleted successfully");
            }
            catch (ArgumentException ex)
            {
                return NotFound(ex.Message);
            }
            catch (Exception)
            {
                return StatusCode(StatusCodes.Status409Conflict,
                    $"The project \"{projectName}\" cannot be deleted because it is linked to other deployment requests. Please remove those links or update the related requests before trying again.");
            }
        }
    }
}
