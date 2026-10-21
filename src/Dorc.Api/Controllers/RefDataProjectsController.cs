using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Sources.Interfaces;
using Dorc.PersistentData.Sources;
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
        private readonly IDeploymentDefinitions _definitions;

        public RefDataProjectsController(
            IProjectsPersistentSource projectsPersistentSource,
            IAccessControlPersistentSource accessControlPersistentSource,
            IActiveDirectorySearcher activeDirectorySearcher, IRolePrivilegesChecker rolePrivilegesChecker,
            ISecurityPrivilegesChecker securityPrivilegesChecker,
            IClaimsPrincipalReader claimsPrincipalReader,
            IDeploymentDefinitions definitions = null
            )
        {
            _securityPrivilegesChecker = securityPrivilegesChecker;
            _rolePrivilegesChecker = rolePrivilegesChecker;
            _activeDirectorySearcher = activeDirectorySearcher;
            _accessControlPersistentSource = accessControlPersistentSource;
            _projectsPersistentSource = projectsPersistentSource;
            _claimsPrincipalReader = claimsPrincipalReader;
            _definitions = definitions;
        }

        /// <summary>
        /// Get list of Projects
        /// </summary>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(IEnumerable<ProjectApiModel>))]
        [HttpGet]
        public IActionResult Get()
        {
            var projects = _projectsPersistentSource.GetProjects(User).ToList();
            if (_definitions != null && projects.Count > 0)
            {
                var documents = _definitions.GetMany(projects.Select(p => p.ProjectId).ToArray());
                foreach (var project in projects)
                {
                    if (documents.TryGetValue(project.ProjectId, out var doc)) project.Revision = doc.Revision;
                }
            }
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
            if (project != null && _definitions != null) project.Revision = _definitions.Get(project.ProjectId).Revision;
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
            if (project != null && _definitions != null) project.Revision = _definitions.Get(project.ProjectId).Revision;
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
                string username = _claimsPrincipalReader.GetUserLogin(User);
                var adSearch = _activeDirectorySearcher.GetUserData(username);
                var saved = _definitions.CreateProject(project, _claimsPrincipalReader.GetUserFullDomainName(User),
                    new AccessControlApiModel
                    { Sid = adSearch.Sid, Pid = adSearch.Pid, Name = adSearch.DisplayName, Allow = (int)(AccessLevel.Write | AccessLevel.ReadSecrets), Deny = 0 },
                    () => _projectsPersistentSource.ValidateProject(project, HttpRequestType.Post));
                return Ok(saved);
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
                    "Projects can only be updated by Admins or users with write access!");

            if (project.ProjectName == null)
                return StatusCode(StatusCodes.Status403Forbidden, "Project Name can not be null!");

            if (_projectsPersistentSource.ProjectArtifactsUriHttpValid(project) ||
                _projectsPersistentSource.ProjectArtifactsUriFileValid(project))
            {
                try
                {
                    var saved = _definitions.Save(new RefDataApiModel { Project = project, Revision = project.Revision },
                        false, _claimsPrincipalReader.GetUserFullDomainName(User),
                        id => _securityPrivilegesChecker.CanModifyProject(User, id),
                        () => _projectsPersistentSource.ValidateProject(project, HttpRequestType.Put), projectOnly: true);
                    return Ok(saved.Project);
                }
                catch (DefinitionPreconditionException e) { return StatusCode(e.StatusCode, e.Message); }
                catch (UnauthorizedAccessException) { return Forbid(); }
                catch (ArgumentException e) { return BadRequest(e.Message); }
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
        public IActionResult Delete(int projectId, [FromQuery] string? expectedRevision = null)
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

                _definitions.DeleteProject(projectId, expectedRevision!, _claimsPrincipalReader.GetUserFullDomainName(User),
                    id => _rolePrivilegesChecker.IsAdmin(User) ||
                        (_rolePrivilegesChecker.IsPowerUser(User) && _securityPrivilegesChecker.CanModifyProject(User, id)));
                return Ok($"Project {projectName} deleted successfully");
            }
            catch (DefinitionPreconditionException ex) { return StatusCode(ex.StatusCode, ex.Message); }
            catch (UnauthorizedAccessException) { return Forbid(); }
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
