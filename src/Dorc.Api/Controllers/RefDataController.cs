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

        public RefDataController(
            IManageProjectsPersistentSource manageProjectsPersistentSource,
            IProjectsPersistentSource projectsPersistentSource,
            ISecurityPrivilegesChecker securityPrivilegesChecker,
            IClaimsPrincipalReader claimsPrincipalReader
            )
        {
            _securityPrivilegesChecker = securityPrivilegesChecker;
            _projectsPersistentSource = projectsPersistentSource;
            _manageProjectsPersistentSource = manageProjectsPersistentSource;
            _claimsPrincipalReader = claimsPrincipalReader;
        }

        /// <summary>
        /// Get component tree for a project
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(RefDataApiModel))]
        [HttpGet]
        [Route("{id}")]
        public IActionResult Get(string id)
        {
            var projectId = Convert.ToInt32(id);

            var project = _projectsPersistentSource.GetProject(projectId);
            var components =
                _manageProjectsPersistentSource.GetOrderedComponents(projectId);

            var refData = new RefDataApiModel { Components = components, Project = project };

            return StatusCode(StatusCodes.Status200OK, refData);
        }

        /// <summary>
        /// Edit component tree for a project
        /// </summary>
        /// <param name="refData"></param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(RefDataApiModel))]
        [SwaggerResponse(StatusCodes.Status400BadRequest, Type = typeof(string))]
        [HttpPut]
        public IActionResult Put(RefDataApiModel refData)
        {
            if (!_securityPrivilegesChecker.CanModifyProject(User, refData.Project.ProjectId))
                return StatusCode(StatusCodes.Status403Forbidden, "User does not have Modify rights on this Project");

            try
            {
                string username = _claimsPrincipalReader.GetUserFullDomainName(User);
                _manageProjectsPersistentSource.InsertRefDataAudit(username, HttpRequestType.Put,
                    refData);
                _projectsPersistentSource.ValidateProject(refData.Project, HttpRequestType.Put);

                _manageProjectsPersistentSource.ValidateComponents(refData.Components, refData.Project.ProjectId,
                    HttpRequestType.Put);

                _projectsPersistentSource.UpdateProject(refData.Project);

                var updateAndInsertComponents =
                    _manageProjectsPersistentSource.UpdateComponent;
                updateAndInsertComponents += _manageProjectsPersistentSource.CreateComponent;

                _manageProjectsPersistentSource.TraverseComponents(refData.Components, null,
                    refData.Project.ProjectId, updateAndInsertComponents);

                var flattenedComponents = new List<ComponentApiModel>();
                _manageProjectsPersistentSource.FlattenApiComponents(refData.Components, flattenedComponents);

                _manageProjectsPersistentSource.DeleteComponents(flattenedComponents,
                    refData.Project.ProjectId);

                refData.Components = _manageProjectsPersistentSource.GetOrderedComponents(refData.Project.ProjectId);

                refData.Project = _projectsPersistentSource.GetProject(refData.Project.ProjectId);

                return StatusCode(StatusCodes.Status200OK, refData);
            }
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

            _projectsPersistentSource.ValidateProject(refData.Project, HttpRequestType.Post);

            _manageProjectsPersistentSource.ValidateComponents(refData.Components, refData.Project.ProjectId,
                HttpRequestType.Post);

            _projectsPersistentSource.InsertProject(refData.Project);

            _manageProjectsPersistentSource.TraverseComponents(refData.Components, null,
                refData.Project.ProjectId, _manageProjectsPersistentSource.CreateComponent);

            refData.Components = _manageProjectsPersistentSource.GetOrderedComponents(refData.Project.ProjectId);

            refData.Project = _projectsPersistentSource.GetProject(refData.Project.ProjectId);

            string username = _claimsPrincipalReader.GetUserFullDomainName(User);
            _manageProjectsPersistentSource.InsertRefDataAudit(username, HttpRequestType.Post,
                refData);

            return StatusCode(StatusCodes.Status200OK, refData);
        }
    }
}