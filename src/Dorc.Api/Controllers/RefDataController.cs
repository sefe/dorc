using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Repositories;
using Dorc.PersistentData.Sources;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Dorc.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class RefDataController : ControllerBase
    {
        private readonly IManageProjectsPersistentSource _manageProjectsPersistentSource;
        private readonly IProjectsPersistentSource _projectsPersistentSource;
        private ISecurityPrivilegesChecker _securityPrivilegesChecker;
        private readonly IClaimsPrincipalReader _claimsPrincipalReader;
        private readonly IDeploymentDefinitions _definitions;

        public RefDataController(
            IManageProjectsPersistentSource manageProjectsPersistentSource,
            IProjectsPersistentSource projectsPersistentSource,
            ISecurityPrivilegesChecker securityPrivilegesChecker,
            IClaimsPrincipalReader claimsPrincipalReader,
            IDeploymentDefinitions definitions = null
            )
        {
            _securityPrivilegesChecker = securityPrivilegesChecker;
            _projectsPersistentSource = projectsPersistentSource;
            _manageProjectsPersistentSource = manageProjectsPersistentSource;
            _claimsPrincipalReader = claimsPrincipalReader;
            _definitions = definitions;
        }

        /// <summary>
        /// Get component tree for a project
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(RefDataApiModel))]
        [SwaggerResponse(StatusCodes.Status404NotFound, Type = typeof(string))]
        [HttpGet]
        [Route("{id}")]
        public IActionResult Get(string id)
        {
            if (!int.TryParse(id, out var projectId)) return NotFound("Project not found.");

            RefDataApiModel refData;
            try { refData = _definitions.Get(projectId); }
            catch (ProjectNotFoundException e) { return NotFound(e.Message); }
            refData.CanEdit = _securityPrivilegesChecker.CanModifyProject(User, projectId);
            if (!refData.CanEdit && _projectsPersistentSource.GetProjectEnvironments(refData.Project.ProjectName, User, true)?.Items?.Any() != true)
                return Forbid();
            refData.CanDeploy = _projectsPersistentSource.GetProjectEnvironments(refData.Project.ProjectName, User, false)?.Items?.Any() == true;

            return StatusCode(StatusCodes.Status200OK, refData);
        }

        /// <summary>
        /// Edit component tree for a project
        /// </summary>
        /// <param name="refData"></param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(RefDataApiModel))]
        [SwaggerResponse(StatusCodes.Status400BadRequest, Type = typeof(string))]
        [SwaggerResponse(428, Type = typeof(string))]
        [SwaggerResponse(409, Type = typeof(string))]
        [HttpPut]
        public IActionResult Put(RefDataApiModel refData)
        {
            if (!_securityPrivilegesChecker.CanModifyProject(User, refData.Project.ProjectId))
                return StatusCode(StatusCodes.Status403Forbidden, "User does not have Modify rights on this Project");

            try
            {
                string username = _claimsPrincipalReader.GetUserFullDomainName(User);
                var saved = _definitions.Save(refData, false, username,
                    id => _securityPrivilegesChecker.CanModifyProject(User, id), () =>
                    {
                        _projectsPersistentSource.ValidateProject(refData.Project, HttpRequestType.Put);
                        Validation.TerraformExclusivityValidator.ValidateAll(refData.Components);
                        _manageProjectsPersistentSource.ValidateComponents(refData.Components, refData.Project.ProjectId, HttpRequestType.Put);
                    });
                saved.CanEdit = true;
                saved.CanDeploy = _projectsPersistentSource.GetProjectEnvironments(saved.Project.ProjectName, User, false)?.Items?.Any() == true;
                return Ok(saved);
            }
            catch (DefinitionPreconditionException e) { return StatusCode(e.StatusCode, e.Message); }
            catch (UnauthorizedAccessException) { return Forbid(); }
            catch (Exception e)
            {
                return StatusCode(StatusCodes.Status400BadRequest, e.Message);
            }
        }

        /// <summary>
        /// Create new project and component tree for a project
        /// </summary>
        /// <param name="refData"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(RefDataApiModel))]
        [HttpPost]
        public IActionResult Post(RefDataApiModel refData)
        {
            if (!User.IsInRole("Admin"))
                throw new Exception("User must be part of 'Admin' group to create new Projects");

            try
            {
                var saved = _definitions.Save(refData, true, _claimsPrincipalReader.GetUserFullDomainName(User),
                    id => User.IsInRole("Admin"), () =>
                    {
                        _projectsPersistentSource.ValidateProject(refData.Project, HttpRequestType.Post);
                        Validation.TerraformExclusivityValidator.ValidateAll(refData.Components);
                        _manageProjectsPersistentSource.ValidateComponents(refData.Components, refData.Project.ProjectId, HttpRequestType.Post);
                    });
                saved.CanEdit = true;
                return Ok(saved);
            }
            catch (ArgumentException e) { return BadRequest(e.Message); }
        }
    }
}